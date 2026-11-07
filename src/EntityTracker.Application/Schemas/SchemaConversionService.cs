using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Tracking;
using EntityTracker.Domain;

namespace EntityTracker.Application.Schemas;

/// <summary>One Project's part of a conversion: the schema it gets and the copies linked to it.</summary>
public sealed record SchemaConversionProject(
    Project Project,
    string SchemaName,
    bool IsSourceProject,
    IReadOnlyList<Tracker> Copies);

/// <summary>What converting a Tracker into a schema will do, shown before it is applied.</summary>
public sealed record SchemaConversionPreview(
    Tracker Source,
    int EntityCount,
    int PlaceholderCount,
    int ArchivedCount,
    IReadOnlyList<SchemaConversionProject> Projects)
{
    public SchemaConversionProject SourceProject => Projects.Single(static project => project.IsSourceProject);
    public IReadOnlyList<SchemaConversionProject> OtherProjects =>
        Projects.Where(static project => !project.IsSourceProject).ToArray();
    public int CopyCount => Projects.Sum(static project => project.Copies.Count);
}

/// <summary>The schemas to create and the copies' links, applied together with recycling the source.</summary>
public sealed record SchemaConversionCommit(
    TrackerId SourceTrackerId,
    IReadOnlyList<SchemaContent> Schemas,
    IReadOnlyList<TrackerSchemaLink> Links);

/// <summary>
/// Turns a Tracker used as the source of copies into a schema. The schema gets the Tracker's active entities
/// and the placeholders they depend on, as real schema entities. Its copies in the same Project are linked to
/// it, and each other Project on this machine with copies gets its own copy of the schema. A copy's entities
/// are linked through its Sync from source baseline, and its schema baseline is the source as the copy last
/// synced it, so source changes it hadn't synced yet show up in the first push. The Tracker goes to the
/// recycle bin.
/// </summary>
public sealed class SchemaConversionService(
    IProjectRepository projectRepository,
    ITrackerRepository trackerRepository,
    IEntityRepository entityRepository,
    IDependencyRepository dependencyRepository,
    ISchemaStore schemaStore,
    ITrackerSyncStore syncStore,
    ISchemaLinkStore linkStore,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<SchemaConversionPreview> PreviewAsync(
        TrackerId trackerId, CancellationToken cancellationToken = default) =>
        (await PlanAsync(trackerId, cancellationToken)).Preview;

    /// <summary>Converts the Tracker; returns the schema made in its own Project.</summary>
    public async Task<SchemaId> ConvertAsync(TrackerId trackerId, CancellationToken cancellationToken = default)
    {
        (_, SchemaConversionCommit commit) = await PlanAsync(trackerId, cancellationToken);
        await linkStore.ApplyConversionAsync(commit, cancellationToken);
        return commit.Schemas[0].Schema.Id;
    }

    private async Task<(SchemaConversionPreview Preview, SchemaConversionCommit Commit)> PlanAsync(
        TrackerId trackerId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        Tracker source = await trackerRepository.GetAsync(trackerId, cancellationToken) ??
            throw new InvalidOperationException("The tracker no longer exists.");
        if (source.LifecycleState != CatalogLifecycleState.Active)
            throw new InvalidOperationException("The tracker is in the recycle bin. Restore it first.");
        if (source.SchemaId is not null)
            throw new InvalidOperationException("This tracker was made from a schema. Detach it from its schema first.");
        Project sourceProject = await projectRepository.GetAsync(source.ProjectId, cancellationToken) ??
            throw new InvalidOperationException("The project no longer exists.");
        if (sourceProject.LifecycleState != CatalogLifecycleState.Active)
            throw new InvalidOperationException("The project is recycled.");

        TrackedEntity[] entities = (await entityRepository.GetAllAsync(trackerId, cancellationToken)).ToArray();
        EntityDependency[] dependencies = (await dependencyRepository.GetAllAsync(trackerId, cancellationToken)).ToArray();
        Dictionary<EntityId, TrackedEntity> byId = entities.ToDictionary(static e => e.Id);
        TrackedEntity[] active = entities.Where(static e => e.LifecycleState == EntityLifecycleState.Active).ToArray();
        HashSet<EntityId> activeIds = active.Select(static e => e.Id).ToHashSet();
        // The placeholders active entities depend on become schema entities too; a dependency on an archived
        // entity is left out with it.
        TrackedEntity[] placeholders = dependencies
            .Where(d => activeIds.Contains(d.DependentEntityId))
            .Select(d => byId.GetValueOrDefault(d.DependencyEntityId))
            .OfType<TrackedEntity>()
            .Where(static e => e.IsPlaceholder)
            .DistinctBy(static e => e.Id)
            .ToArray();
        TrackedEntity[] included = [.. active, .. placeholders];
        HashSet<EntityId> includedIds = included.Select(static e => e.Id).ToHashSet();
        EntityDependency[] includedDependencies = dependencies
            .Where(d => activeIds.Contains(d.DependentEntityId) && includedIds.Contains(d.DependencyEntityId))
            .ToArray();

        // Copies in Projects on this machine that are not recycled; the rest keep their dormant link.
        List<(Project Project, Tracker[] Copies)> copyProjects = [];
        foreach (IGrouping<ProjectId, Tracker> group in (await trackerRepository.GetAllAsync(cancellationToken))
                     .Where(t => t.CopiedFromTrackerId == source.Id && t.Id != source.Id && t.SchemaId is null)
                     .GroupBy(static t => t.ProjectId))
        {
            Project? project = await projectRepository.GetAsync(group.Key, cancellationToken);
            if (project?.LifecycleState == CatalogLifecycleState.Active)
                copyProjects.Add((project, group.OrderBy(static t => t.Name, StringComparer.OrdinalIgnoreCase).ToArray()));
        }
        if (copyProjects.All(item => item.Project.Id != sourceProject.Id))
            copyProjects.Insert(0, (sourceProject, []));
        else
        {
            int index = copyProjects.FindIndex(item => item.Project.Id == sourceProject.Id);
            (Project Project, Tracker[] Copies) own = copyProjects[index];
            copyProjects.RemoveAt(index);
            copyProjects.Insert(0, own);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();
        List<SchemaConversionProject> previewProjects = [];
        List<SchemaContent> schemas = [];
        List<TrackerSchemaLink> links = [];
        foreach ((Project project, Tracker[] copies) in copyProjects)
        {
            string name = await UniqueNameAsync(project.Id, source.Name, cancellationToken);
            Schema schema = new(SchemaId.New(), project.Id, name, now, now);
            Dictionary<EntityId, SchemaEntityId> schemaIds = included.ToDictionary(static e => e.Id, static _ => SchemaEntityId.New());
            SchemaContent content = new(schema,
                included.Select(e => new SchemaEntity(schemaIds[e.Id], schema.Id, e.SourceName,
                    e.IsPlaceholder ? string.Empty : e.GroupName)).ToArray(),
                includedDependencies.Select(d => new SchemaDependency(schemaIds[d.DependentEntityId],
                    schemaIds[d.DependencyEntityId])).ToArray());
            schemas.Add(content);
            foreach (Tracker copy in copies)
                links.Add(await LinkAsync(copy, content, included, schemaIds, cancellationToken));
            previewProjects.Add(new SchemaConversionProject(project, name, project.Id == sourceProject.Id, copies));
        }

        return (new SchemaConversionPreview(source, active.Length, placeholders.Length,
                entities.Count(static e => e.LifecycleState == EntityLifecycleState.Archived), previewProjects),
            new SchemaConversionCommit(source.Id, schemas, links));
    }

    /// <summary>
    /// Links a copy's entities to the schema through its Sync from source baseline: a source entity the copy
    /// synced is linked to the copy's entity it was paired with, and its baseline is the source entity as it
    /// was then. A source entity the copy never synced is left out of the baseline, so the first push adds it
    /// or links it by name.
    /// </summary>
    private async Task<TrackerSchemaLink> LinkAsync(Tracker copy, SchemaContent content,
        IReadOnlyList<TrackedEntity> included, IReadOnlyDictionary<EntityId, SchemaEntityId> schemaIds,
        CancellationToken cancellationToken)
    {
        TrackerSyncBaseline? baseline = await syncStore.ReadBaselineAsync(copy.Id, cancellationToken);
        if (baseline is null) return new TrackerSchemaLink(copy.Id, content.Schema.Id, []);

        HashSet<EntityId> copyEntities = (await entityRepository.GetAllAsync(copy.Id, cancellationToken))
            .Where(static e => !e.IsPlaceholder).Select(static e => e.Id).ToHashSet();
        Dictionary<string, TrackerSyncEntity> oldSourceByKey = new(StringComparer.Ordinal);
        Dictionary<Guid, TrackerSyncEntity> oldSourceById = [];
        foreach (TrackerSyncEntity entity in baseline.Source.Entities)
        {
            oldSourceByKey.TryAdd(Key(entity.Name), entity);
            if (entity.Id is { } id) oldSourceById.TryAdd(id, entity);
        }
        Dictionary<string, TrackerSyncEntity> pairedCopy = new(StringComparer.Ordinal);
        foreach (TrackerSyncEntity entity in baseline.Destination.Entities)
            pairedCopy.TryAdd(Key(entity.SourceName ?? entity.Name), entity);

        // Which schema entity a name in the old source refers to: by the source entity's ID where it had one,
        // else by name.
        Dictionary<string, SchemaEntityId> schemaByOldName = new(StringComparer.Ordinal);
        foreach (TrackedEntity entity in included)
        {
            TrackerSyncEntity? old = oldSourceById.GetValueOrDefault(entity.Id.Value) ??
                oldSourceByKey.GetValueOrDefault(Key(entity.SourceName));
            schemaByOldName.TryAdd(Key(old?.Name ?? entity.SourceName), schemaIds[entity.Id]);
        }

        List<(SchemaEntityId Id, EntityId? EntityId, TrackerSyncEntity Old)> linked = [];
        HashSet<EntityId> used = [];
        foreach (TrackedEntity entity in included)
        {
            TrackerSyncEntity? old = oldSourceById.GetValueOrDefault(entity.Id.Value) ??
                oldSourceByKey.GetValueOrDefault(Key(entity.SourceName));
            if (old is null || !old.Active) continue;
            if (pairedCopy.GetValueOrDefault(Key(old.Name)) is not { Id: { } copyId }) continue;
            EntityId target = new(copyId);
            linked.Add((schemaIds[entity.Id], copyEntities.Contains(target) && used.Add(target) ? target : null, old));
        }

        HashSet<SchemaEntityId> inBaseline = linked.Select(static item => item.Id).ToHashSet();
        return new TrackerSchemaLink(copy.Id, content.Schema.Id, linked.Select(item => new SchemaBaselineEntity(
                item.Id, item.EntityId, item.Old.Name, item.Old.GroupName ?? string.Empty,
                item.Old.Dependencies
                    .Select(d => schemaByOldName.GetValueOrDefault(Key(d.Name)))
                    .OfType<SchemaEntityId>()
                    .Where(d => d != item.Id && inBaseline.Contains(d))
                    .Distinct().ToArray()))
            .ToArray());
    }

    private async Task<string> UniqueNameAsync(ProjectId projectId, string name, CancellationToken cancellationToken)
    {
        string candidate = name;
        for (int number = 2; await schemaStore.IsNameReservedAsync(projectId, candidate, null, cancellationToken); number++)
            candidate = $"{name} ({number})";
        return candidate;
    }

    private static string Key(string name) => EntitySourceKey.From(name).Value;
}

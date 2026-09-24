using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tracking;

public sealed record PurgeTrackerRequest(TrackerId TrackerId);

public sealed class TrackerManagementService(
    IProjectRepository projectRepository,
    ITrackerRepository trackerRepository,
    IEntityRepository entityRepository,
    IDependencyRepository dependencyRepository,
    IManualDependencyOverrideRepository overrideRepository,
    IProjectTrackerStore store,
    EffectiveDependencyResolver effectiveDependencyResolver,
    ProgressSnapshotCalculator snapshotCalculator,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<Tracker> CreateBlankAsync(
        ProjectId projectId,
        string name,
        CancellationToken cancellationToken = default)
    {
        await RequireActiveProjectAsync(projectId, cancellationToken);
        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();
        Tracker tracker = new(TrackerId.New(), projectId, name, now, now);
        ProgressSnapshotState baseline = new(0, 0, 0, 0, 0, 0);
        await store.CreateTrackerAsync(
            new TrackerCreationState(
                tracker,
                new TrackedStateChangeSet([], [], [], [], [], []),
                baseline),
            cancellationToken);
        return tracker;
    }

    public async Task<Tracker> CopyAsync(
        TrackerId sourceTrackerId,
        ProjectId destinationProjectId,
        string destinationName,
        CancellationToken cancellationToken = default)
    {
        Tracker source = await RequireActiveTrackerAsync(sourceTrackerId, cancellationToken);
        await RequireActiveProjectAsync(source.ProjectId, cancellationToken);
        await RequireActiveProjectAsync(destinationProjectId, cancellationToken);

        Task<IReadOnlyList<TrackedEntity>> entitiesTask =
            entityRepository.GetAllAsync(sourceTrackerId, cancellationToken);
        Task<IReadOnlyList<PersistedDependency>> resolvedTask =
            dependencyRepository.GetAllAsync(sourceTrackerId, cancellationToken);
        Task<IReadOnlyList<PersistedUnresolvedDependency>> unresolvedTask =
            dependencyRepository.GetAllUnresolvedAsync(sourceTrackerId, cancellationToken);
        Task<IReadOnlyList<ManualDependencyOverride>> overridesTask =
            overrideRepository.GetAllAsync(sourceTrackerId, cancellationToken);
        await Task.WhenAll(entitiesTask, resolvedTask, unresolvedTask, overridesTask);

        TrackedEntity[] sourceEntities = (await entitiesTask).ToArray();
        PersistedDependency[] sourceResolved = (await resolvedTask).ToArray();
        PersistedUnresolvedDependency[] sourceUnresolved = (await unresolvedTask).ToArray();
        ManualDependencyOverride[] sourceOverrides = (await overridesTask).ToArray();
        TrackerStateValidator.EnsureOwned(
            sourceTrackerId,
            sourceEntities,
            sourceResolved,
            sourceUnresolved,
            sourceOverrides);

        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();
        Tracker tracker = new(
            TrackerId.New(),
            destinationProjectId,
            destinationName,
            now,
            now,
            copiedFromTrackerId: sourceTrackerId);
        TrackedEntity[] activeSource = sourceEntities
            .Where(static entity => entity.LifecycleState == EntityLifecycleState.Active)
            .ToArray();
        Dictionary<EntityId, EntityId> idMap = activeSource.ToDictionary(
            static entity => entity.Id,
            static _ => EntityId.New());
        Dictionary<EntityId, TrackedEntity> sourceById = sourceEntities.ToDictionary(
            static entity => entity.Id);
        TrackedEntity[] copiedEntities = activeSource.Select(entity => new TrackedEntity(
            idMap[entity.Id],
            tracker.Id,
            entity.SourceName,
            provenance: EntityProvenance.Copied,
            requestedPriority: entity.RequestedPriority,
            groupName: entity.GroupName)).ToArray();

        List<PersistedDependency> copiedResolved = [];
        List<PersistedUnresolvedDependency> copiedUnresolved = [];
        foreach (PersistedDependency dependency in sourceResolved.Where(dependency =>
                     idMap.ContainsKey(dependency.Edge.DependentEntityId)))
        {
            EntityId copiedOwnerId = idMap[dependency.Edge.DependentEntityId];
            if (idMap.TryGetValue(dependency.Edge.DependencyEntityId, out EntityId? copiedTargetId))
            {
                copiedResolved.Add(new PersistedDependency(
                    new DependencyEdge(copiedOwnerId, copiedTargetId),
                    dependency.Kind));
            }
            else if (sourceById.TryGetValue(
                         dependency.Edge.DependencyEntityId,
                         out TrackedEntity? archivedTarget))
            {
                copiedUnresolved.Add(new PersistedUnresolvedDependency(
                    new UnresolvedDependency(copiedOwnerId, archivedTarget.SourceName),
                    dependency.Kind));
            }
        }

        copiedUnresolved.AddRange(sourceUnresolved
            .Where(dependency => idMap.ContainsKey(dependency.Dependency.DependentEntityId))
            .Select(dependency => new PersistedUnresolvedDependency(
                new UnresolvedDependency(
                    idMap[dependency.Dependency.DependentEntityId],
                    dependency.Dependency.DependencySourceName),
                dependency.Kind)));
        ManualDependencyOverride[] copiedOverrides = sourceOverrides
            .Where(item => idMap.ContainsKey(item.DependentEntityId))
            .Select(item => new ManualDependencyOverride(
                idMap[item.DependentEntityId],
                item.DependencySourceName,
                item.Action))
            .ToArray();
        EffectiveDependencyState effective = effectiveDependencyResolver.Resolve(
            copiedEntities,
            copiedResolved,
            copiedUnresolved,
            copiedOverrides);
        ProgressSnapshotState baseline = snapshotCalculator.Calculate(copiedEntities, effective);
        EntityId[] ownerIds = copiedEntities.Select(static entity => entity.Id).ToArray();
        TrackedStateChangeSet changeSet = new(
            copiedEntities,
            [],
            [],
            ownerIds,
            copiedResolved,
            copiedUnresolved,
            ownerIds,
            copiedOverrides,
            progressSnapshotAfterChanges: baseline);
        await store.CreateTrackerAsync(
            new TrackerCreationState(tracker, changeSet, baseline),
            cancellationToken);
        return tracker;
    }

    public async Task RenameAsync(TrackerId trackerId, string name, CancellationToken cancellationToken = default)
    {
        await RequireTrackerAsync(trackerId, cancellationToken);
        await store.RenameTrackerAsync(trackerId, name, cancellationToken);
    }

    public async Task RecycleAsync(TrackerId trackerId, CancellationToken cancellationToken = default)
    {
        await RequireTrackerAsync(trackerId, cancellationToken);
        await store.SetTrackerLifecycleAsync(trackerId, CatalogLifecycleState.Recycled, cancellationToken);
    }

    public async Task RestoreAsync(TrackerId trackerId, CancellationToken cancellationToken = default)
    {
        Tracker tracker = await RequireTrackerAsync(trackerId, cancellationToken);
        await RequireActiveProjectAsync(tracker.ProjectId, cancellationToken);
        await store.SetTrackerLifecycleAsync(trackerId, CatalogLifecycleState.Active, cancellationToken);
    }

    public async Task PurgeAsync(PurgeTrackerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Tracker tracker = await RequireTrackerAsync(request.TrackerId, cancellationToken);
        if (tracker.LifecycleState != CatalogLifecycleState.Recycled)
        {
            throw new InvalidOperationException("A tracker must be recycled before it can be purged.");
        }

        await store.PurgeTrackerAsync(request.TrackerId, cancellationToken);
    }

    private async Task<Project> RequireActiveProjectAsync(ProjectId projectId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        Project project = await projectRepository.GetAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("The project no longer exists.");
        if (project.LifecycleState != CatalogLifecycleState.Active)
        {
            throw new InvalidOperationException("The project is recycled.");
        }

        return project;
    }

    private async Task<Tracker> RequireActiveTrackerAsync(TrackerId trackerId, CancellationToken cancellationToken)
    {
        Tracker tracker = await RequireTrackerAsync(trackerId, cancellationToken);
        if (tracker.LifecycleState != CatalogLifecycleState.Active)
        {
            throw new InvalidOperationException("The source tracker is recycled.");
        }

        return tracker;
    }

    private async Task<Tracker> RequireTrackerAsync(TrackerId trackerId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        return await trackerRepository.GetAsync(trackerId, cancellationToken)
            ?? throw new InvalidOperationException("The tracker no longer exists.");
    }
}

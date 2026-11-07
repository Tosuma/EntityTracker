using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Ranking;
using EntityTracker.Domain;

namespace EntityTracker.Application.Schemas;

/// <summary>
/// Trackers made from a schema: creating one, detaching it, and pushing the schema's later changes to every
/// Tracker linked to it.
/// </summary>
public sealed class SchemaTrackerService(
    IProjectRepository projectRepository,
    ITrackerRepository trackerRepository,
    IEntityRepository entityRepository,
    IDependencyRepository dependencyRepository,
    ISchemaStore schemaStore,
    ISchemaLinkStore linkStore,
    IProjectTrackerStore trackerStore,
    EffectiveDependencyResolver resolver,
    ProgressSnapshotCalculator snapshotCalculator,
    IDependencyRankingService ranker,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// Creates a Tracker with the schema's entities, all Not started, with their groups and dependencies,
    /// linked to the schema.
    /// </summary>
    public async Task<Tracker> CreateTrackerAsync(
        SchemaId schemaId, string name, CancellationToken cancellationToken = default)
    {
        SchemaContent content = await RequireActiveSchemaAsync(schemaId, cancellationToken);
        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();
        Tracker tracker = new(TrackerId.New(), content.Schema.ProjectId, name, now, now, schemaId: schemaId);
        TrackerPushOutcome outcome = SchemaPushPlanner.Plan(content, tracker, [], [], null, SchemaPushPlanner.UseSchema);
        TrackedEntity[] entities = outcome.ChangeSet.EntitiesToAdd.ToArray();
        EntityDependency[] dependencies = outcome.ChangeSet.Dependencies.ToArray();
        ProgressSnapshotState snapshot = snapshotCalculator.Calculate(entities,
            resolver.Resolve(entities, dependencies));
        TrackedStateChangeSet changes = new(entities, [], [],
            entities.Select(static entity => entity.Id).ToArray(), dependencies,
            progressSnapshotAfterChanges: snapshot);
        await trackerStore.CreateTrackerAsync(
            new TrackerCreationState(tracker, changes, snapshot, SchemaLink: outcome.Link), cancellationToken);
        return tracker;
    }

    /// <summary>Makes the Tracker independent: it keeps its entities and progress, and leaves the schema.</summary>
    public async Task DetachAsync(TrackerId trackerId, CancellationToken cancellationToken = default)
    {
        Tracker tracker = await trackerRepository.GetAsync(trackerId, cancellationToken) ??
            throw new InvalidOperationException("The tracker no longer exists.");
        if (tracker.SchemaId is null)
            throw new InvalidOperationException("This tracker isn't linked to a schema.");
        await linkStore.DetachAsync(trackerId, cancellationToken);
    }

    /// <summary>The schema's changes since it was last pushed to the Trackers linked to it.</summary>
    public async Task<SchemaPendingChanges> GetPendingChangesAsync(
        SchemaId schemaId, CancellationToken cancellationToken = default)
    {
        SchemaContent content = await schemaStore.GetContentAsync(schemaId, cancellationToken) ??
            throw new InvalidOperationException("The schema no longer exists.");
        TrackerSchemaLink[] links = await ActiveLinksAsync(schemaId, cancellationToken);
        List<string> changes = [];
        int behind = 0;
        foreach (TrackerSchemaLink link in links)
        {
            IReadOnlyList<string> trackerChanges = SchemaPushPlanner.DescribeChanges(content, link);
            if (trackerChanges.Count > 0) behind++;
            foreach (string change in trackerChanges)
                if (!changes.Contains(change, StringComparer.Ordinal)) changes.Add(change);
        }
        return new SchemaPendingChanges(changes, links.Length, behind);
    }

    /// <summary>
    /// Reviews a push to every active Tracker linked to the schema: each change the Tracker doesn't have yet,
    /// all undecided.
    /// </summary>
    public async Task<SchemaPushReview> ReviewPushAsync(
        SchemaId schemaId, CancellationToken cancellationToken = default)
    {
        string schemaFingerprint = await linkStore.FingerprintSchemaAsync(schemaId, cancellationToken);
        SchemaContent content = await RequireActiveSchemaAsync(schemaId, cancellationToken);
        List<TrackerPushPlan> plans = [];
        foreach (TrackerSchemaLink link in await ActiveLinksAsync(schemaId, cancellationToken))
        {
            (Tracker tracker, string fingerprint, TrackerSchemaLink current, TrackerPushOutcome outcome) =
                await PlanAsync(content, link.TrackerId, SchemaPushPlanner.Undecided, cancellationToken);
            plans.Add(new TrackerPushPlan(tracker, fingerprint, outcome.Changes, !SameLink(current, outcome.Link)));
        }
        if (schemaFingerprint != await linkStore.FingerprintSchemaAsync(schemaId, cancellationToken))
            throw new InvalidOperationException("The schema changed while the review was loading. Open it again.");
        return new SchemaPushReview(content, schemaFingerprint,
            plans.OrderBy(static plan => plan.Tracker.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    /// <summary>
    /// Applies a reviewed push to all its Trackers at once: the changes decided for each, while undecided ones
    /// wait for a later push. Nothing is written if the schema or a Tracker changed since the review opened.
    /// </summary>
    public async Task ApplyPushAsync(SchemaPushReview review, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (!review.CanApply)
            throw new InvalidOperationException("Choose Use schema or Keep tracker for at least one change before pushing.");
        SchemaId schemaId = review.Schema.Schema.Id;
        await RequireActiveSchemaAsync(schemaId, cancellationToken);
        List<TrackerPushCommit> commits = [];
        Func<SchemaPushChange, SchemaPushChoice?> choices = SchemaPushPlanner.ChoicesOf(review.Changes);
        foreach (TrackerPushPlan plan in review.Trackers)
        {
            // A Tracker with nothing decided, and a baseline that doesn't move by itself, is left as it is.
            if (!plan.HasBaselineUpdate && plan.Changes.All(static change => change.Choice is null)) continue;
            (_, string fingerprint, _, TrackerPushOutcome outcome) =
                await PlanAsync(review.Schema, plan.Tracker.Id, choices, cancellationToken);
            if (fingerprint != plan.Fingerprint)
                throw new InvalidOperationException(
                    $"{plan.Tracker.Name} changed after this review opened. Open a new review.");
            EffectiveDependencyState effective = resolver.Resolve(outcome.ResultEntities, outcome.ResultDependencies);
            var ranking = ranker.Rank(
                outcome.ResultEntities.Where(static entity => entity.LifecycleState == EntityLifecycleState.Active),
                effective.ResolvedDependencies.Select(static item => item.Edge),
                effective.UnresolvedDependencies.Select(static item => item.Dependency));
            if (!ranking.IsSuccess)
                throw new InvalidOperationException($"The push would leave {plan.Tracker.Name} with a dependency loop: " +
                    string.Join(" ", ranking.Diagnostics.Select(static item => item.Message)));
            TrackedStateChangeSet changes = outcome.ChangeSet;
            TrackedStateChangeSet withSnapshot = new(changes.EntitiesToAdd, changes.EntitiesToUpdate,
                changes.EntityIdsToArchive, changes.DependencyOwnerIds, changes.Dependencies,
                entitiesWithGroupNameToUpdate: changes.EntitiesWithGroupNameToUpdate,
                progressSnapshotAfterChanges: snapshotCalculator.Calculate(outcome.ResultEntities, effective));
            commits.Add(new TrackerPushCommit(plan.Tracker.Id, plan.Fingerprint, withSnapshot, outcome.Link));
        }
        await linkStore.ApplyPushAsync(new SchemaPushCommit(schemaId, review.SchemaFingerprint, commits),
            cancellationToken);
    }

    private async Task<(Tracker, string, TrackerSchemaLink, TrackerPushOutcome)> PlanAsync(SchemaContent content,
        TrackerId trackerId, Func<SchemaPushChange, SchemaPushChoice?> choices, CancellationToken cancellationToken)
    {
        string fingerprint = await linkStore.FingerprintTrackerAsync(trackerId, cancellationToken);
        Tracker tracker = await trackerRepository.GetAsync(trackerId, cancellationToken) ??
            throw new InvalidOperationException("A linked tracker no longer exists. Open a new review.");
        TrackerSchemaLink link = await linkStore.GetLinkAsync(trackerId, cancellationToken) ??
            throw new InvalidOperationException($"{tracker.Name} was detached from the schema. Open a new review.");
        TrackedEntity[] entities = (await entityRepository.GetAllAsync(trackerId, cancellationToken)).ToArray();
        EntityDependency[] dependencies = (await dependencyRepository.GetAllAsync(trackerId, cancellationToken)).ToArray();
        if (fingerprint != await linkStore.FingerprintTrackerAsync(trackerId, cancellationToken))
            throw new InvalidOperationException($"{tracker.Name} changed while the review was loading. Open it again.");
        return (tracker, fingerprint, link,
            SchemaPushPlanner.Plan(content, tracker, entities, dependencies, link, choices));
    }

    private static bool SameLink(TrackerSchemaLink a, TrackerSchemaLink b) =>
        a.SchemaId == b.SchemaId && a.Entities.Count == b.Entities.Count &&
        a.Entities.OrderBy(static e => e.SchemaEntityId.Value).Zip(b.Entities.OrderBy(static e => e.SchemaEntityId.Value))
            .All(static pair => pair.First.SchemaEntityId == pair.Second.SchemaEntityId &&
                                pair.First.EntityId == pair.Second.EntityId &&
                                pair.First.Name == pair.Second.Name &&
                                pair.First.GroupName == pair.Second.GroupName &&
                                pair.First.Dependencies.Select(static d => d.Value).Order()
                                    .SequenceEqual(pair.Second.Dependencies.Select(static d => d.Value).Order()));

    /// <summary>The links of the schema's active Trackers. A recycled Tracker catches up when it is next pushed to.</summary>
    private async Task<TrackerSchemaLink[]> ActiveLinksAsync(SchemaId schemaId, CancellationToken cancellationToken)
    {
        List<TrackerSchemaLink> active = [];
        foreach (TrackerSchemaLink link in await linkStore.GetLinksAsync(schemaId, cancellationToken))
            if ((await trackerRepository.GetAsync(link.TrackerId, cancellationToken))?.LifecycleState ==
                CatalogLifecycleState.Active)
                active.Add(link);
        return active.ToArray();
    }

    private async Task<SchemaContent> RequireActiveSchemaAsync(SchemaId schemaId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schemaId);
        SchemaContent content = await schemaStore.GetContentAsync(schemaId, cancellationToken) ??
            throw new InvalidOperationException("The schema no longer exists.");
        if (content.Schema.LifecycleState != CatalogLifecycleState.Active)
            throw new InvalidOperationException("The schema is in the recycle bin. Restore it first.");
        Project project = await projectRepository.GetAsync(content.Schema.ProjectId, cancellationToken) ??
            throw new InvalidOperationException("The project no longer exists.");
        if (project.LifecycleState != CatalogLifecycleState.Active)
            throw new InvalidOperationException("The project is recycled.");
        return content;
    }
}

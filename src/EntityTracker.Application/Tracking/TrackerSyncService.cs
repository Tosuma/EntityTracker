using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Ranking;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tracking;

public sealed class TrackerSyncService(
    IProjectRepository projectRepository,
    ITrackerRepository trackerRepository,
    IEntityRepository entityRepository,
    IDependencyRepository dependencyRepository,
    IManualDependencyOverrideRepository overrideRepository,
    ITrackerSyncStore store,
    EffectiveDependencyResolver resolver,
    ProgressSnapshotCalculator snapshotCalculator,
    IDependencyRankingService ranker)
{
    public async Task<TrackerSyncReview> ReviewAsync(
        TrackerId destinationId, CancellationToken cancellationToken = default)
    {
        Tracker destination = await RequireActiveAsync(destinationId, cancellationToken);
        TrackerId sourceId = destination.CopiedFromTrackerId ??
            throw new InvalidOperationException("This tracker was not copied from another tracker.");
        await RequireActiveAsync(sourceId, cancellationToken);
        string sourceFingerprint = await store.FingerprintAsync(sourceId, cancellationToken);
        string destinationFingerprint = await store.FingerprintAsync(destinationId, cancellationToken);
        (TrackerSyncStructure source, _, _, _, _) = await ReadStructureAsync(sourceId, cancellationToken);
        (TrackerSyncStructure target, _, _, _, _) = await ReadStructureAsync(destinationId, cancellationToken);
        TrackerSyncBaseline? baseline = await store.ReadBaselineAsync(destinationId, cancellationToken);
        if (sourceFingerprint != await store.FingerprintAsync(sourceId, cancellationToken) ||
            destinationFingerprint != await store.FingerprintAsync(destinationId, cancellationToken))
            throw new InvalidOperationException("A tracker changed while loading the review. Open sync again.");
        return TrackerSyncPlanner.CreateReview(sourceId, destinationId,
            source, target, baseline, sourceFingerprint, destinationFingerprint);
    }

    public async Task ApplyAsync(TrackerSyncReview review, CancellationToken cancellationToken = default)
    {
        (TrackerSyncCommit commit, _) = await PrepareAsync(review, cancellationToken);
        await store.ApplyAsync(commit, cancellationToken);
    }

    public async Task<TrackerSyncPreview> PreviewAsync(
        TrackerSyncReview review, CancellationToken cancellationToken = default)
    {
        (_, TrackerSyncPreview preview) = await PrepareAsync(review, cancellationToken);
        return preview;
    }

    private async Task<(TrackerSyncCommit, TrackerSyncPreview)> PrepareAsync(
        TrackerSyncReview review, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (!review.CanApply)
            throw new InvalidOperationException("Choose a result for every sync change before applying.");
        Tracker destination = await RequireActiveAsync(review.DestinationTrackerId, cancellationToken);
        if (destination.CopiedFromTrackerId != review.SourceTrackerId)
            throw new InvalidOperationException("The recorded source tracker changed. Open a new review.");
        await RequireActiveAsync(review.SourceTrackerId, cancellationToken);

        TrackerSyncStructure selected = TrackerSyncPlanner.Resolve(review);
        (_, TrackedEntity[] existing, PersistedDependency[] existingResolved,
            PersistedUnresolvedDependency[] existingUnresolved,
            ManualDependencyOverride[] existingOverrides) =
            await ReadStructureAsync(review.DestinationTrackerId, cancellationToken);
        ProgressSnapshotState currentProgress = snapshotCalculator.Calculate(existing,
            resolver.Resolve(existing, existingResolved, existingUnresolved, existingOverrides));
        var existingByKey = existing.ToDictionary(static entity => TrackerSyncPlanner.Key(entity.SourceName));
        var currentByKey = review.Destination.Entities.ToDictionary(
            static entity => TrackerSyncPlanner.Key(entity.Name));
        var selectedByKey = selected.Entities.ToDictionary(
            static entity => TrackerSyncPlanner.Key(entity.Name));
        List<TrackedEntity> additions = [];
        List<EntityId> archives = [];
        List<EntityId> restores = [];
        List<TrackedEntity> priorityUpdates = [];
        List<TrackedEntity> groupUpdates = [];
        foreach (TrackerSyncEntity wanted in selected.Entities)
        {
            string key = TrackerSyncPlanner.Key(wanted.Name);
            if (!existingByKey.TryGetValue(key, out TrackedEntity? entity))
            {
                if (!wanted.Active) continue;
                entity = new TrackedEntity(EntityId.New(), review.DestinationTrackerId,
                    wanted.Name, provenance: EntityProvenance.Copied,
                    requestedPriority: wanted.RequestedPriority, groupName: wanted.GroupName);
                existingByKey.Add(key, entity);
                additions.Add(entity);
                continue;
            }
            if (wanted.Active && entity.LifecycleState == EntityLifecycleState.Archived)
                restores.Add(entity.Id);
            if (!wanted.Active && entity.LifecycleState == EntityLifecycleState.Active)
                archives.Add(entity.Id);
            if (entity.RequestedPriority != wanted.RequestedPriority)
            {
                entity.ChangeRequestedPriority(wanted.RequestedPriority);
                priorityUpdates.Add(entity);
            }
            if (!string.Equals(entity.GroupName, wanted.GroupName, StringComparison.Ordinal))
            {
                entity.ChangeGroupName(wanted.GroupName);
                groupUpdates.Add(entity);
            }
        }

        List<EntityId> reconciled = [];
        List<PersistedDependency> desiredResolved = [];
        List<PersistedUnresolvedDependency> desiredUnresolved = [];
        List<ManualDependencyOverride> desiredOverrides = [];
        var existingById = existing.ToDictionary(static entity => entity.Id);
        foreach (TrackerSyncEntity wanted in selected.Entities.Where(static item => item.Active))
        {
            string key = TrackerSyncPlanner.Key(wanted.Name);
            currentByKey.TryGetValue(key, out TrackerSyncEntity? current);
            if (current is not null && SameDependencies(current.Dependencies, wanted.Dependencies))
                continue;
            EntityId ownerId = existingByKey[key].Id;
            reconciled.Add(ownerId);
            var wantedTargets = wanted.Dependencies.ToDictionary(
                static item => TrackerSyncPlanner.Key(item.Name));
            ManualDependencyOverride[] retainedOverrides = existingOverrides
                .Where(item => item.DependentEntityId == ownerId)
                .Where(item =>
                {
                    bool present = wantedTargets.TryGetValue(
                        TrackerSyncPlanner.Key(item.DependencySourceName), out TrackerSyncDependency? dependency);
                    return item.Action == ManualDependencyOverrideAction.Add
                        ? present && dependency!.Kind == Importing.ImportedDependencyKind.Mandatory
                        : !present;
                }).ToArray();
            desiredOverrides.AddRange(retainedOverrides);
            HashSet<string> manuallyAdded = retainedOverrides
                .Where(static item => item.Action == ManualDependencyOverrideAction.Add)
                .Select(static item => TrackerSyncPlanner.Key(item.DependencySourceName))
                .ToHashSet();
            HashSet<string> manuallySuppressed = retainedOverrides
                .Where(static item => item.Action == ManualDependencyOverrideAction.Suppress)
                .Select(static item => TrackerSyncPlanner.Key(item.DependencySourceName))
                .ToHashSet();
            foreach (TrackerSyncDependency dependency in wanted.Dependencies)
            {
                string targetKey = TrackerSyncPlanner.Key(dependency.Name);
                if (manuallyAdded.Contains(targetKey)) continue;
                if (selectedByKey.TryGetValue(targetKey, out TrackerSyncEntity? target) &&
                    target.Active && existingByKey.TryGetValue(targetKey, out TrackedEntity? targetEntity))
                {
                    desiredResolved.Add(new PersistedDependency(
                        new DependencyEdge(ownerId, targetEntity.Id), dependency.Kind));
                }
                else
                {
                    desiredUnresolved.Add(new PersistedUnresolvedDependency(
                        new UnresolvedDependency(ownerId, dependency.Name), dependency.Kind));
                }
            }
            desiredResolved.AddRange(existingResolved.Where(item =>
                item.Edge.DependentEntityId == ownerId &&
                existingById.TryGetValue(item.Edge.DependencyEntityId, out TrackedEntity? target) &&
                manuallySuppressed.Contains(TrackerSyncPlanner.Key(target.SourceName))));
            desiredUnresolved.AddRange(existingUnresolved.Where(item =>
                item.Dependency.DependentEntityId == ownerId &&
                manuallySuppressed.Contains(TrackerSyncPlanner.Key(
                    item.Dependency.DependencySourceName))));
        }
        HashSet<EntityId> reconciledIds = reconciled.ToHashSet();
        var finalResolved = existingResolved.Where(item =>
                !reconciledIds.Contains(item.Edge.DependentEntityId))
            .Concat(desiredResolved).ToArray();
        var finalUnresolved = existingUnresolved.Where(item =>
                !reconciledIds.Contains(item.Dependency.DependentEntityId))
            .Concat(desiredUnresolved).ToArray();
        var finalOverrides = existingOverrides.Where(item =>
            !reconciledIds.Contains(item.DependentEntityId))
            .Concat(desiredOverrides).ToArray();
        TrackedEntity[] candidate = existingByKey.Values.ToArray();
        foreach (TrackedEntity entity in candidate)
        {
            string key = TrackerSyncPlanner.Key(entity.SourceName);
            if (selectedByKey.TryGetValue(key, out TrackerSyncEntity? wanted))
                entity.ChangeLifecycleState(wanted.Active ? EntityLifecycleState.Active : EntityLifecycleState.Archived);
        }
        EffectiveDependencyState effective = resolver.Resolve(candidate,
            finalResolved, finalUnresolved, finalOverrides);
        var ranking = ranker.Rank(candidate.Where(static item => item.LifecycleState == EntityLifecycleState.Active),
            effective.ResolvedDependencies.Select(static item => item.Edge),
            effective.UnresolvedDependencies.Select(static item => item.Dependency));
        if (!ranking.IsSuccess)
            throw new InvalidOperationException(string.Join(Environment.NewLine,
                ranking.Diagnostics.Select(static item => item.Message)));
        var snapshot = snapshotCalculator.Calculate(candidate, effective);
        TrackedStateChangeSet changes = new(additions, [], archives,
            reconciled, desiredResolved, desiredUnresolved,
            reconciled, desiredOverrides, entityIdsToRestore: restores,
            progressSnapshotAfterChanges: snapshot,
            entitiesWithRequestedPriorityToUpdate: priorityUpdates,
            entitiesWithGroupNameToUpdate: groupUpdates);
        TrackerSyncCommit commit = new(review.SourceTrackerId,
            review.DestinationTrackerId, review.SourceFingerprint,
            review.DestinationFingerprint, changes,
            new TrackerSyncBaseline(review.Source, selected));
        return (commit, new TrackerSyncPreview(currentProgress, snapshot,
            effective.UnresolvedDependencies.Count));
    }

    private async Task<Tracker> RequireActiveAsync(TrackerId id, CancellationToken cancellationToken)
    {
        Tracker tracker = await trackerRepository.GetAsync(id, cancellationToken) ??
            throw new InvalidOperationException("The source or destination tracker no longer exists.");
        if (tracker.LifecycleState != CatalogLifecycleState.Active)
            throw new InvalidOperationException("The source or destination tracker is recycled.");
        Project project = await projectRepository.GetAsync(tracker.ProjectId, cancellationToken) ??
            throw new InvalidOperationException("The source or destination project no longer exists.");
        if (project.LifecycleState != CatalogLifecycleState.Active)
            throw new InvalidOperationException("The source or destination project is recycled.");
        return tracker;
    }

    private async Task<(TrackerSyncStructure, TrackedEntity[], PersistedDependency[],
        PersistedUnresolvedDependency[], ManualDependencyOverride[])> ReadStructureAsync(
        TrackerId id, CancellationToken cancellationToken)
    {
        TrackedEntity[] entities = (await entityRepository.GetAllAsync(id, cancellationToken)).ToArray();
        PersistedDependency[] resolved = (await dependencyRepository.GetAllAsync(id, cancellationToken)).ToArray();
        PersistedUnresolvedDependency[] unresolved =
            (await dependencyRepository.GetAllUnresolvedAsync(id, cancellationToken)).ToArray();
        ManualDependencyOverride[] overrides = (await overrideRepository.GetAllAsync(id, cancellationToken)).ToArray();
        return (TrackerSyncPlanner.Capture(entities, resolved, unresolved, overrides),
            entities, resolved, unresolved, overrides);
    }

    private static bool SameDependencies(
        IReadOnlyList<TrackerSyncDependency> a, IReadOnlyList<TrackerSyncDependency> b) =>
        a.Count == b.Count && a.All(item => b.Any(other =>
            TrackerSyncPlanner.Key(other.Name) == TrackerSyncPlanner.Key(item.Name) &&
            other.Kind == item.Kind));
}

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
        (TrackerSyncStructure source, _, _) = await ReadStructureAsync(sourceId, cancellationToken);
        (TrackerSyncStructure target, _, _) = await ReadStructureAsync(destinationId, cancellationToken);
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
        (_, TrackedEntity[] existing, EntityDependency[] existingDependencies) =
            await ReadStructureAsync(review.DestinationTrackerId, cancellationToken);
        ProgressSnapshotState currentProgress = snapshotCalculator.Calculate(existing,
            resolver.Resolve(existing, existingDependencies));
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
        List<TrackedEntity> filterActiveUpdates = [];
        foreach (TrackerSyncEntity wanted in selected.Entities)
        {
            string key = TrackerSyncPlanner.Key(wanted.Name);
            existingByKey.TryGetValue(key, out TrackedEntity? entity);
            if (entity is null or { IsPlaceholder: true })
            {
                if (!wanted.Active) continue;
                // A placeholder with the name becomes the entity, keeping its ID.
                entity = new TrackedEntity(entity?.Id ?? EntityId.New(), review.DestinationTrackerId,
                    wanted.Name, requestedPriority: wanted.RequestedPriority, groupName: wanted.GroupName,
                    filterActive: wanted.FilterActive ?? "");
                existingByKey[key] = entity;
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
            if (wanted.FilterActive is { } filterActive &&
                !string.Equals(entity.FilterActive, filterActive, StringComparison.Ordinal))
            {
                entity.ChangeFilterActive(filterActive);
                filterActiveUpdates.Add(entity);
            }
        }

        // An entity whose dependencies the sync changes gets exactly the selected list.
        List<EntityId> reconciled = [];
        List<EntityDependency> desiredDependencies = [];
        DependencyTargets targets = new(review.DestinationTrackerId, existingByKey.Values);
        foreach (TrackerSyncEntity wanted in selected.Entities.Where(static item => item.Active))
        {
            string key = TrackerSyncPlanner.Key(wanted.Name);
            currentByKey.TryGetValue(key, out TrackerSyncEntity? current);
            if (current is not null && SameDependencies(current.Dependencies, wanted.Dependencies))
                continue;
            EntityId ownerId = existingByKey[key].Id;
            reconciled.Add(ownerId);
            desiredDependencies.AddRange(wanted.Dependencies.Select(dependency =>
                new EntityDependency(ownerId, targets.For(dependency.Name).Id)));
        }
        additions.AddRange(targets.NewPlaceholders);
        HashSet<EntityId> reconciledIds = reconciled.ToHashSet();
        EntityDependency[] finalDependencies = existingDependencies
            .Where(item => !reconciledIds.Contains(item.DependentEntityId))
            .Concat(desiredDependencies).ToArray();
        TrackedEntity[] candidate = [.. existingByKey.Values, .. targets.NewPlaceholders];
        foreach (TrackedEntity entity in candidate)
        {
            if (entity.IsPlaceholder) continue;
            string key = TrackerSyncPlanner.Key(entity.SourceName);
            if (selectedByKey.TryGetValue(key, out TrackerSyncEntity? wanted))
                entity.ChangeLifecycleState(wanted.Active ? EntityLifecycleState.Active : EntityLifecycleState.Archived);
        }
        EffectiveDependencyState effective = resolver.Resolve(candidate, finalDependencies);
        var ranking = ranker.Rank(candidate.Where(static item => item.LifecycleState == EntityLifecycleState.Active),
            effective.ResolvedDependencies.Select(static item => item.Edge),
            effective.UnresolvedDependencies.Select(static item => item.Dependency));
        if (!ranking.IsSuccess)
            throw new InvalidOperationException(string.Join(Environment.NewLine,
                ranking.Diagnostics.Select(static item => item.Message)));
        var snapshot = snapshotCalculator.Calculate(candidate, effective);
        TrackedStateChangeSet changes = new(additions, [], archives,
            reconciled, desiredDependencies, entitiesWithProgressToUpdate: filterActiveUpdates,
            entityIdsToRestore: restores,
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

    private async Task<(TrackerSyncStructure, TrackedEntity[], EntityDependency[])> ReadStructureAsync(
        TrackerId id, CancellationToken cancellationToken)
    {
        TrackedEntity[] entities = (await entityRepository.GetAllAsync(id, cancellationToken)).ToArray();
        EntityDependency[] dependencies = (await dependencyRepository.GetAllAsync(id, cancellationToken)).ToArray();
        return (TrackerSyncPlanner.Capture(entities, dependencies), entities, dependencies);
    }

    private static bool SameDependencies(
        IReadOnlyList<TrackerSyncDependency> a, IReadOnlyList<TrackerSyncDependency> b) =>
        a.Count == b.Count && a.All(item => b.Any(other =>
            TrackerSyncPlanner.Key(other.Name) == TrackerSyncPlanner.Key(item.Name)));
}

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
        var existingById = existing.ToDictionary(static entity => entity.Id);
        var currentById = review.Destination.Entities
            .Where(static entity => entity.Id is not null)
            .ToDictionary(static entity => entity.Id!.Value);
        var currentByKey = review.Destination.Entities.ToDictionary(
            static entity => TrackerSyncPlanner.Key(entity.Name));
        var selectedByKey = selected.Entities.ToDictionary(
            static entity => TrackerSyncPlanner.Key(entity.Name));
        List<TrackedEntity> additions = [];
        List<TrackedEntity> renames = [];
        List<EntityId> archives = [];
        List<EntityId> restores = [];
        List<TrackedEntity> priorityUpdates = [];
        List<TrackedEntity> groupUpdates = [];
        List<TrackedEntity> filterActiveUpdates = [];
        List<TrackedEntity> dueDateUpdates = [];
        Dictionary<TrackerSyncEntity, TrackedEntity> results = new(ReferenceEqualityComparer.Instance);

        // Renames come first, so a new entity can take a name a renamed one gave up. An entity renamed
        // onto a placeholder's name takes the placeholder's place: what depended on the placeholder
        // depends on the entity.
        Dictionary<EntityId, EntityId> replacedPlaceholders = [];
        foreach (TrackerSyncEntity wanted in selected.Entities)
        {
            if (Existing(wanted) is not { IsPlaceholder: false } entity ||
                string.Equals(entity.SourceName, wanted.Name, StringComparison.Ordinal))
                continue;
            string oldKey = TrackerSyncPlanner.Key(entity.SourceName);
            if (existingByKey.GetValueOrDefault(oldKey) == entity) existingByKey.Remove(oldKey);
            string key = TrackerSyncPlanner.Key(wanted.Name);
            if (existingByKey.TryGetValue(key, out TrackedEntity? holder) && holder != entity)
            {
                if (!holder.IsPlaceholder)
                    throw new InvalidOperationException(
                        $"Another entity in this tracker is already called {holder.SourceName}.");
                replacedPlaceholders[holder.Id] = entity.Id;
            }
            entity.ChangeSourceName(wanted.Name);
            existingByKey[key] = entity;
            // A copy, because the entity's lifecycle changes below: archiving and restoring are their own
            // steps of the change.
            renames.Add(new TrackedEntity(entity.Id, entity.TrackerId, entity.SourceName, entity.Status,
                entity.Notes, entity.LifecycleState, entity.RequestedPriority, entity.ResponsibleDeveloper,
                entity.GroupName, entity.FilterActive, entity.SharedNotes, entity.DueDate));
        }

        foreach (TrackerSyncEntity wanted in selected.Entities)
        {
            string key = TrackerSyncPlanner.Key(wanted.Name);
            TrackedEntity? entity = Existing(wanted);
            if (entity is null or { IsPlaceholder: true })
            {
                if (!wanted.Active) continue;
                // A placeholder with the name becomes the entity, keeping its ID.
                entity = new TrackedEntity(entity?.Id ?? EntityId.New(), review.DestinationTrackerId,
                    wanted.Name, requestedPriority: wanted.RequestedPriority, groupName: wanted.GroupName,
                    filterActive: wanted.FilterActive ?? "", dueDate: wanted.DueDate);
                existingByKey[key] = entity;
                additions.Add(entity);
                results[wanted] = entity;
                continue;
            }
            results[wanted] = entity;
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
            if (entity.DueDate != wanted.DueDate)
            {
                entity.ChangeDueDate(wanted.DueDate);
                dueDateUpdates.Add(entity);
            }
        }

        // An entity whose dependencies the sync changes gets exactly the selected list.
        List<EntityId> reconciled = [];
        List<EntityDependency> desiredDependencies = [];
        DependencyTargets targets = new(review.DestinationTrackerId, existingByKey.Values);
        foreach (TrackerSyncEntity wanted in selected.Entities.Where(static item => item.Active))
        {
            TrackerSyncEntity? current = wanted.Id is { } id
                ? currentById.GetValueOrDefault(id)
                : currentByKey.GetValueOrDefault(TrackerSyncPlanner.Key(wanted.Name));
            if (current is not null && SameDependencies(current.Dependencies, wanted.Dependencies))
                continue;
            EntityId ownerId = results[wanted].Id;
            reconciled.Add(ownerId);
            desiredDependencies.AddRange(wanted.Dependencies.Select(dependency =>
                new EntityDependency(ownerId, targets.For(dependency.Name).Id)));
        }
        HashSet<EntityId> reconciledIds = reconciled.ToHashSet();
        // What depended on a placeholder an entity was renamed onto depends on the entity now.
        foreach (IGrouping<EntityId, EntityDependency> owner in existingDependencies
                     .Where(item => replacedPlaceholders.ContainsKey(item.DependencyEntityId) &&
                                    !reconciledIds.Contains(item.DependentEntityId))
                     .GroupBy(static item => item.DependentEntityId))
        {
            reconciled.Add(owner.Key);
            reconciledIds.Add(owner.Key);
            desiredDependencies.AddRange(existingDependencies
                .Where(item => item.DependentEntityId == owner.Key)
                .Select(item => new EntityDependency(owner.Key,
                    replacedPlaceholders.GetValueOrDefault(item.DependencyEntityId, item.DependencyEntityId)))
                .Distinct());
        }
        additions.AddRange(targets.NewPlaceholders);
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
        TrackedStateChangeSet changes = new(additions, renames, archives,
            reconciled, desiredDependencies, entitiesWithProgressToUpdate: filterActiveUpdates,
            entityIdsToRestore: restores,
            progressSnapshotAfterChanges: snapshot,
            entitiesWithRequestedPriorityToUpdate: priorityUpdates,
            entitiesWithGroupNameToUpdate: groupUpdates,
            entitiesWithDueDateToUpdate: dueDateUpdates);
        TrackerSyncCommit commit = new(review.SourceTrackerId,
            review.DestinationTrackerId, review.SourceFingerprint,
            review.DestinationFingerprint, changes,
            // The baseline keeps the copy's entity IDs, new entities' included, to recognise renames.
            new TrackerSyncBaseline(review.Source, new TrackerSyncStructure(selected.Entities
                .Select(wanted => results.TryGetValue(wanted, out TrackedEntity? entity)
                    ? wanted with { Id = entity.Id.Value }
                    : wanted)
                .ToArray())));
        return (commit, new TrackerSyncPreview(currentProgress, snapshot,
            effective.UnresolvedDependencies.Count));

        // The copy's entity a selected one is: by ID, or by name for one new to the copy.
        TrackedEntity? Existing(TrackerSyncEntity wanted) =>
            wanted.Id is { } id && existingById.TryGetValue(new EntityId(id), out TrackedEntity? byId)
                ? byId
                : existingByKey.GetValueOrDefault(TrackerSyncPlanner.Key(wanted.Name));
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

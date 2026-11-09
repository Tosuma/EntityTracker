using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Tracking;
using EntityTracker.Domain;

namespace EntityTracker.Application.Workflow;

/// <summary>
/// Applies one status, priority, due date and responsibility change to a validated collection of active
/// entities, in one save.
/// </summary>
public sealed class BulkEntityUpdateService
{
    private readonly IEntityRepository _entityRepository;
    private readonly IDependencyRepository _dependencyRepository;
    private readonly EffectiveDependencyResolver _effectiveDependencyResolver;
    private readonly ITrackedStateStore _store;
    private readonly IResponsibilityPeriodRepository? _responsibilityPeriods;
    private readonly ProgressSnapshotCalculator _snapshotCalculator;

    public BulkEntityUpdateService(
        IEntityRepository entityRepository,
        IDependencyRepository dependencyRepository,
        EffectiveDependencyResolver effectiveDependencyResolver,
        ITrackedStateStore store,
        IResponsibilityPeriodRepository? responsibilityPeriods = null,
        ProgressSnapshotCalculator? snapshotCalculator = null)
    {
        ArgumentNullException.ThrowIfNull(entityRepository);
        ArgumentNullException.ThrowIfNull(dependencyRepository);
        ArgumentNullException.ThrowIfNull(effectiveDependencyResolver);
        ArgumentNullException.ThrowIfNull(store);

        _entityRepository = entityRepository;
        _dependencyRepository = dependencyRepository;
        _effectiveDependencyResolver = effectiveDependencyResolver;
        _store = store;
        _responsibilityPeriods = responsibilityPeriods;
        _snapshotCalculator = snapshotCalculator ?? new ProgressSnapshotCalculator();
    }

    public async Task<BulkEntityUpdateResult> ApplyAsync(
        TrackerId trackerId,
        IReadOnlyCollection<EntityId> entityIds,
        BulkEntityUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(entityIds);
        ArgumentNullException.ThrowIfNull(update);
        if (entityIds.Count == 0)
        {
            throw new ArgumentException(
                "At least one entity must be selected.",
                nameof(entityIds));
        }

        if (!update.HasChanges)
        {
            throw new ArgumentException("The update does not change anything.", nameof(update));
        }

        if (update.Status is { } status && !Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(update),
                status,
                "The development status is not defined.");
        }

        if (update.Priority?.Value is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(update),
                update.Priority.Value,
                "The requested priority must be between 1 and 5.");
        }

        if (update.Responsibility is not null && _responsibilityPeriods is null)
        {
            throw new InvalidOperationException("Responsibility cannot be changed in this context.");
        }

        if (entityIds.Any(static id => id is null))
        {
            throw new ArgumentException(
                "The selected entity IDs cannot contain null values.",
                nameof(entityIds));
        }

        EntityId[] selectedIds = entityIds.Distinct().ToArray();
        Task<IReadOnlyList<TrackedEntity>> entitiesTask =
            _entityRepository.GetAllAsync(trackerId, cancellationToken);
        Task<IReadOnlyList<EntityDependency>> dependenciesTask =
            _dependencyRepository.GetAllAsync(trackerId, cancellationToken);
        await Task.WhenAll(entitiesTask, dependenciesTask);

        TrackedEntity[] entities = (await entitiesTask).ToArray();
        TrackerStateValidator.EnsureOwned(
            trackerId,
            entities,
            await dependenciesTask);
        Dictionary<EntityId, TrackedEntity> entitiesById = entities.ToDictionary(
            static entity => entity.Id);
        foreach (EntityId selectedId in selectedIds)
        {
            if (!entitiesById.TryGetValue(selectedId, out TrackedEntity? entity))
            {
                throw new InvalidOperationException(
                    "A selected entity no longer exists. Refresh the overview and try again.");
            }

            if (entity.LifecycleState != EntityLifecycleState.Active)
            {
                throw new InvalidOperationException(
                    $"'{entity.SourceName}' is no longer active. Refresh the overview and try again.");
            }
        }

        // The developer is responsible for these entities now; Add skips them and Remove only touches them.
        HashSet<EntityId> currentlyResponsible = update.Responsibility is { } responsibility
            ? (await _responsibilityPeriods!.GetByTrackerAsync(trackerId, cancellationToken))
                .Where(period => period.IsCurrent && period.DeveloperId == responsibility.DeveloperId)
                .Select(static period => period.EntityId)
                .ToHashSet()
            : [];

        List<TrackedEntity> progressUpdates = [];
        List<TrackedEntity> priorityUpdates = [];
        List<TrackedEntity> dueDateUpdates = [];
        List<ResponsibilityAddition> additions = [];
        List<ResponsibilityRemoval> removals = [];
        Dictionary<EntityId, TrackedEntity> updatesById = [];
        List<BulkEntityUndoItem> undoItems = [];
        foreach (EntityId selectedId in selectedIds)
        {
            TrackedEntity entity = entitiesById[selectedId];
            DevelopmentStatus targetStatus = update.Status ?? entity.Status;
            int? targetPriority = update.Priority is null ? entity.RequestedPriority : update.Priority.Value;
            DateOnly? targetDueDate = update.DueDate is null ? entity.DueDate : update.DueDate.DueDate;
            bool statusChanged = targetStatus != entity.Status;
            bool priorityChanged = targetPriority != entity.RequestedPriority;
            bool dueDateChanged = targetDueDate != entity.DueDate;
            bool responsibilityChanged = false;
            if (update.Responsibility is { } change)
            {
                bool isResponsible = currentlyResponsible.Contains(entity.Id);
                if (change.Add && !isResponsible)
                {
                    additions.Add(new ResponsibilityAddition(entity.Id, change.DeveloperId));
                    responsibilityChanged = true;
                }
                else if (!change.Add && isResponsible)
                {
                    removals.Add(new ResponsibilityRemoval(entity.Id, change.DeveloperId));
                    responsibilityChanged = true;
                }
            }

            if (statusChanged || priorityChanged || dueDateChanged)
            {
                TrackedEntity updated = WithValues(entity, trackerId, targetStatus, targetPriority, targetDueDate);
                updatesById[entity.Id] = updated;
                if (statusChanged) progressUpdates.Add(updated);
                if (priorityChanged) priorityUpdates.Add(updated);
                if (dueDateChanged) dueDateUpdates.Add(updated);
            }

            if (statusChanged || priorityChanged || dueDateChanged || responsibilityChanged)
            {
                undoItems.Add(new BulkEntityUndoItem(
                    entity.Id,
                    entity.SourceName,
                    statusChanged ? new FieldUndo<DevelopmentStatus>(entity.Status, targetStatus) : null,
                    priorityChanged ? new FieldUndo<int?>(entity.RequestedPriority, targetPriority) : null,
                    dueDateChanged ? new FieldUndo<DateOnly?>(entity.DueDate, targetDueDate) : null,
                    responsibilityChanged ? update.Responsibility : null));
            }
        }

        int changedCount = undoItems.Count;
        int unchangedCount = selectedIds.Length - changedCount;
        if (changedCount == 0)
        {
            return new BulkEntityUpdateResult(0, unchangedCount);
        }

        // Only a status change moves progress, so only then is a new progress snapshot recorded.
        ProgressSnapshotState? snapshot = null;
        if (progressUpdates.Count > 0)
        {
            TrackedEntity[] candidateEntities = entities
                .Select(entity => updatesById.GetValueOrDefault(entity.Id) ?? entity)
                .ToArray();
            EffectiveDependencyState effectiveState = _effectiveDependencyResolver.Resolve(
                candidateEntities,
                await dependenciesTask);
            snapshot = _snapshotCalculator.Calculate(candidateEntities, effectiveState);
        }

        await _store.ApplyAsync(
            trackerId,
            new TrackedStateChangeSet(
                [],
                [],
                [],
                entitiesWithProgressToUpdate: progressUpdates,
                progressSnapshotAfterChanges: snapshot,
                entitiesWithRequestedPriorityToUpdate: priorityUpdates,
                responsibilityAdditions: additions,
                responsibilityRemovals: removals,
                entitiesWithDueDateToUpdate: dueDateUpdates),
            cancellationToken);

        return new BulkEntityUpdateResult(changedCount, unchangedCount, new BulkEntityUndo(undoItems));
    }

    /// <summary>
    /// Puts back what a bulk update changed, in one save, as a new change: statuses append to the history.
    /// An entity changed since then, in a field the update set, is left alone and named in the result.
    /// A developer the update added is taken off again and one it removed gets their ended period back, so
    /// the responsibility timeline shows no trace of the mistake.
    /// </summary>
    public async Task<BulkEntityUndoResult> UndoAsync(
        TrackerId trackerId,
        BulkEntityUndo undo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(undo);

        Task<IReadOnlyList<TrackedEntity>> entitiesTask =
            _entityRepository.GetAllAsync(trackerId, cancellationToken);
        Task<IReadOnlyList<EntityDependency>> dependenciesTask =
            _dependencyRepository.GetAllAsync(trackerId, cancellationToken);
        await Task.WhenAll(entitiesTask, dependenciesTask);
        TrackedEntity[] entities = (await entitiesTask).ToArray();
        IReadOnlyList<EntityDependency> dependencies = await dependenciesTask;
        TrackerStateValidator.EnsureOwned(trackerId, entities, dependencies);
        Dictionary<EntityId, TrackedEntity> entitiesById = entities.ToDictionary(static entity => entity.Id);
        IReadOnlyList<ResponsibilityPeriod> periods =
            _responsibilityPeriods is not null && undo.Items.Any(static item => item.Responsibility is not null)
                ? await _responsibilityPeriods.GetByTrackerAsync(trackerId, cancellationToken)
                : [];

        List<TrackedEntity> progressUpdates = [];
        List<TrackedEntity> priorityUpdates = [];
        List<TrackedEntity> dueDateUpdates = [];
        List<Guid> periodsToReopen = [];
        List<Guid> periodsToDelete = [];
        Dictionary<EntityId, TrackedEntity> updatesById = [];
        List<string> notRestored = [];
        int restoredCount = 0;
        foreach (BulkEntityUndoItem item in undo.Items)
        {
            ResponsibilityPeriod? period = item.Responsibility is null ? null : PeriodToPutBack(item, periods);
            if (!entitiesById.TryGetValue(item.EntityId, out TrackedEntity? entity) ||
                entity.LifecycleState != EntityLifecycleState.Active ||
                ChangedSince(entity, item) ||
                (item.Responsibility is not null && period is null))
            {
                notRestored.Add(item.Name);
                continue;
            }

            if (item.Status is not null || item.Priority is not null || item.DueDate is not null)
            {
                TrackedEntity restored = WithValues(
                    entity,
                    trackerId,
                    item.Status is { } status ? status.Before : entity.Status,
                    item.Priority is { } priority ? priority.Before : entity.RequestedPriority,
                    item.DueDate is { } dueDate ? dueDate.Before : entity.DueDate);
                updatesById[entity.Id] = restored;
                if (item.Status is not null) progressUpdates.Add(restored);
                if (item.Priority is not null) priorityUpdates.Add(restored);
                if (item.DueDate is not null) dueDateUpdates.Add(restored);
            }

            if (period is not null)
                (item.Responsibility!.Add ? periodsToDelete : periodsToReopen).Add(period.Id);
            restoredCount++;
        }

        if (restoredCount == 0)
        {
            return new BulkEntityUndoResult(0, notRestored);
        }

        ProgressSnapshotState? snapshot = null;
        if (progressUpdates.Count > 0)
        {
            TrackedEntity[] candidateEntities = entities
                .Select(entity => updatesById.GetValueOrDefault(entity.Id) ?? entity)
                .ToArray();
            snapshot = _snapshotCalculator.Calculate(
                candidateEntities,
                _effectiveDependencyResolver.Resolve(candidateEntities, dependencies));
        }

        await _store.ApplyAsync(
            trackerId,
            new TrackedStateChangeSet(
                [],
                [],
                [],
                entitiesWithProgressToUpdate: progressUpdates,
                progressSnapshotAfterChanges: snapshot,
                entitiesWithRequestedPriorityToUpdate: priorityUpdates,
                entitiesWithDueDateToUpdate: dueDateUpdates,
                responsibilityPeriodsToReopen: periodsToReopen,
                responsibilityPeriodsToDelete: periodsToDelete),
            cancellationToken);
        return new BulkEntityUndoResult(restoredCount, notRestored);
    }

    /// <summary>Gets whether a field the update set no longer holds what the update put there.</summary>
    private static bool ChangedSince(TrackedEntity entity, BulkEntityUndoItem item) =>
        (item.Status is { } status && entity.Status != status.After) ||
        (item.Priority is { } priority && entity.RequestedPriority != priority.After) ||
        (item.DueDate is { } dueDate && entity.DueDate != dueDate.After);

    /// <summary>
    /// The period to undo: the current one of a developer the update added, or the latest ended one of a
    /// developer it removed. Null when the assignment has changed since.
    /// </summary>
    private static ResponsibilityPeriod? PeriodToPutBack(
        BulkEntityUndoItem item, IReadOnlyList<ResponsibilityPeriod> periods)
    {
        BulkResponsibilityChange change = item.Responsibility!;
        ResponsibilityPeriod[] own = periods
            .Where(period => period.EntityId == item.EntityId && period.DeveloperId == change.DeveloperId)
            .ToArray();
        ResponsibilityPeriod? current = own.FirstOrDefault(static period => period.IsCurrent);
        if (change.Add) return current;
        return current is not null ? null : own.MaxBy(static period => period.EndedAtUtc);
    }

    private static TrackedEntity WithValues(
        TrackedEntity entity, TrackerId trackerId, DevelopmentStatus status, int? priority, DateOnly? dueDate) =>
        new(
            entity.Id,
            trackerId,
            entity.SourceName,
            status,
            entity.Notes,
            entity.LifecycleState,
            priority,
            entity.ResponsibleDeveloper,
            entity.GroupName,
            entity.FilterActive,
            entity.SharedNotes,
            dueDate);
}

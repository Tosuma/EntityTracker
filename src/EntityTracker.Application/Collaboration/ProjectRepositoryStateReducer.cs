using EntityTracker.Application.Importing;
using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Synchronization;
using EntityTracker.Domain;

namespace EntityTracker.Application.Collaboration;

public sealed class ProjectRepositoryStateReducer
{
    public ProjectRepositoryState Apply(ProjectRepositoryState current, ProjectMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(mutation);
        if (current.Project.Id != mutation.ProjectId)
        {
            throw new InvalidOperationException("The mutation belongs to another Project.");
        }
        if (current.Operations.Any(item => item.Id == mutation.OperationId))
        {
            throw new InvalidOperationException("The operation ID already exists in the repository.");
        }

        return mutation switch
        {
            RenameProjectMutation value => RenameProject(current, value),
            SetProjectLifecycleMutation value => SetProjectLifecycle(current, value),
            PurgeProjectMutation value => PurgeProject(current, value),
            CreateTrackerMutation value => CreateTracker(current, value),
            RenameTrackerMutation value => RenameTracker(current, value),
            SetTrackerLifecycleMutation value => SetTrackerLifecycle(current, value),
            PurgeTrackerMutation value => PurgeTracker(current, value),
            ChangeTrackedStateMutation value => ChangeTrackedState(current, value),
            _ => throw new InvalidOperationException("The mutation is not valid for a linked Project.")
        };
    }

    private static ProjectRepositoryState RenameProject(ProjectRepositoryState current, RenameProjectMutation mutation)
    {
        Project project = new(current.Project.Id, mutation.Name, current.Project.CreatedAtUtc,
            mutation.OccurredAtUtc, current.Project.LifecycleState, current.Project.RecycledAtUtc);
        return Append(current with { Project = project }, Operation(mutation, [project.Id], [], []));
    }

    private static ProjectRepositoryState SetProjectLifecycle(ProjectRepositoryState current, SetProjectLifecycleMutation mutation)
    {
        Project project = new(current.Project.Id, current.Project.Name, current.Project.CreatedAtUtc,
            mutation.OccurredAtUtc, mutation.LifecycleState,
            mutation.LifecycleState == CatalogLifecycleState.Recycled ? mutation.OccurredAtUtc : null);
        return Append(current with { Project = project }, Operation(mutation, [project.Id], [], []));
    }

    private static ProjectRepositoryState PurgeProject(ProjectRepositoryState current, PurgeProjectMutation mutation)
    {
        TrackerId[] trackerIds = current.Trackers.Select(item => item.Tracker.Id).ToArray();
        EntityId[] entityIds = current.Trackers.SelectMany(item => item.Entities).Select(item => item.Entity.Id).ToArray();
        RepositoryOperation operation = Operation(mutation, [current.Project.Id], trackerIds, entityIds);
        RepositoryTombstone[] tombstones = current.Tombstones
            .Concat([new RepositoryTombstone(RepositoryTombstoneKind.Project, current.Project.Id.Value, current.Project.Id, mutation.OperationId, mutation.OccurredAtUtc)])
            .Concat(trackerIds.Select(id => new RepositoryTombstone(RepositoryTombstoneKind.Tracker, id.Value, current.Project.Id, mutation.OperationId, mutation.OccurredAtUtc)))
            .Concat(entityIds.Select(id => new RepositoryTombstone(RepositoryTombstoneKind.Entity, id.Value, current.Project.Id, mutation.OperationId, mutation.OccurredAtUtc)))
            .ToArray();
        return Append(current with { Trackers = [], Tombstones = tombstones }, operation);
    }

    private static ProjectRepositoryState CreateTracker(ProjectRepositoryState current, CreateTrackerMutation mutation)
    {
        if (current.Project.LifecycleState != CatalogLifecycleState.Active)
        {
            throw new InvalidOperationException("A tracker cannot be created in a recycled Project.");
        }
        if (current.Trackers.Any(item => string.Equals(item.Tracker.Name.Trim(), mutation.Creation.Tracker.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("The tracker name is already reserved.");
        }

        TrackedStateChangeSet changes = mutation.Creation.ChangeSet;
        Dictionary<EntityId, TrackedEntity> entities = changes.EntitiesToAdd.ToDictionary(item => item.Id);
        EntityRepositoryState[] repositoryEntities = changes.EntitiesToAdd.Select(entity =>
            new EntityRepositoryState(
                entity,
                new EntityAuditTimestamps(entity.Id, mutation.OccurredAtUtc, mutation.OccurredAtUtc, mutation.OccurredAtUtc),
                BuildImportedDeclarations(entity.Id, changes, entities),
                changes.ManualDependencyOverrides.Where(item => item.DependentEntityId == entity.Id).ToArray())).ToArray();
        TrackerRepositoryState trackerState = new(mutation.Creation.Tracker, repositoryEntities);
        EntityStatusHistoryEntry[] transitions = repositoryEntities.Select(item =>
            new EntityStatusHistoryEntry(mutation.OperationId, item.Entity.Id, null, item.Entity.Status,
                mutation.OccurredAtUtc, StatusHistoryEntryKind.Baseline)).ToArray();
        RepositoryImportSummary? import = mutation.Creation.ImportCompletion is null
            ? null
            : ImportSummary(mutation.Creation.Tracker.Id, mutation.Creation.ImportCompletion);
        RepositoryProgressSnapshot snapshot = new(
            mutation.Creation.Tracker.Id,
            mutation.OccurredAtUtc,
            mutation.Creation.InitialSnapshot);
        RepositoryOperation operation = Operation(mutation, [], [mutation.Creation.Tracker.Id],
            repositoryEntities.Select(item => item.Entity.Id), transitions, import, [snapshot]);
        return Append(current with { Trackers = current.Trackers.Append(trackerState).ToArray() }, operation);
    }

    private static ProjectRepositoryState RenameTracker(ProjectRepositoryState current, RenameTrackerMutation mutation)
    {
        TrackerRepositoryState tracker = RequireTracker(current, mutation.TrackerId);
        if (current.Trackers.Any(item => item.Tracker.Id != mutation.TrackerId &&
            string.Equals(item.Tracker.Name.Trim(), mutation.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("The tracker name is already reserved.");
        }
        Tracker updated = new(tracker.Tracker.Id, current.Project.Id, mutation.Name,
            tracker.Tracker.CreatedAtUtc, mutation.OccurredAtUtc, tracker.Tracker.LifecycleState,
            tracker.Tracker.RecycledAtUtc, tracker.Tracker.CopiedFromTrackerId);
        return Append(ReplaceTracker(current, tracker with { Tracker = updated }),
            Operation(mutation, [], [mutation.TrackerId], []));
    }

    private static ProjectRepositoryState SetTrackerLifecycle(ProjectRepositoryState current, SetTrackerLifecycleMutation mutation)
    {
        TrackerRepositoryState tracker = RequireTracker(current, mutation.TrackerId);
        Tracker updated = new(tracker.Tracker.Id, current.Project.Id, tracker.Tracker.Name,
            tracker.Tracker.CreatedAtUtc, mutation.OccurredAtUtc, mutation.LifecycleState,
            mutation.LifecycleState == CatalogLifecycleState.Recycled ? mutation.OccurredAtUtc : null,
            tracker.Tracker.CopiedFromTrackerId);
        return Append(ReplaceTracker(current, tracker with { Tracker = updated }),
            Operation(mutation, [], [mutation.TrackerId], []));
    }

    private static ProjectRepositoryState PurgeTracker(ProjectRepositoryState current, PurgeTrackerMutation mutation)
    {
        TrackerRepositoryState tracker = RequireTracker(current, mutation.TrackerId);
        EntityId[] entityIds = tracker.Entities.Select(item => item.Entity.Id).ToArray();
        RepositoryOperation operation = Operation(mutation, [], [mutation.TrackerId], entityIds);
        RepositoryTombstone[] tombstones = current.Tombstones
            .Append(new RepositoryTombstone(RepositoryTombstoneKind.Tracker, mutation.TrackerId.Value,
                current.Project.Id, mutation.OperationId, mutation.OccurredAtUtc))
            .Concat(entityIds.Select(id => new RepositoryTombstone(RepositoryTombstoneKind.Entity,
                id.Value, current.Project.Id, mutation.OperationId, mutation.OccurredAtUtc)))
            .ToArray();
        ProjectRepositoryState reduced = current with
        {
            Trackers = current.Trackers.Where(item => item.Tracker.Id != mutation.TrackerId).ToArray(),
            Tombstones = tombstones
        };
        return Append(reduced, operation);
    }

    private static ProjectRepositoryState ChangeTrackedState(ProjectRepositoryState current, ChangeTrackedStateMutation mutation)
    {
        TrackerRepositoryState tracker = RequireTracker(current, mutation.TrackerId);
        TrackedStateChangeSet changes = mutation.ChangeSet;
        Dictionary<EntityId, EntityRepositoryState> byId = tracker.Entities.ToDictionary(item => item.Entity.Id);
        Dictionary<EntityId, TrackedEntity> original = tracker.Entities.ToDictionary(item => item.Entity.Id, item => item.Entity);

        foreach (TrackedEntity entity in changes.EntitiesToAdd)
        {
            byId.Add(entity.Id, new EntityRepositoryState(entity,
                new EntityAuditTimestamps(entity.Id, mutation.OccurredAtUtc, mutation.OccurredAtUtc, mutation.OccurredAtUtc), [], []));
        }
        foreach (TrackedEntity entity in changes.EntitiesToUpdate)
            UpdateEntity(byId, entity, mutation.OccurredAtUtc, schemaChanged: true, progressChanged: false);
        foreach (TrackedEntity entity in changes.EntitiesWithProgressToUpdate)
            UpdateEntity(byId, entity, mutation.OccurredAtUtc, schemaChanged: false, progressChanged: true);
        foreach (TrackedEntity entity in changes.EntitiesWithRequestedPriorityToUpdate
                     .Concat(changes.EntitiesWithResponsibleDeveloperToUpdate)
                     .Concat(changes.EntitiesWithGroupNameToUpdate))
            UpdateEntity(byId, entity, mutation.OccurredAtUtc, schemaChanged: false, progressChanged: false);

        foreach (EntityId id in changes.EntityIdsToArchive)
            SetEntityLifecycle(byId, id, EntityLifecycleState.Archived, mutation.OccurredAtUtc);
        foreach (EntityId id in changes.EntityIdsToRestore)
            SetEntityLifecycle(byId, id, EntityLifecycleState.Active, mutation.OccurredAtUtc);

        Dictionary<EntityId, TrackedEntity> entityValues = byId.ToDictionary(item => item.Key, item => item.Value.Entity);
        foreach (EntityId ownerId in changes.ReconciledOwnerIds)
        {
            EntityRepositoryState item = byId[ownerId];
            byId[ownerId] = item with { ImportedDependencies = BuildImportedDeclarations(ownerId, changes, entityValues) };
        }
        foreach (EntityId ownerId in changes.ReconciledOverrideOwnerIds)
        {
            EntityRepositoryState item = byId[ownerId];
            byId[ownerId] = item with
            {
                ManualOverrides = changes.ManualDependencyOverrides.Where(value => value.DependentEntityId == ownerId).ToArray()
            };
        }

        EntityStatusHistoryEntry[] transitions = changes.EntitiesToAdd.Select(entity =>
                new EntityStatusHistoryEntry(mutation.OperationId, entity.Id, null, entity.Status,
                    mutation.OccurredAtUtc, StatusHistoryEntryKind.Created))
            .Concat(changes.EntitiesWithProgressToUpdate
                .Where(entity => original.TryGetValue(entity.Id, out TrackedEntity? previous) && previous.Status != entity.Status)
                .Select(entity => new EntityStatusHistoryEntry(mutation.OperationId, entity.Id,
                    original[entity.Id].Status, entity.Status, mutation.OccurredAtUtc,
                    StatusHistoryEntryKind.Transition)))
            .ToArray();

        List<RepositoryProgressSnapshot> snapshots = [];
        if (changes.ProgressSnapshotAfterChanges is { } progress &&
            !SameProgress(LastProgress(current, mutation.TrackerId), progress))
        {
            snapshots.Add(new RepositoryProgressSnapshot(mutation.TrackerId, mutation.OccurredAtUtc, progress));
        }
        RepositoryImportSummary? import = mutation.ImportCompletion is null
            ? null
            : ImportSummary(mutation.TrackerId, mutation.ImportCompletion);
        EntityId[] affected = changes.EntitiesToAdd.Select(item => item.Id)
            .Concat(changes.EntitiesToUpdate.Select(item => item.Id))
            .Concat(changes.EntitiesWithProgressToUpdate.Select(item => item.Id))
            .Concat(changes.EntitiesWithRequestedPriorityToUpdate.Select(item => item.Id))
            .Concat(changes.EntitiesWithResponsibleDeveloperToUpdate.Select(item => item.Id))
            .Concat(changes.EntitiesWithGroupNameToUpdate.Select(item => item.Id))
            .Concat(changes.EntityIdsToArchive)
            .Concat(changes.EntityIdsToRestore)
            .Concat(changes.ReconciledOwnerIds)
            .Concat(changes.ReconciledOverrideOwnerIds)
            .Distinct().ToArray();
        TrackerRepositoryState updatedTracker = tracker with { Entities = byId.Values.ToArray() };
        ProjectRepositoryState updated = ReplaceTracker(current, updatedTracker);
        return Append(updated, Operation(mutation, [], [mutation.TrackerId], affected,
            transitions, import, snapshots));
    }

    private static IReadOnlyList<ImportedDependencyDeclaration> BuildImportedDeclarations(
        EntityId ownerId,
        TrackedStateChangeSet changes,
        IReadOnlyDictionary<EntityId, TrackedEntity> entities) =>
        changes.ResolvedDependencies.Where(item => item.Edge.DependentEntityId == ownerId)
            .Select(item => new ImportedDependencyDeclaration(entities[item.Edge.DependencyEntityId].SourceName, item.Kind))
            .Concat(changes.UnresolvedDependencies.Where(item => item.Dependency.DependentEntityId == ownerId)
                .Select(item => new ImportedDependencyDeclaration(item.Dependency.DependencySourceName, item.Kind)))
            .ToArray();

    private static void UpdateEntity(Dictionary<EntityId, EntityRepositoryState> byId, TrackedEntity entity,
        DateTimeOffset timestamp, bool schemaChanged, bool progressChanged)
    {
        EntityRepositoryState current = byId[entity.Id];
        byId[entity.Id] = current with
        {
            Entity = entity,
            AuditTimestamps = current.AuditTimestamps with
            {
                SchemaUpdatedAtUtc = schemaChanged ? timestamp : current.AuditTimestamps.SchemaUpdatedAtUtc,
                ProgressUpdatedAtUtc = progressChanged ? timestamp : current.AuditTimestamps.ProgressUpdatedAtUtc
            }
        };
    }

    private static void SetEntityLifecycle(Dictionary<EntityId, EntityRepositoryState> byId,
        EntityId id, EntityLifecycleState lifecycle, DateTimeOffset timestamp)
    {
        EntityRepositoryState current = byId[id];
        TrackedEntity value = current.Entity;
        TrackedEntity updated = new(value.Id, value.TrackerId, value.SourceName, value.Status,
            value.Notes, lifecycle, value.Provenance, value.RequestedPriority,
            value.ResponsibleDeveloper, value.GroupName);
        UpdateEntity(byId, updated, timestamp, schemaChanged: true, progressChanged: false);
    }

    private static TrackerRepositoryState RequireTracker(ProjectRepositoryState current, TrackerId id) =>
        current.Trackers.SingleOrDefault(item => item.Tracker.Id == id)
        ?? throw new InvalidOperationException("The tracker no longer exists in the authoritative repository.");

    private static ProjectRepositoryState ReplaceTracker(ProjectRepositoryState current, TrackerRepositoryState tracker) =>
        current with { Trackers = current.Trackers.Select(item => item.Tracker.Id == tracker.Tracker.Id ? tracker : item).ToArray() };

    private static ProjectRepositoryState Append(ProjectRepositoryState state, RepositoryOperation operation) =>
        state with { Operations = state.Operations.Append(operation).ToArray() };

    private static RepositoryOperation Operation(ProjectMutation mutation,
        IEnumerable<ProjectId> projects, IEnumerable<TrackerId> trackers, IEnumerable<EntityId> entities,
        IReadOnlyList<EntityStatusHistoryEntry>? transitions = null,
        RepositoryImportSummary? import = null,
        IReadOnlyList<RepositoryProgressSnapshot>? snapshots = null) =>
        new(mutation.OperationId, mutation.Kind, mutation.OccurredAtUtc, projects.ToArray(), trackers.ToArray(),
            entities.ToArray(), transitions ?? [], import, snapshots ?? []);

    private static RepositoryImportSummary ImportSummary(TrackerId trackerId, SchemaImportCompletion value) =>
        new(trackerId, value.SourceFileName, value.Mode, value.NewEntityCount, value.ChangedEntityCount,
            value.ArchivedEntityCount, value.UnchangedEntityCount, value.UnresolvedEntityCount);

    private static ProgressSnapshotState? LastProgress(ProjectRepositoryState state, TrackerId trackerId) =>
        state.Operations.SelectMany(item => item.RecordedProgressSnapshots)
            .Where(item => item.TrackerId == trackerId)
            .OrderBy(item => item.RecordedAtUtc)
            .LastOrDefault()?.State;

    private static bool SameProgress(ProgressSnapshotState? left, ProgressSnapshotState right) =>
        left is not null && left.ReadyCount == right.ReadyCount && left.BlockedCount == right.BlockedCount &&
        left.InProgressCount == right.InProgressCount && left.ReworkNeededCount == right.ReworkNeededCount &&
        left.DevelopmentCompletedCount == right.DevelopmentCompletedCount && left.ReconciledCount == right.ReconciledCount;
}

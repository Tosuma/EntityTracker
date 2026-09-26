using EntityTracker.Application.Collaboration;
using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests.Collaboration;

public sealed class ProjectRepositoryStateReducerTests
{
    private static readonly DateTimeOffset Time =
        new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CatalogMutationsAppendOperationsAndCascadingTombstones()
    {
        ProjectRepositoryStateReducer reducer = new();
        Project project = new(ProjectId.New(), "Project", Time, Time);
        ProjectRepositoryState state = new(project, [], [], []);

        state = reducer.Apply(state, new RenameProjectMutation(
            project.Id, "Renamed", OperationId.New(), Time.AddMinutes(1)));
        state = reducer.Apply(state, new SetProjectLifecycleMutation(
            project.Id, CatalogLifecycleState.Recycled, OperationId.New(), Time.AddMinutes(2)));
        state = reducer.Apply(state, new SetProjectLifecycleMutation(
            project.Id, CatalogLifecycleState.Active, OperationId.New(), Time.AddMinutes(3)));

        TrackerId trackerId = TrackerId.New();
        EntityId entityId = EntityId.New();
        OperationId createId = OperationId.New();
        Tracker tracker = new(trackerId, project.Id, "Tracker", Time, Time);
        TrackedEntity entity = new(entityId, trackerId, "customer");
        TrackedStateChangeSet initial = new(
            [entity], [], [], [], [], [],
            progressSnapshotAfterChanges: new ProgressSnapshotState(1, 0, 0, 0, 0, 0),
            operationId: createId);
        state = reducer.Apply(state, new CreateTrackerMutation(
            project.Id,
            new TrackerCreationState(tracker, initial, new ProgressSnapshotState(1, 0, 0, 0, 0, 0)),
            createId,
            RepositoryOperationKind.TrackerCreated,
            Time.AddMinutes(4)));
        state = reducer.Apply(state, new RenameTrackerMutation(
            project.Id, trackerId, "Renamed tracker", OperationId.New(), Time.AddMinutes(5)));
        state = reducer.Apply(state, new PurgeTrackerMutation(
            project.Id, trackerId, OperationId.New(), Time.AddMinutes(6)));

        Assert.Equal("Renamed", state.Project.Name);
        Assert.Equal(CatalogLifecycleState.Active, state.Project.LifecycleState);
        Assert.Empty(state.Trackers);
        Assert.Equal(6, state.Operations.Count);
        Assert.Equal(6, state.Operations.Select(item => item.Id).Distinct().Count());
        Assert.Contains(state.Tombstones, item =>
            item.Kind == RepositoryTombstoneKind.Tracker && item.DeletedId == trackerId.Value);
        Assert.Contains(state.Tombstones, item =>
            item.Kind == RepositoryTombstoneKind.Entity && item.DeletedId == entityId.Value);
    }

    [Fact]
    public void TrackedMutationRetainsStatusHistoryAndProgressSnapshotUnderOneOperation()
    {
        ProjectRepositoryStateReducer reducer = new();
        Project project = new(ProjectId.New(), "Project", Time, Time);
        Tracker tracker = new(TrackerId.New(), project.Id, "Tracker", Time, Time);
        TrackedEntity entity = new(EntityId.New(), tracker.Id, "customer");
        EntityRepositoryState entityState = new(entity,
            new EntityAuditTimestamps(entity.Id, Time, Time, Time), [], []);
        ProjectRepositoryState state = new(project, [new TrackerRepositoryState(tracker, [entityState])], [], []);
        OperationId operationId = OperationId.New();
        TrackedEntity progressed = new(entity.Id, entity.TrackerId, entity.SourceName,
            DevelopmentStatus.InProgress, entity.Notes, entity.LifecycleState, entity.Provenance,
            entity.RequestedPriority, entity.ResponsibleDeveloper, entity.GroupName);
        ProgressSnapshotState progress = new(0, 0, 1, 0, 0, 0);
        TrackedStateChangeSet changes = new(
            [], [], [], [], [], [],
            entitiesWithProgressToUpdate: [progressed],
            progressSnapshotAfterChanges: progress,
            operationId: operationId);

        ProjectRepositoryState updated = reducer.Apply(state, new ChangeTrackedStateMutation(
            project.Id,
            tracker.Id,
            changes,
            null,
            RepositoryOperationKind.StatusUpdated,
            Time.AddMinutes(1)));

        RepositoryOperation operation = Assert.Single(updated.Operations);
        Assert.Equal(operationId, operation.Id);
        Assert.Equal(operationId, Assert.Single(operation.StatusTransitions).OperationId);
        Assert.Equal(progress, Assert.Single(operation.RecordedProgressSnapshots).State);
        Assert.Equal(DevelopmentStatus.InProgress,
            Assert.Single(Assert.Single(updated.Trackers).Entities).Entity.Status);
    }
}

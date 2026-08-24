using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests.History;

public sealed class ProgressSnapshotCalculatorTests
{
    [Fact]
    public void Calculate_CountsExclusiveWorkflowStatesAndExcludesArchivedEntities()
    {
        TrackedEntity ready = Entity(1, "Ready");
        TrackedEntity blocked = Entity(2, "Blocked");
        TrackedEntity inProgress = Entity(3, "In progress", DevelopmentStatus.InProgress);
        TrackedEntity rework = Entity(4, "Rework", DevelopmentStatus.ReworkNeeded);
        TrackedEntity completed = Entity(5, "Completed", DevelopmentStatus.DevelopmentCompleted);
        TrackedEntity reconciled = Entity(6, "Reconciled", DevelopmentStatus.Reconciled);
        TrackedEntity archived = new(
            new EntityId(new Guid(7, 0, 0, new byte[8])),
            "Archived",
            lifecycleState: EntityLifecycleState.Archived);
        TrackedEntity[] entities =
            [ready, blocked, inProgress, rework, completed, reconciled, archived];
        EffectiveDependencyState effective = new EffectiveDependencyResolver().Resolve(
            entities,
            [],
            [new PersistedUnresolvedDependency(
                new UnresolvedDependency(blocked.Id, "Missing"),
                ImportedDependencyKind.Mandatory)],
            []);

        ProgressSnapshotState result = new ProgressSnapshotCalculator().Calculate(
            entities,
            effective);

        Assert.Equal(1, result.ReadyCount);
        Assert.Equal(1, result.BlockedCount);
        Assert.Equal(1, result.InProgressCount);
        Assert.Equal(1, result.ReworkNeededCount);
        Assert.Equal(1, result.DevelopmentCompletedCount);
        Assert.Equal(1, result.ReconciledCount);
        Assert.Equal(3, result.ImplementedCount);
        Assert.Equal(6, result.TotalActiveCount);
    }

    private static TrackedEntity Entity(
        int id,
        string name,
        DevelopmentStatus status = DevelopmentStatus.NotStarted) =>
        new(new EntityId(new Guid(id, 0, 0, new byte[8])), name, status);
}

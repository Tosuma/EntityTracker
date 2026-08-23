using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests.Workflow;

public sealed class WorkflowReadinessEvaluatorTests
{
    [Fact]
    public void Evaluate_ReturnsEveryDirectUnimplementedOrUnresolvedDependency()
    {
        TrackedEntity owner = Entity(1, "Owner");
        TrackedEntity notStarted = Entity(2, "Zulu", DevelopmentStatus.NotStarted);
        TrackedEntity inProgress = Entity(3, "alpha", DevelopmentStatus.InProgress);
        TrackedEntity developmentCompleted = Entity(
            4,
            "DevelopmentCompleted",
            DevelopmentStatus.DevelopmentCompleted);
        TrackedEntity reconciled = Entity(5, "Reconciled", DevelopmentStatus.Reconciled);
        EffectiveDependencyState dependencies = new EffectiveDependencyResolver().Resolve(
            [owner, notStarted, inProgress, developmentCompleted, reconciled],
            [
                Dependency(owner, notStarted, ImportedDependencyKind.Optional),
                Dependency(owner, inProgress, ImportedDependencyKind.Mandatory),
                Dependency(owner, developmentCompleted, ImportedDependencyKind.Mandatory),
                Dependency(owner, reconciled, ImportedDependencyKind.Optional)
            ],
            [Unresolved(owner, "Missing", ImportedDependencyKind.Optional)],
            []);

        EntityReadiness readiness = new WorkflowReadinessEvaluator()
            .Evaluate(
                [owner, notStarted, inProgress, developmentCompleted, reconciled],
                dependencies)[owner.Id];

        Assert.False(readiness.IsReady);
        Assert.Equal(["alpha", "Missing", "Zulu"],
            readiness.Blockers.Select(static blocker => blocker.SourceName));
        Assert.Equal(
            [
                DependencyBlockerKind.Incomplete,
                DependencyBlockerKind.Unresolved,
                DependencyBlockerKind.Incomplete
            ],
            readiness.Blockers.Select(static blocker => blocker.Kind));
        Assert.DoesNotContain(readiness.Blockers, blocker =>
            blocker.SourceName is "DevelopmentCompleted" or "Reconciled");
    }

    [Fact]
    public void Evaluate_DoesNotExpandBlockedDependencyToItsTransitiveRootCause()
    {
        TrackedEntity direct = Entity(1, "Direct");
        TrackedEntity owner = Entity(2, "Owner");
        EffectiveDependencyState dependencies = new EffectiveDependencyResolver().Resolve(
            [owner, direct],
            [Dependency(owner, direct, ImportedDependencyKind.Mandatory)],
            [Unresolved(direct, "RootCause", ImportedDependencyKind.Mandatory)],
            []);

        IReadOnlyDictionary<EntityId, EntityReadiness> result =
            new WorkflowReadinessEvaluator().Evaluate([owner, direct], dependencies);

        Assert.Equal(["Direct"],
            result[owner.Id].Blockers.Select(static blocker => blocker.SourceName));
        Assert.Equal(["RootCause"],
            result[direct.Id].Blockers.Select(static blocker => blocker.SourceName));
    }

    [Fact]
    public void Classify_CreatesExclusiveWorkflowStates()
    {
        WorkflowReadinessEvaluator evaluator = new();
        TrackedEntity ready = Entity(1, "Ready");
        TrackedEntity blocked = Entity(2, "Blocked");
        EffectiveDependencyState effectiveState = new EffectiveDependencyResolver().Resolve(
            [ready, blocked],
            [],
            [Unresolved(blocked, "Missing", ImportedDependencyKind.Mandatory)],
            []);
        IReadOnlyDictionary<EntityId, EntityReadiness> readiness =
            evaluator.Evaluate([ready, blocked], effectiveState);
        EntityReadiness noBlockers = readiness[ready.Id];
        EntityReadiness blockers = readiness[blocked.Id];

        Assert.Equal(EntityWorkflowState.Ready, evaluator.Classify(ready, noBlockers));
        Assert.Equal(EntityWorkflowState.Blocked, evaluator.Classify(blocked, blockers));
        Assert.Equal(
            EntityWorkflowState.InProgress,
            evaluator.Classify(Entity(3, "InProgress", DevelopmentStatus.InProgress), blockers));
        Assert.Equal(
            EntityWorkflowState.DevelopmentCompleted,
            evaluator.Classify(Entity(
                4,
                "DevelopmentCompleted",
                DevelopmentStatus.DevelopmentCompleted), blockers));
        Assert.Equal(
            EntityWorkflowState.Reconciled,
            evaluator.Classify(Entity(5, "Reconciled", DevelopmentStatus.Reconciled), blockers));
        Assert.Equal(
            EntityWorkflowState.Archived,
            evaluator.Classify(new TrackedEntity(
                new EntityId(new Guid(6, 0, 0, new byte[8])),
                "Archived",
                lifecycleState: EntityLifecycleState.Archived)));
    }

    private static TrackedEntity Entity(
        int id,
        string name,
        DevelopmentStatus status = DevelopmentStatus.NotStarted) =>
        new(new EntityId(new Guid(id, 0, 0, new byte[8])), name, status);

    private static PersistedDependency Dependency(
        TrackedEntity owner,
        TrackedEntity target,
        ImportedDependencyKind kind) => new(new DependencyEdge(owner.Id, target.Id), kind);

    private static PersistedUnresolvedDependency Unresolved(
        TrackedEntity owner,
        string targetName,
        ImportedDependencyKind kind) => new(new UnresolvedDependency(owner.Id, targetName), kind);
}

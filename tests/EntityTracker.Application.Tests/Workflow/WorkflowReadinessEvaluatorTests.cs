using EntityTracker.Application.Dependencies;
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
        TrackedEntity rework = Entity(6, "Rework", DevelopmentStatus.ReworkNeeded);
        TrackedEntity[] entities = [owner, notStarted, inProgress, developmentCompleted, reconciled, rework];
        EffectiveDependencyState dependencies = TestDependencies.Resolve(
                entities,
                [
                    Dependency(owner, notStarted),
                    Dependency(owner, inProgress),
                    Dependency(owner, developmentCompleted),
                    Dependency(owner, reconciled),
                    Dependency(owner, rework)
                ],
                [Unresolved(owner, "Missing")]);

        EntityReadiness readiness = new WorkflowReadinessEvaluator()
            .Evaluate(
                [owner, notStarted, inProgress, developmentCompleted, reconciled, rework],
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
            blocker.SourceName is "DevelopmentCompleted" or "Reconciled" or "Rework");
    }

    [Fact]
    public void Evaluate_DoesNotExpandBlockedDependencyToItsTransitiveRootCause()
    {
        TrackedEntity direct = Entity(1, "Direct");
        TrackedEntity owner = Entity(2, "Owner");
        EffectiveDependencyState dependencies = TestDependencies.Resolve(
            [owner, direct], [Dependency(owner, direct)], [Unresolved(direct, "RootCause")]);

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
        EffectiveDependencyState effectiveState = TestDependencies.Resolve(
            [ready, blocked], [], [Unresolved(blocked, "Missing")]);
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
            EntityWorkflowState.ReworkNeeded,
            evaluator.Classify(Entity(6, "Rework", DevelopmentStatus.ReworkNeeded), blockers));
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
                new EntityId(new Guid(7, 0, 0, new byte[8])),
                TestTrackerId,
                "Archived",
                lifecycleState: EntityLifecycleState.Archived)));
    }

    [Fact]
    public void ManualBlockStopsDependentsWhileBothReworkStatesSatisfyThem()
    {
        TrackedEntity owner = Entity(10, "Owner");
        TrackedEntity manualBlock = Entity(11, "Manual block", DevelopmentStatus.Blocked);
        TrackedEntity pendingRework = Entity(12, "Pending rework", DevelopmentStatus.ReworkNeeded);
        TrackedEntity activeRework = Entity(13, "Active rework", DevelopmentStatus.Reworking);
        TrackedEntity[] entities = [owner, manualBlock, pendingRework, activeRework];
        EffectiveDependencyState effective = TestDependencies.Resolve(entities, [Dependency(owner, manualBlock),
             Dependency(owner, pendingRework),
             Dependency(owner, activeRework)], []);

        WorkflowReadinessEvaluator evaluator = new();
        IReadOnlyDictionary<EntityId, EntityReadiness> readiness = evaluator.Evaluate(entities, effective);

        Assert.Equal(["Manual block"],
            readiness[owner.Id].Blockers.Select(static blocker => blocker.SourceName));
        Assert.Equal(EntityWorkflowState.ManuallyBlocked,
            evaluator.Classify(manualBlock, readiness[manualBlock.Id]));
        Assert.Equal(EntityWorkflowState.ReworkNeeded,
            evaluator.Classify(pendingRework, readiness[pendingRework.Id]));
        Assert.Equal(EntityWorkflowState.Reworking,
            evaluator.Classify(activeRework, readiness[activeRework.Id]));
    }

    [Fact]
    public void ANotRelevantDependencyBlocksNothing()
    {
        // #73: out of scope in this Tracker, so the entities that depend on it can start.
        TrackedEntity owner = Entity(1, "Owner");
        TrackedEntity notRelevant = Entity(2, "Legacy fax number", DevelopmentStatus.NotRelevant);
        TrackedEntity[] entities = [owner, notRelevant];
        EffectiveDependencyState dependencies = TestDependencies.Resolve(
            entities, [Dependency(owner, notRelevant)], []);
        WorkflowReadinessEvaluator evaluator = new();

        IReadOnlyDictionary<EntityId, EntityReadiness> readiness = evaluator.Evaluate(entities, dependencies);

        Assert.True(readiness[owner.Id].IsReady);
        Assert.Equal(EntityWorkflowState.Ready, evaluator.Classify(owner, readiness[owner.Id]));
        Assert.Equal(EntityWorkflowState.NotRelevant, evaluator.Classify(notRelevant, readiness[notRelevant.Id]));
    }

    [Fact]
    public void ANotRelevantEntityHasNoBlockersOfItsOwn()
    {
        // #73: out of scope, so its own unfinished or missing dependencies don't matter.
        TrackedEntity notRelevant = Entity(1, "Legacy fax number", DevelopmentStatus.NotRelevant);
        TrackedEntity unfinished = Entity(2, "Unfinished");
        TrackedEntity[] entities = [notRelevant, unfinished];
        EffectiveDependencyState dependencies = TestDependencies.Resolve(
            entities, [Dependency(notRelevant, unfinished)], [Unresolved(notRelevant, "Missing")]);

        EntityReadiness readiness = new WorkflowReadinessEvaluator().Evaluate(entities, dependencies)[notRelevant.Id];

        Assert.Empty(readiness.Blockers);
    }

    private static TrackedEntity Entity(
        int id,
        string name,
        DevelopmentStatus status = DevelopmentStatus.NotStarted) =>
        new(new EntityId(new Guid(id, 0, 0, new byte[8])), TestTrackerId, name, status);

    private static PersistedDependency Dependency(
        TrackedEntity owner,
        TrackedEntity target) => new(new DependencyEdge(owner.Id, target.Id));

    private static PersistedUnresolvedDependency Unresolved(
        TrackedEntity owner,
        string targetName) => new(new UnresolvedDependency(owner.Id, targetName));
}

using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Planning;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests.Planning;

public sealed class PriorityPlanningServiceTests
{
    private readonly EffectiveDependencyResolver _resolver = new();
    private readonly PriorityPlanningService _service = new();

    [Fact]
    public void CalculateEffectivePriorities_PropagatesThroughTransitivePrerequisites()
    {
        TrackedEntity foundation = Entity(1, "Foundation");
        TrackedEntity middle = Entity(2, "Middle");
        TrackedEntity target = Entity(3, "Target", 2);
        TrackedEntity unrelated = Entity(4, "Unrelated");
        TrackedEntity[] entities = [foundation, middle, target, unrelated];
        EffectiveDependencyState state = Resolve(
            entities,
            [Dependency(target, middle), Dependency(middle, foundation)]);

        IReadOnlyDictionary<EntityId, int?> priorities =
            _service.CalculateEffectivePriorities(entities, state);

        Assert.Equal(2, priorities[foundation.Id]);
        Assert.Equal(2, priorities[middle.Id]);
        Assert.Equal(2, priorities[target.Id]);
        Assert.Null(priorities[unrelated.Id]);
    }

    [Fact]
    public void CalculateEffectivePriorities_RecomputesSharedMinimumAfterChangeAndClear()
    {
        TrackedEntity shared = Entity(1, "Shared");
        TrackedEntity highTarget = Entity(2, "High", 1);
        TrackedEntity lowerTarget = Entity(3, "Lower", 4);
        PersistedDependency[] dependencies =
        [
            Dependency(highTarget, shared),
            Dependency(lowerTarget, shared)
        ];

        IReadOnlyDictionary<EntityId, int?> initial = _service.CalculateEffectivePriorities(
            [shared, highTarget, lowerTarget],
            Resolve([shared, highTarget, lowerTarget], dependencies));
        Assert.Equal(1, initial[shared.Id]);

        highTarget.ChangeRequestedPriority(null);
        IReadOnlyDictionary<EntityId, int?> afterChange = _service.CalculateEffectivePriorities(
            [shared, highTarget, lowerTarget],
            Resolve([shared, highTarget, lowerTarget], dependencies));
        Assert.Equal(4, afterChange[shared.Id]);

        lowerTarget.ChangeRequestedPriority(null);
        IReadOnlyDictionary<EntityId, int?> afterClear = _service.CalculateEffectivePriorities(
            [shared, highTarget, lowerTarget],
            Resolve([shared, highTarget, lowerTarget], dependencies));
        Assert.Null(afterClear[shared.Id]);
    }

    [Fact]
    public void CalculateEffectivePriorities_UsesManualOverridesAndExcludesArchivedTargets()
    {
        TrackedEntity importedTarget = Entity(1, "ImportedTarget");
        TrackedEntity manualTarget = Entity(2, "ManualTarget");
        TrackedEntity owner = Entity(3, "Owner", 3);
        TrackedEntity[] active = [importedTarget, manualTarget, owner];
        PersistedDependency imported = Dependency(owner, importedTarget);
        ManualDependencyOverride[] overrides =
        [
            new(owner.Id, importedTarget.SourceName, ManualDependencyOverrideAction.Suppress),
            new(owner.Id, manualTarget.SourceName, ManualDependencyOverrideAction.Add)
        ];

        IReadOnlyDictionary<EntityId, int?> activePriorities =
            _service.CalculateEffectivePriorities(
                active,
                _resolver.Resolve(active, [imported], [], overrides));
        Assert.Null(activePriorities[importedTarget.Id]);
        Assert.Equal(3, activePriorities[manualTarget.Id]);

        TrackedEntity archivedOwner = new(
            owner.Id,
            owner.SourceName,
            lifecycleState: EntityLifecycleState.Archived,
            requestedPriority: owner.RequestedPriority);
        TrackedEntity[] archivedState = [importedTarget, manualTarget, archivedOwner];
        IReadOnlyDictionary<EntityId, int?> archivedPriorities =
            _service.CalculateEffectivePriorities(
                archivedState,
                _resolver.Resolve(archivedState, [imported], [], overrides));
        Assert.DoesNotContain(owner.Id, archivedPriorities.Keys);
        Assert.Null(archivedPriorities[manualTarget.Id]);

        TrackedEntity restoredOwner = new(
            owner.Id,
            owner.SourceName,
            requestedPriority: archivedOwner.RequestedPriority);
        TrackedEntity[] restoredState = [importedTarget, manualTarget, restoredOwner];
        IReadOnlyDictionary<EntityId, int?> restoredPriorities =
            _service.CalculateEffectivePriorities(
                restoredState,
                _resolver.Resolve(restoredState, [imported], [], overrides));
        Assert.Equal(3, restoredPriorities[manualTarget.Id]);
    }

    [Fact]
    public void CreatePreview_OrdersPrerequisitesAndReportsOnlyReachableUnresolvedNames()
    {
        TrackedEntity foundation = Entity(1, "Foundation");
        TrackedEntity middle = Entity(2, "Middle");
        TrackedEntity target = Entity(3, "Target");
        TrackedEntity unrelated = Entity(4, "Unrelated");
        TrackedEntity[] entities = [foundation, middle, target, unrelated];
        EffectiveDependencyState state = _resolver.Resolve(
            entities,
            [Dependency(target, middle), Dependency(middle, foundation)],
            [Unresolved(middle, "zeta_missing"), Unresolved(target, "Alpha_missing"),
                Unresolved(unrelated, "Ignored")],
            []);

        PriorityPlanningPreview preview = _service.CreatePreview(
            target.Id,
            1,
            entities,
            state);

        Assert.Equal(
            ["Foundation", "Middle", "Target"],
            preview.Entities.Select(static item => item.SourceName));
        Assert.Equal(["Prerequisite", "Prerequisite", "Target"], preview.Entities.Select(
            static item => item.IsTarget ? "Target" : "Prerequisite"));
        Assert.All(preview.Entities, static item => Assert.Equal(1, item.EffectivePriority));
        Assert.Equal(["Alpha_missing", "zeta_missing"], preview.UnresolvedDependencyNames);
    }

    [Fact]
    public void CreatePreview_ClearingCandidateShowsPriorityInheritedFromAnotherTarget()
    {
        TrackedEntity shared = Entity(1, "Shared");
        TrackedEntity selected = Entity(2, "Selected", 2);
        TrackedEntity other = Entity(3, "Other", 1);
        TrackedEntity[] entities = [shared, selected, other];
        EffectiveDependencyState state = Resolve(
            entities,
            [Dependency(selected, shared), Dependency(other, shared)]);

        PriorityPlanningPreview preview = _service.CreatePreview(
            selected.Id,
            null,
            entities,
            state);

        PriorityPlanningItem selectedItem = preview.Entities.Single(static item => item.IsTarget);
        PriorityPlanningItem sharedItem = preview.Entities.Single(item => item.EntityId == shared.Id);
        Assert.Null(selectedItem.RequestedPriority);
        Assert.Null(selectedItem.EffectivePriority);
        Assert.Equal(1, sharedItem.EffectivePriority);
    }

    private EffectiveDependencyState Resolve(
        IReadOnlyList<TrackedEntity> entities,
        IReadOnlyList<PersistedDependency> dependencies) =>
        _resolver.Resolve(entities, dependencies, [], []);

    private static PersistedDependency Dependency(TrackedEntity owner, TrackedEntity target) =>
        new(new DependencyEdge(owner.Id, target.Id), ImportedDependencyKind.Mandatory);

    private static PersistedUnresolvedDependency Unresolved(
        TrackedEntity owner,
        string targetName) =>
        new(
            new UnresolvedDependency(owner.Id, targetName),
            ImportedDependencyKind.Mandatory);

    private static TrackedEntity Entity(int id, string name, int? priority = null) =>
        new(Id(id), name, requestedPriority: priority);

    private static EntityId Id(int id) => new(new Guid(id, 0, 0, new byte[8]));
}

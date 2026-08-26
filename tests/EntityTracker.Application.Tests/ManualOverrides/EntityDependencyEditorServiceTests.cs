using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Importing;
using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Planning;
using EntityTracker.Application.Ranking;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests.ManualOverrides;

public sealed class EntityDependencyEditorServiceTests
{
    [Fact]
    public async Task LoadAsync_DescribesImportedDependencyWithoutCreatingOverride()
    {
        TrackedEntity owner = Entity(1, "Owner");
        TrackedEntity target = Entity(2, "Target");
        EntityDependencyEditorService service = Service(
            [owner, target],
            [Dependency(owner, target)],
            [],
            [],
            out _);

        EntityDependencyEditPlan plan = await service.LoadAsync(owner.Id);

        EntityDependencyEditItem dependency = Assert.Single(plan.Dependencies);
        Assert.Equal(DependencyEditOrigin.Imported, dependency.Origin);
        Assert.True(dependency.IsResolved);
        Assert.Empty(plan.DesiredOverrides);
        Assert.True(plan.IsValid);
    }

    [Fact]
    public void CreatePlan_SuppressesImportedFactWithoutRemovingRawDependency()
    {
        TrackedEntity owner = Entity(1, "Owner");
        TrackedEntity target = Entity(2, "Target");
        PersistedDependency imported = Dependency(owner, target);
        EntityDependencyEditorService service = Service(
            [owner, target],
            [imported],
            [],
            [],
            out _);

        EntityDependencyEditPlan plan = service.CreatePlan(
            owner.Id,
            [owner, target],
            [imported],
            [],
            [],
            [new ManualDependencyOverride(
                owner.Id,
                target.SourceName,
                ManualDependencyOverrideAction.Suppress)]);

        Assert.True(plan.IsValid);
        Assert.Equal(
            DependencyEditOrigin.SuppressedImported,
            Assert.Single(plan.Dependencies).Origin);
        Assert.Empty(plan.EffectiveState.ResolvedDependencies);
        Assert.Equal(new DependencyEdge(owner.Id, target.Id), imported.Edge);
    }

    [Fact]
    public void CreatePlan_UnknownManualAdditionIsAValidUnresolvedWarning()
    {
        TrackedEntity owner = Entity(1, "Owner");
        EntityDependencyEditorService service = Service([owner], [], [], [], out _);

        EntityDependencyEditPlan plan = service.CreatePlan(
            owner.Id,
            [owner],
            [],
            [],
            [],
            [new ManualDependencyOverride(
                owner.Id,
                "Future",
                ManualDependencyOverrideAction.Add)]);

        Assert.True(plan.IsValid);
        Assert.NotEmpty(plan.Warnings);
        Assert.Equal(
            "Future",
            Assert.Single(plan.EffectiveState.UnresolvedDependencies)
                .Dependency.DependencySourceName);
    }

    [Fact]
    public void CreatePlan_CycleIsRejectedImmediately()
    {
        TrackedEntity a = Entity(1, "A");
        TrackedEntity b = Entity(2, "B");
        EntityDependencyEditorService service = Service(
            [a, b],
            [Dependency(a, b)],
            [],
            [],
            out _);

        EntityDependencyEditPlan plan = service.CreatePlan(
            b.Id,
            [a, b],
            [Dependency(a, b)],
            [],
            [],
            [new ManualDependencyOverride(
                b.Id,
                a.SourceName,
                ManualDependencyOverrideAction.Add)]);

        Assert.False(plan.IsValid);
        Assert.Contains(plan.CandidateRanking.Diagnostics, diagnostic =>
            diagnostic.Code == DependencyRankingDiagnosticCode.CycleDetected);
    }

    [Fact]
    public async Task SaveAsync_ReconcilesOnlySelectedOwnersOverrides()
    {
        TrackedEntity owner = Entity(1, "Owner");
        EntityDependencyEditorService service = Service([owner], [], [], [], out StubStore store);
        EntityDependencyEditPlan plan = service.CreatePlan(
            owner.Id,
            [owner],
            [],
            [],
            [],
            [new ManualDependencyOverride(
                owner.Id,
                "Future",
                ManualDependencyOverrideAction.Add)]);

        await service.SaveAsync(
            plan,
            owner.Status,
            owner.Notes,
            owner.RequestedPriority,
            owner.ResponsibleDeveloper,
            owner.GroupName);

        TrackedStateChangeSet changeSet = Assert.IsType<TrackedStateChangeSet>(
            store.LastChangeSet);
        Assert.Equal(owner.Id, Assert.Single(changeSet.ReconciledOverrideOwnerIds));
        Assert.Equal(
            1,
            Assert.IsType<ProgressSnapshotState>(changeSet.ProgressSnapshotAfterChanges)
                .BlockedCount);
        Assert.Single(changeSet.ManualDependencyOverrides);
        Assert.Empty(changeSet.ReconciledOwnerIds);
        Assert.Empty(changeSet.ResolvedDependencies);
        Assert.Empty(changeSet.UnresolvedDependencies);
        Assert.Empty(changeSet.EntitiesWithProgressToUpdate);
    }

    [Fact]
    public async Task SaveAsync_StatusNotesAndOverridesShareOneChangeSet()
    {
        TrackedEntity owner = Entity(1, "Owner", "Core Team");
        EntityDependencyEditorService service = Service([owner], [], [], [], out StubStore store);
        EntityDependencyEditPlan plan = service.CreatePlan(
            owner.Id,
            [owner],
            [],
            [],
            [],
            [new ManualDependencyOverride(
                owner.Id,
                "Future",
                ManualDependencyOverrideAction.Add)]);

        await service.SaveAsync(
            plan,
            DevelopmentStatus.Reconciled,
            "Verified notes",
            owner.RequestedPriority,
            owner.ResponsibleDeveloper,
            owner.GroupName);

        TrackedStateChangeSet changeSet = Assert.IsType<TrackedStateChangeSet>(
            store.LastChangeSet);
        TrackedEntity progress = Assert.Single(changeSet.EntitiesWithProgressToUpdate);
        Assert.Equal(owner.Id, progress.Id);
        Assert.Equal(DevelopmentStatus.Reconciled, progress.Status);
        Assert.Equal(
            1,
            Assert.IsType<ProgressSnapshotState>(changeSet.ProgressSnapshotAfterChanges)
                .ReconciledCount);
        Assert.Equal("Verified notes", progress.Notes);
        Assert.Equal("Core Team", progress.ResponsibleDeveloper);
        Assert.Equal(owner.Id, Assert.Single(changeSet.ReconciledOverrideOwnerIds));
        Assert.Single(changeSet.ManualDependencyOverrides);
    }

    [Theory]
    [InlineData("", "  Ada Lovelace  ", "Ada Lovelace")]
    [InlineData("Existing Team", "   ", "")]
    public async Task SaveAsync_ResponsibleDeveloperChangeIsNormalizedAndIndependent(
        string existing,
        string entered,
        string expected)
    {
        TrackedEntity owner = Entity(1, "Owner", existing);
        EntityDependencyEditorService service = Service([owner], [], [], [], out StubStore store);
        EntityDependencyEditPlan plan = await service.LoadAsync(owner.Id);

        await service.SaveAsync(
            plan,
            owner.Status,
            owner.Notes,
            owner.RequestedPriority,
            entered,
            owner.GroupName);

        TrackedStateChangeSet changeSet = Assert.IsType<TrackedStateChangeSet>(store.LastChangeSet);
        TrackedEntity update = Assert.Single(
            changeSet.EntitiesWithResponsibleDeveloperToUpdate);
        Assert.Equal(expected, update.ResponsibleDeveloper);
        Assert.Empty(changeSet.EntitiesWithProgressToUpdate);
        Assert.Empty(changeSet.EntitiesWithRequestedPriorityToUpdate);
    }

    [Fact]
    public async Task SaveAsync_EquivalentNormalizedResponsibleDeveloperIsNotUpdated()
    {
        TrackedEntity owner = Entity(1, "Owner", "Platform Team");
        EntityDependencyEditorService service = Service([owner], [], [], [], out StubStore store);
        EntityDependencyEditPlan plan = await service.LoadAsync(owner.Id);

        await service.SaveAsync(
            plan,
            owner.Status,
            owner.Notes,
            owner.RequestedPriority,
            "  Platform Team  ",
            owner.GroupName);

        Assert.Empty(Assert.IsType<TrackedStateChangeSet>(store.LastChangeSet)
            .EntitiesWithResponsibleDeveloperToUpdate);
    }

    [Theory]
    [InlineData("", "  Core Data  ", "Core Data")]
    [InlineData("Existing Group", "   ", "")]
    public async Task SaveAsync_GroupNameChangeIsNormalizedAndIndependent(
        string existing,
        string entered,
        string expected)
    {
        TrackedEntity owner = Entity(1, "Owner", groupName: existing);
        EntityDependencyEditorService service = Service([owner], [], [], [], out StubStore store);
        EntityDependencyEditPlan plan = await service.LoadAsync(owner.Id);

        await service.SaveAsync(
            plan,
            owner.Status,
            owner.Notes,
            owner.RequestedPriority,
            owner.ResponsibleDeveloper,
            entered);

        TrackedStateChangeSet changeSet = Assert.IsType<TrackedStateChangeSet>(store.LastChangeSet);
        TrackedEntity update = Assert.Single(changeSet.EntitiesWithGroupNameToUpdate);
        Assert.Equal(expected, update.GroupName);
        Assert.Empty(changeSet.EntitiesWithProgressToUpdate);
        Assert.Empty(changeSet.EntitiesWithRequestedPriorityToUpdate);
        Assert.Empty(changeSet.EntitiesWithResponsibleDeveloperToUpdate);
    }

    [Fact]
    public async Task LoadArchivedDetailsAsync_ReturnsPreservedFactsAndOverridesReadOnlyProjection()
    {
        TrackedEntity owner = new(
            new EntityId(new Guid(1, 0, 0, new byte[8])),
            "Owner",
            DevelopmentStatus.Reconciled,
            "Verified",
            EntityLifecycleState.Archived,
            responsibleDeveloper: "Legacy Team");
        TrackedEntity target = Entity(2, "Target");
        EntityDependencyEditorService service = Service(
            [owner, target],
            [Dependency(owner, target, ImportedDependencyKind.Optional)],
            [],
            [new ManualDependencyOverride(
                owner.Id,
                "ManualMissing",
                ManualDependencyOverrideAction.Add)],
            out _);

        ArchivedEntityDetails details = await service.LoadArchivedDetailsAsync(owner.Id);

        Assert.Equal(owner.Id, details.Entity.Id);
        Assert.Equal(DevelopmentStatus.Reconciled, details.Entity.Status);
        Assert.Equal("Verified", details.Entity.Notes);
        Assert.Equal("Legacy Team", details.Entity.ResponsibleDeveloper);
        Assert.Equal(
            ["ManualMissing", "Target"],
            details.Dependencies.Select(static dependency => dependency.DependencySourceName));
    }

    [Fact]
    public async Task SearchDependenciesAsync_ExcludesOwnerAndArchivedEntities()
    {
        TrackedEntity owner = Entity(1, "Owner");
        TrackedEntity active = Entity(2, "ActiveTarget");
        TrackedEntity archived = new(
            Id(3),
            "ArchivedTarget",
            lifecycleState: EntityLifecycleState.Archived);
        EntityDependencyEditorService service = Service(
            [owner, active, archived], [], [], [], out _);

        var result = await service.SearchDependenciesAsync(owner.Id, "target");

        Assert.Equal(active.Id, Assert.Single(result.Suggestions).EntityId);
        Assert.DoesNotContain(result.Suggestions, item => item.EntityId == owner.Id);
        Assert.DoesNotContain(result.Suggestions, item => item.EntityId == archived.Id);
    }

    [Fact]
    public async Task SearchGroupNamesAsync_IncludesGroupsUsedOnlyByArchivedEntities()
    {
        TrackedEntity owner = Entity(1, "Owner", groupName: "Current Team");
        TrackedEntity archived = new(
            Id(2),
            "Archived",
            lifecycleState: EntityLifecycleState.Archived,
            groupName: "Legacy Team");
        EntityDependencyEditorService service = Service(
            [owner, archived], [], [], [], out _);

        IReadOnlyList<string> suggestions = await service.SearchGroupNamesAsync("team");

        Assert.Equal(["Current Team", "Legacy Team"], suggestions);
    }

    [Fact]
    public async Task PriorityPreviewAndSave_UseCandidateGraphAndPersistOnlyRequestedValue()
    {
        TrackedEntity prerequisite = Entity(1, "Prerequisite");
        TrackedEntity owner = Entity(2, "Owner");
        PersistedDependency dependency = new(
            new DependencyEdge(owner.Id, prerequisite.Id),
            ImportedDependencyKind.Mandatory);
        EntityDependencyEditorService service = Service(
            [owner, prerequisite],
            [dependency],
            [],
            [],
            out StubStore store);
        EntityDependencyEditPlan plan = await service.LoadAsync(owner.Id);

        PriorityPlanningPreview preview = service.CreatePriorityPreview(plan, 2);

        Assert.Equal(
            ["Prerequisite", "Owner"],
            preview.Entities.Select(static item => item.SourceName));
        Assert.All(preview.Entities, static item => Assert.Equal(2, item.EffectivePriority));

        await service.SaveAsync(
            plan,
            owner.Status,
            owner.Notes,
            2,
            owner.ResponsibleDeveloper,
            owner.GroupName);

        TrackedStateChangeSet changeSet = Assert.IsType<TrackedStateChangeSet>(store.LastChangeSet);
        TrackedEntity priorityUpdate = Assert.Single(
            changeSet.EntitiesWithRequestedPriorityToUpdate);
        Assert.Equal(owner.Id, priorityUpdate.Id);
        Assert.Equal(2, priorityUpdate.RequestedPriority);
        Assert.Empty(changeSet.EntitiesWithProgressToUpdate);
    }

    private static EntityDependencyEditorService Service(
        IReadOnlyList<TrackedEntity> entities,
        IReadOnlyList<PersistedDependency> resolved,
        IReadOnlyList<PersistedUnresolvedDependency> unresolved,
        IReadOnlyList<ManualDependencyOverride> overrides,
        out StubStore store)
    {
        store = new StubStore();
        return new EntityDependencyEditorService(
            new StubEntityRepository(entities),
            new StubDependencyRepository(resolved, unresolved),
            new StubManualDependencyOverrideRepository(overrides),
            new EffectiveDependencyResolver(),
            new DependencyRanker(),
            store,
            new PriorityPlanningService());
    }

    private static TrackedEntity Entity(
        int id,
        string name,
        string? responsibleDeveloper = null,
        string? groupName = null) =>
        new(
            Id(id),
            name,
            responsibleDeveloper: responsibleDeveloper,
            groupName: groupName);

    private static EntityId Id(int id) => new(new Guid(id, 0, 0, new byte[8]));

    private static PersistedDependency Dependency(
        TrackedEntity owner,
        TrackedEntity target,
        ImportedDependencyKind kind = ImportedDependencyKind.Mandatory) =>
        new(new DependencyEdge(owner.Id, target.Id), kind);

    private sealed class StubEntityRepository(IReadOnlyList<TrackedEntity> entities)
        : IEntityRepository
    {
        public Task<TrackedEntity?> GetAsync(
            EntityId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(entities.SingleOrDefault(entity => entity.Id == id));

        public Task<IReadOnlyList<TrackedEntity>> GetAllAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(entities);

    }

    private sealed class StubDependencyRepository(
        IReadOnlyList<PersistedDependency> resolved,
        IReadOnlyList<PersistedUnresolvedDependency> unresolved) : IDependencyRepository
    {
        public Task<IReadOnlyList<PersistedDependency>> GetAllAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(resolved);

        public Task<IReadOnlyList<PersistedUnresolvedDependency>> GetAllUnresolvedAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(unresolved);

    }

    private sealed class StubStore : ITrackedStateStore
    {
        public TrackedStateChangeSet? LastChangeSet { get; private set; }

        public Task ApplyAsync(
            TrackedStateChangeSet changeSet,
            CancellationToken cancellationToken = default)
        {
            LastChangeSet = changeSet;
            return Task.CompletedTask;
        }

        public Task EnsureHistoryBaselineAsync(
            IEnumerable<TrackedEntity> entities,
            EntityTracker.Application.History.ProgressSnapshotState snapshot,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

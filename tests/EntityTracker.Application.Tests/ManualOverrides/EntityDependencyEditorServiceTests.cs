using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Planning;
using EntityTracker.Application.Ranking;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests.ManualOverrides;

public sealed class EntityDependencyEditorServiceTests
{
    [Fact]
    public async Task LoadAsync_DescribesTheEntitysDependencies()
    {
        TrackedEntity owner = Entity(1, "Owner");
        TrackedEntity target = Entity(2, "Target");
        TrackedEntity missing = TrackedEntity.Placeholder(TestTrackerId, "Missing");
        EntityDependencyEditorService service = Service(
            [owner, target, missing],
            [new EntityDependency(owner.Id, target.Id), new EntityDependency(owner.Id, missing.Id)],
            out _);

        EntityDependencyEditPlan plan = await service.LoadAsync(owner.Id);

        Assert.Equal(
            [("Missing", false), ("Target", true)],
            plan.Dependencies.Select(static item => (item.DependencySourceName, item.IsResolved)));
        Assert.Equal(2, plan.DesiredDependencies.Count);
        Assert.True(plan.IsValid);
    }

    [Fact]
    public void CreatePlan_RemovingADependencyTakesItOutOfTheGraph()
    {
        TrackedEntity owner = Entity(1, "Owner");
        TrackedEntity target = Entity(2, "Target");
        EntityDependency dependency = new EntityDependency(owner.Id, target.Id);
        EntityDependencyEditorService service = Service([owner, target], [dependency], out _);

        EntityDependencyEditPlan plan = service.CreatePlan(owner.Id, [owner, target], [dependency], []);

        Assert.True(plan.IsValid);
        Assert.Empty(plan.Dependencies);
        Assert.Empty(plan.EffectiveState.ResolvedDependencies);
    }

    [Fact]
    public void CreatePlan_UnknownNameIsAValidUnresolvedWarning()
    {
        TrackedEntity owner = Entity(1, "Owner");
        EntityDependencyEditorService service = Service([owner], [], out _);

        EntityDependencyEditPlan plan = service.CreatePlan(
            owner.Id,
            [owner],
            [],
            [DependencyTarget.Named("Future")]);

        Assert.True(plan.IsValid);
        Assert.NotEmpty(plan.Warnings);
        Assert.Equal(
            "Future",
            Assert.Single(plan.EffectiveState.UnresolvedDependencies)
                .Dependency.DependencySourceName);
    }

    [Fact]
    public void CreatePlan_ArchivedTargetAndSelfDependencyAreErrors()
    {
        TrackedEntity owner = Entity(1, "Owner");
        TrackedEntity archived = new(
            Id(2),
            TestTrackerId,
            "Archived",
            lifecycleState: EntityLifecycleState.Archived);
        EntityDependencyEditorService service = Service([owner, archived], [], out _);

        EntityDependencyEditPlan plan = service.CreatePlan(
            owner.Id,
            [owner, archived],
            [],
            [DependencyTarget.Named("Archived"), DependencyTarget.Named("owner")]);

        Assert.False(plan.IsValid);
        Assert.Contains("'Archived' exists but is archived.", plan.Errors);
        Assert.Contains("An entity cannot depend on itself.", plan.Errors);
    }

    [Fact]
    public void CreatePlan_CycleIsRejectedImmediately()
    {
        TrackedEntity a = Entity(1, "A");
        TrackedEntity b = Entity(2, "B");
        EntityDependency aOnB = new EntityDependency(a.Id, b.Id);
        EntityDependencyEditorService service = Service([a, b], [aOnB], out _);

        EntityDependencyEditPlan plan = service.CreatePlan(
            b.Id,
            [a, b],
            [aOnB],
            [DependencyTarget.Named("A")]);

        Assert.False(plan.IsValid);
        Assert.Contains(plan.CandidateRanking.Diagnostics, diagnostic =>
            diagnostic.Code == DependencyRankingDiagnosticCode.CycleDetected);
    }

    [Fact]
    public async Task SaveAsync_ReplacesOnlyTheEditedEntitysDependencies()
    {
        TrackedEntity owner = Entity(1, "Owner");
        EntityDependencyEditorService service = Service([owner], [], out StubStore store);
        EntityDependencyEditPlan plan = service.CreatePlan(
            owner.Id,
            [owner],
            [],
            [DependencyTarget.Named("Future")]);

        await service.SaveAsync(
            plan,
            owner.Status,
            owner.Notes,
            owner.RequestedPriority,
            owner.ResponsibleDeveloper,
            owner.GroupName);

        TrackedStateChangeSet changeSet = Assert.IsType<TrackedStateChangeSet>(
            store.LastChangeSet);
        Assert.Equal(owner.Id, Assert.Single(changeSet.DependencyOwnerIds));
        Assert.Equal(
            1,
            Assert.IsType<ProgressSnapshotState>(changeSet.ProgressSnapshotAfterChanges)
                .BlockedCount);
        // The unknown name gets a placeholder entity, added with the edit.
        TrackedEntity future = Assert.Single(changeSet.EntitiesToAdd);
        Assert.True(future.IsPlaceholder);
        Assert.Equal("Future", future.SourceName);
        Assert.Equal(new EntityDependency(owner.Id, future.Id), Assert.Single(changeSet.Dependencies));
        Assert.Empty(changeSet.EntitiesWithProgressToUpdate);
    }

    [Fact]
    public async Task SaveAsync_StatusNotesAndDependenciesShareOneChangeSet()
    {
        TrackedEntity owner = Entity(1, "Owner", "Core Team");
        EntityDependencyEditorService service = Service([owner], [], out StubStore store);
        EntityDependencyEditPlan plan = service.CreatePlan(
            owner.Id,
            [owner],
            [],
            [DependencyTarget.Named("Future")]);

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
        Assert.Equal(owner.Id, Assert.Single(changeSet.DependencyOwnerIds));
        Assert.Single(changeSet.Dependencies);
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
        EntityDependencyEditorService service = Service([owner], [], out StubStore store);
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
        EntityDependencyEditorService service = Service([owner], [], out StubStore store);
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
        EntityDependencyEditorService service = Service([owner], [], out StubStore store);
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
    public async Task LoadArchivedDetailsAsync_ReturnsTheArchivedEntityAndItsDependencies()
    {
        TrackedEntity owner = new(
            new EntityId(new Guid(1, 0, 0, new byte[8])),
            TestTrackerId,
            "Owner",
            DevelopmentStatus.Reconciled,
            "Verified",
            EntityLifecycleState.Archived,
            responsibleDeveloper: "Legacy Team");
        TrackedEntity target = Entity(2, "Target");
        TrackedEntity missing = TrackedEntity.Placeholder(TestTrackerId, "ManualMissing");
        EntityDependencyEditorService service = Service(
            [owner, target, missing],
            [new EntityDependency(owner.Id, target.Id), new EntityDependency(owner.Id, missing.Id)],
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
            TestTrackerId,
            "ArchivedTarget",
            lifecycleState: EntityLifecycleState.Archived);
        EntityDependencyEditorService service = Service(
            [owner, active, archived], [], out _);

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
            TestTrackerId,
            "Archived",
            lifecycleState: EntityLifecycleState.Archived,
            groupName: "Legacy Team");
        EntityDependencyEditorService service = Service(
            [owner, archived], [], out _);

        IReadOnlyList<string> suggestions = await service.SearchGroupNamesAsync("team");

        Assert.Equal(["Current Team", "Legacy Team"], suggestions);
    }

    [Fact]
    public async Task PriorityPreviewAndSave_UseCandidateGraphAndPersistOnlyRequestedValue()
    {
        TrackedEntity prerequisite = Entity(1, "Prerequisite");
        TrackedEntity owner = Entity(2, "Owner");
        EntityDependencyEditorService service = Service(
            [owner, prerequisite],
            [new EntityDependency(owner.Id, prerequisite.Id)],
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

    [Fact]
    public async Task SaveAsync_RenameUpdatesTheEntityAndKeepsItsProgress()
    {
        TrackedEntity owner = Entity(1, "custmer_account", groupName: "Billing");
        EntityDependencyEditorService service = Service([owner], [], out StubStore store);
        EntityDependencyEditPlan plan = service.CreatePlan(owner.Id, [owner], [], []);

        await Save(service, plan, owner, "  customer_account ");

        TrackedStateChangeSet changeSet = Assert.IsType<TrackedStateChangeSet>(store.LastChangeSet);
        TrackedEntity renamed = Assert.Single(changeSet.EntitiesToUpdate);
        Assert.Equal((owner.Id, "customer_account", "Billing", owner.Provenance),
            (renamed.Id, renamed.SourceName, renamed.GroupName, renamed.Provenance));
        Assert.Empty(changeSet.EntitiesWithProgressToUpdate);
    }

    [Fact]
    public async Task SaveAsync_WithoutRenameDoesNotUpdateTheEntity()
    {
        TrackedEntity owner = Entity(1, "Owner");
        EntityDependencyEditorService service = Service([owner], [], out StubStore store);
        EntityDependencyEditPlan plan = service.CreatePlan(owner.Id, [owner], [], []);

        await Save(service, plan, owner, "Owner");

        Assert.Empty(Assert.IsType<TrackedStateChangeSet>(store.LastChangeSet).EntitiesToUpdate);
    }

    [Fact]
    public async Task SaveAsync_RenameOntoAPlaceholdersNameTakesOverItsDependencies()
    {
        TrackedEntity target = Entity(1, "custmer");
        TrackedEntity other = Entity(2, "invoice");
        TrackedEntity third = Entity(3, "payment");
        TrackedEntity future = TrackedEntity.Placeholder(TestTrackerId, "Future");
        TrackedEntity waiting = TrackedEntity.Placeholder(TestTrackerId, "customer");
        TrackedEntity[] entities = [target, other, third, future, waiting];
        EntityDependency[] all =
        [
            new EntityDependency(other.Id, target.Id),
            new EntityDependency(other.Id, future.Id),
            new EntityDependency(third.Id, waiting.Id)
        ];
        EntityDependencyEditorService service = Service(entities, all, out StubStore store);
        EntityDependencyEditPlan plan = service.CreatePlan(target.Id, entities, all, []);

        Assert.Null(await service.FindNameConflictAsync(TestTrackerId, target.Id, "CUSTOMER"));
        await Save(service, plan, target, "customer");

        // The dependency on the entity's ID needs no change; the dependency on the placeholder with the
        // new name moves to the entity, so the placeholder, no longer referred to, goes.
        TrackedStateChangeSet changeSet = Assert.IsType<TrackedStateChangeSet>(store.LastChangeSet);
        Assert.True(changeSet.DependencyOwnerIds.ToHashSet().SetEquals([target.Id, third.Id]));
        Assert.Equal(new EntityDependency(third.Id, target.Id), Assert.Single(changeSet.Dependencies));
        Assert.Equal(2, Assert.IsType<ProgressSnapshotState>(changeSet.ProgressSnapshotAfterChanges).BlockedCount);
    }

    [Fact]
    public void CreatePlan_StoresANameAnEntityHasAsADependencyOnItsId()
    {
        TrackedEntity owner = Entity(1, "Owner");
        TrackedEntity target = Entity(2, "Target");
        EntityDependencyEditorService service = Service([owner, target], [], out _);

        EntityDependencyEditPlan plan = service.CreatePlan(
            owner.Id, [owner, target], [], [DependencyTarget.Named(" target ")]);

        Assert.Equal(new EntityDependency(owner.Id, target.Id), Assert.Single(plan.DesiredDependencies));
        Assert.Equal("Target", Assert.Single(plan.Dependencies).DependencySourceName);
    }

    [Theory]
    [InlineData("Invoice")]
    [InlineData("  INVOICE  ")]
    public async Task SaveAsync_RefusesANameAnotherEntityAlreadyUses(string name)
    {
        TrackedEntity owner = Entity(1, "Owner");
        TrackedEntity other = Entity(2, "invoice");
        EntityDependencyEditorService service = Service([owner, other], [], out StubStore store);
        EntityDependencyEditPlan plan = service.CreatePlan(owner.Id, [owner, other], [], []);

        Assert.Equal("invoice", await service.FindNameConflictAsync(TestTrackerId, owner.Id, name));
        InvalidOperationException error =
            await Assert.ThrowsAsync<InvalidOperationException>(() => Save(service, plan, owner, name));
        Assert.Equal("Another entity in this Tracker is already called invoice.", error.Message);
        Assert.Null(store.LastChangeSet);
    }

    [Fact]
    public async Task FindNameConflictAsync_IgnoresTheEntityItselfAndCaseOnlyRenames()
    {
        TrackedEntity owner = Entity(1, "customer");
        EntityDependencyEditorService service = Service([owner], [], out _);

        Assert.Null(await service.FindNameConflictAsync(TestTrackerId, owner.Id, "Customer"));
    }

    private static Task Save(
        EntityDependencyEditorService service,
        EntityDependencyEditPlan plan,
        TrackedEntity entity,
        string name) =>
        service.SaveAsync(
            TestTrackerId,
            plan,
            entity.Status,
            entity.Notes,
            entity.RequestedPriority,
            entity.ResponsibleDeveloper,
            entity.GroupName,
            sourceName: name);

    private static EntityDependencyEditorService Service(
        IReadOnlyList<TrackedEntity> entities,
        IReadOnlyList<EntityDependency> dependencies,
        out StubStore store)
    {
        store = new StubStore();
        return new EntityDependencyEditorService(
            new StubEntityRepository(entities),
            new StubDependencyRepository(dependencies),
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
            TestTrackerId,
            name,
            responsibleDeveloper: responsibleDeveloper,
            groupName: groupName);

    private static EntityId Id(int id) => new(new Guid(id, 0, 0, new byte[8]));

    private sealed class StubEntityRepository(IReadOnlyList<TrackedEntity> entities)
        : IEntityRepository
    {
        public Task<TrackedEntity?> GetAsync(
            TrackerId trackerId,
            EntityId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(entities.SingleOrDefault(entity => entity.Id == id));

        public Task<IReadOnlyList<TrackedEntity>> GetAllAsync(
            TrackerId trackerId,
            CancellationToken cancellationToken = default) => Task.FromResult(entities);

    }

    private sealed class StubStore : ITrackedStateStore
    {
        public TrackedStateChangeSet? LastChangeSet { get; private set; }

        public Task ApplyAsync(
            TrackerId trackerId,
            TrackedStateChangeSet changeSet,
            CancellationToken cancellationToken = default)
        {
            LastChangeSet = changeSet;
            return Task.CompletedTask;
        }

        public Task EnsureHistoryBaselineAsync(
            TrackerId trackerId,
            IEnumerable<TrackedEntity> entities,
            EntityTracker.Application.History.ProgressSnapshotState snapshot,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

using EntityTracker.Application.Importing;
using EntityTracker.Application.Dependencies;
using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Synchronization;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests.Synchronization;

public sealed class SchemaSynchronizationServiceTests
{
    [Fact]
    public async Task PlanAsync_DoesNotWriteAndReplacesCsvRelativeUnknownWarning()
    {
        ImportDiagnostic warning = new(
            ImportDiagnosticCode.UnknownDependency,
            "Unknown in CSV.",
            Severity: ImportDiagnosticSeverity.Warning);
        SchemaImportCandidate candidate = Candidate(
            ["Owner"],
            [],
            [("Owner", "Target")]);
        StubStore store = new();
        SchemaSynchronizationService service = CreateService(
            SchemaImportResult.Success(candidate, [warning]),
            [],
            [],
            [],
            store);

        SchemaSynchronizationResult result = await service.PlanAsync(
            "schema.csv",
            SchemaImportMode.Complete);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.ImportDiagnostics);
        Assert.Equal("Owner", Assert.Single(result.Plan!.UnresolvedEntities).Entity.SourceName);
        Assert.Equal(0, store.ApplyCount);
    }

    [Fact]
    public async Task ApplyAsync_IdenticalPlan_IsNoOpWithoutStoreCall()
    {
        TrackedEntity entity = Entity(1, "A");
        StubStore store = new();
        SchemaSynchronizationService service = CreateService(
            SchemaImportResult.Success(Candidate(["A"], [])),
            [entity],
            [],
            [],
            store);
        SchemaSynchronizationResult result = await service.PlanAsync(
            "schema.csv",
            SchemaImportMode.Complete);

        await service.ApplyAsync(result.Plan!);

        Assert.Equal(0, store.ApplyCount);
    }

    [Fact]
    public async Task ApplyAsync_ActionablePlan_PassesEntireChangeSetOnce()
    {
        StubStore store = new();
        SchemaSynchronizationService service = CreateService(
            SchemaImportResult.Success(Candidate(["A", "B"], [])),
            [],
            [],
            [],
            store);
        SchemaSynchronizationResult result = await service.PlanAsync(
            "schema.csv",
            SchemaImportMode.Complete);

        await service.ApplyAsync(result.Plan!);

        Assert.Equal(1, store.ApplyCount);
        Assert.Equal(2, store.LastChangeSet!.EntitiesToAdd.Count);
    }

    [Fact]
    public async Task PlanAsync_CandidateCycle_ReturnsReviewThatCannotBeApplied()
    {
        StubStore store = new();
        SchemaSynchronizationService service = CreateService(
            SchemaImportResult.Success(Candidate(
                ["A", "B"],
                [("A", "B"), ("B", "A")])),
            [],
            [],
            [],
            store);

        SchemaSynchronizationResult result = await service.PlanAsync(
            "schema.csv",
            SchemaImportMode.Complete);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Plan);
        Assert.False(result.Plan.CanApply);
        Assert.Contains(result.RankingDiagnostics, static diagnostic =>
            diagnostic.Code == DependencyRankingDiagnosticCode.CycleDetected);
        Assert.Equal(0, store.ApplyCount);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApplyAsync(result.Plan));
    }

    [Fact]
    public async Task StageDependencyEdit_CanCorrectCycleAndAppliesImportAndOverrideTogether()
    {
        StubStore store = new();
        SchemaSynchronizationService service = CreateService(
            SchemaImportResult.Success(Candidate(
                ["A", "B"],
                [("A", "B"), ("B", "A")])),
            [],
            [],
            [],
            store);
        SchemaSynchronizationPlan initial = (await service.PlanAsync(
            "schema.csv",
            SchemaImportMode.Complete)).Plan!;
        TrackedEntity b = initial.CandidateEntities.Single(entity => entity.SourceName == "B");

        EntityDependencyEditPlan edit = service.PreviewDependencyEdit(
            initial,
            b.Id,
            [new ManualDependencyOverride(
                b.Id,
                "A",
                ManualDependencyOverrideAction.Suppress)]);
        SchemaSynchronizationPlan revised = service.StageDependencyEdit(initial, edit);

        Assert.True(edit.IsValid);
        Assert.True(revised.CanApply);
        Assert.Equal(0, store.ApplyCount);
        Assert.Equal(b.Id, Assert.Single(revised.ChangeSet.ReconciledOverrideOwnerIds));
        Assert.Equal(
            ManualDependencyOverrideAction.Suppress,
            Assert.Single(revised.ChangeSet.ManualDependencyOverrides).Action);

        await service.ApplyAsync(revised);

        Assert.Equal(1, store.ApplyCount);
        Assert.Equal(2, store.LastChangeSet!.EntitiesToAdd.Count);
    }

    private static SchemaSynchronizationService CreateService(
        SchemaImportResult importResult,
        IReadOnlyList<TrackedEntity> entities,
        IReadOnlyList<PersistedDependency> dependencies,
        IReadOnlyList<PersistedUnresolvedDependency> unresolved,
        StubStore store)
    {
        DependencyRanker ranker = new();
        StubEntityRepository entityRepository = new(entities);
        StubDependencyRepository dependencyRepository = new(dependencies, unresolved);
        StubManualDependencyOverrideRepository overrideRepository = new();
        EffectiveDependencyResolver resolver = new();
        SchemaSynchronizationPlanner planner = new(ranker, resolver);
        EntityDependencyEditorService editor = new(
            entityRepository,
            dependencyRepository,
            overrideRepository,
            resolver,
            ranker,
            store);
        return new SchemaSynchronizationService(
            new StubParser(importResult),
            entityRepository,
            dependencyRepository,
            overrideRepository,
            planner,
            editor,
            store);
    }

    private static TrackedEntity Entity(int id, string name) =>
        new(new EntityId(new Guid(id, 0, 0, new byte[8])), name);

    private static SchemaImportCandidate Candidate(
        IEnumerable<string> names,
        IEnumerable<(string Owner, string Target)> dependencies,
        IEnumerable<(string Owner, string Target)>? unresolved = null)
    {
        ImportedEntity[] entities = names.Select(name =>
            new ImportedEntity(EntitySourceKey.From(name), name)).ToArray();
        return new SchemaImportCandidate(
            entities,
            dependencies.Select(item => new ImportedDependency(
                EntitySourceKey.From(item.Owner),
                EntitySourceKey.From(item.Target),
                ImportedDependencyKind.Mandatory)),
            (unresolved ?? []).Select(item => new UnresolvedImportedDependency(
                EntitySourceKey.From(item.Owner),
                EntitySourceKey.From(item.Target),
                item.Target,
                ImportedDependencyKind.Mandatory)));
    }

    private sealed class StubParser(SchemaImportResult result) : ISchemaImportFileParser
    {
        public Task<SchemaImportResult> ParseAsync(
            string filePath,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class StubEntityRepository(IReadOnlyList<TrackedEntity> entities)
        : IEntityRepository
    {
        public Task<TrackedEntity?> GetAsync(EntityId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(entities.SingleOrDefault(entity => entity.Id == id));
        public Task<IReadOnlyList<TrackedEntity>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(entities);
        public Task<bool> TryAddAsync(TrackedEntity entity, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<bool> UpdateSchemaMetadataAsync(TrackedEntity entity, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubDependencyRepository(
        IReadOnlyList<PersistedDependency> dependencies,
        IReadOnlyList<PersistedUnresolvedDependency> unresolved) : IDependencyRepository
    {
        public Task<IReadOnlyList<PersistedDependency>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(dependencies);
        public Task<IReadOnlyList<PersistedUnresolvedDependency>> GetAllUnresolvedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(unresolved);
        public Task SaveAsync(PersistedDependency dependency, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task SaveUnresolvedAsync(PersistedUnresolvedDependency dependency, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubStore : ITrackedStateStore
    {
        public int ApplyCount { get; private set; }
        public TrackedStateChangeSet? LastChangeSet { get; private set; }

        public Task ApplyAsync(
            TrackedStateChangeSet changeSet,
            CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            LastChangeSet = changeSet;
            return Task.CompletedTask;
        }
    }
}

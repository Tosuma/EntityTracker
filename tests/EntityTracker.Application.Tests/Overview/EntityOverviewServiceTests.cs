using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Planning;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests.Overview;

public sealed class EntityOverviewServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsRankedPersistedDetailsAndDependencyCounts()
    {
        TrackedEntity foundation = Entity(
            1,
            "Foundation",
            DevelopmentStatus.DevelopmentCompleted,
            "Stable",
            requestedPriority: 2,
            responsibleDeveloper: "Data Team",
            groupName: "Core Data");
        TrackedEntity service = Entity(2, "Service", DevelopmentStatus.InProgress, "API underway");
        TrackedEntity screen = Entity(3, "Screen");
        PersistedDependency[] dependencies =
        [
            Dependency(service, foundation),
            Dependency(screen, service)
        ];
        EntityOverviewService serviceUnderTest = CreateService(
            [screen, service, foundation],
            dependencies.Reverse());

        EntityOverviewResult result = await serviceUnderTest.GetAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            ["Foundation", "Service", "Screen"],
            result.Items.Select(static item => item.SourceName));
        Assert.Equal([1, 2, 3], result.Items.Select(static item => item.Rank));
        Assert.Equal([0, 1, 1], result.Items.Select(static item => item.DependencyCount));
        Assert.Empty(result.Items[0].DependencyNames);
        Assert.Equal(["Foundation"], result.Items[1].DependencyNames);
        Assert.Equal(["Service"], result.Items[2].DependencyNames);
        Assert.Equal(DevelopmentStatus.DevelopmentCompleted, result.Items[0].Status);
        Assert.Equal("Stable", result.Items[0].Notes);
        Assert.Equal("Data Team", result.Items[0].ResponsibleDeveloper);
        Assert.Equal("Core Data", result.Items[0].GroupName);
        Assert.Equal(2, result.Items[0].RequestedPriority);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero),
            result.Items[0].AuditTimestamps.CreatedAtUtc);
        Assert.Equal(DevelopmentStatus.InProgress, result.Items[1].Status);
        Assert.Equal("API underway", result.Items[1].Notes);
    }

    [Fact]
    public async Task GetAsync_EmptyRepositories_ReturnsSuccessfulEmptyResult()
    {
        EntityOverviewService service = CreateService([], []);

        EntityOverviewResult result = await service.GetAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Items);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task GetAsync_DependencyNamesListResolvedAndMissingDependencies()
    {
        TrackedEntity owner = Entity(1, "Owner");
        TrackedEntity retained = Entity(3, "Retained");
        EntityOverviewService service = CreateService(
            [owner, retained],
            [Dependency(owner, retained)],
            [Unresolved(owner, "Manual Missing")]);

        EntityOverviewResult result = await service.GetAsync();

        EntityOverviewItem ownerItem = result.Items.Single(item => item.EntityId == owner.Id);
        Assert.Equal(["Manual Missing", "Retained"], ownerItem.DependencyNames);
        Assert.Equal(2, ownerItem.DependencyCount);
        Assert.Equal(["Manual Missing", "Retained"], ownerItem.MissingDependencyNames);
        Assert.Equal(EntityWorkflowState.Blocked, ownerItem.WorkflowState);
    }

    [Fact]
    public async Task GetAsync_ArchivedEntitiesAreSeparatedFromTheEffectiveOverview()
    {
        TrackedEntity active = Entity(1, "Active");
        TrackedEntity archived = new(
            new EntityId(new Guid(2, 0, 0, new byte[8])),
            TestTrackerId,
            "Archived",
            lifecycleState: EntityLifecycleState.Archived,
            requestedPriority: 4,
            responsibleDeveloper: "Legacy Team",
            groupName: "Legacy Data");
        EntityOverviewService service = CreateService(
            [active, archived],
            [],
            [Unresolved(archived, "Missing Legacy"), Unresolved(archived, "Manual Legacy")]);

        EntityOverviewResult result = await service.GetAsync();

        Assert.Equal("Active", Assert.Single(result.Items).SourceName);
        EntityOverviewItem archivedItem = Assert.Single(result.ArchivedItems);
        Assert.Equal(archived.Id, archivedItem.EntityId);
        Assert.Equal(EntityWorkflowState.Archived, archivedItem.WorkflowState);
        Assert.Null(archivedItem.DependencyState);
        Assert.Empty(archivedItem.DependencyResolutionIssueNames);
        Assert.Null(archivedItem.Rank);
        Assert.Equal(4, archivedItem.RequestedPriority);
        Assert.Equal(2, archivedItem.DependencyCount);
        Assert.Equal(["Manual Legacy", "Missing Legacy"], archivedItem.DependencyNames);
        Assert.Equal("Legacy Team", archivedItem.ResponsibleDeveloper);
        Assert.Equal("Legacy Data", archivedItem.GroupName);
    }

    [Fact]
    public async Task GetAsync_Cycle_ReturnsDiagnosticAndNoRows()
    {
        TrackedEntity alpha = Entity(1, "Alpha");
        TrackedEntity beta = Entity(2, "Beta");
        EntityOverviewService service = CreateService(
            [alpha, beta],
            [
                Dependency(alpha, beta),
                Dependency(beta, alpha)
            ]);

        EntityOverviewResult result = await service.GetAsync();

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Items);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Code == DependencyRankingDiagnosticCode.CycleDetected);
    }

    [Fact]
    public async Task GetAsync_UsesDirectImplementationBlockersWhileKeepingStructuralGraphState()
    {
        TrackedEntity alpha = Entity(1, "Alpha");
        TrackedEntity beta = Entity(2, "Beta");
        TrackedEntity safe = Entity(3, "Safe", DevelopmentStatus.DevelopmentCompleted);
        EntityOverviewService service = CreateService(
            [beta, safe, alpha],
            [Dependency(beta, alpha)],
            [Unresolved(alpha, "MissingX")]);

        EntityOverviewResult result = await service.GetAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["Safe", "Alpha", "Beta"],
            result.Items.Select(static item => item.SourceName));
        Assert.Equal([1, null, null], result.Items.Select(static item => item.Rank));
        Assert.Equal(
            [
                DependencyResolutionState.Resolved,
                DependencyResolutionState.Unresolved,
                DependencyResolutionState.Blocked
            ],
            result.Items.Select(static item => item.DependencyState));
        Assert.Equal([0, 1, 1], result.Items.Select(static item => item.DependencyCount));
        Assert.Empty(result.Items[0].DependencyNames);
        Assert.Equal(["MissingX"], result.Items[1].DependencyNames);
        Assert.Equal(["Alpha"], result.Items[2].DependencyNames);
        Assert.Empty(result.Items[0].MissingDependencyNames);
        Assert.Equal(["MissingX"], result.Items[1].MissingDependencyNames);
        Assert.Equal(["Alpha"], result.Items[2].MissingDependencyNames);
        Assert.Empty(result.Items[0].DependencyResolutionIssueNames);
        Assert.Equal(["MissingX"], result.Items[1].DependencyResolutionIssueNames);
        Assert.Equal(["MissingX"], result.Items[2].DependencyResolutionIssueNames);
        Assert.Equal(
            [
                EntityWorkflowState.DevelopmentCompleted,
                EntityWorkflowState.Blocked,
                EntityWorkflowState.Blocked
            ],
            result.Items.Select(static item => item.WorkflowState));
    }

    [Fact]
    public async Task GetAsync_GroupsByEffectivePriorityWithoutChangingDisplayedRank()
    {
        TrackedEntity prerequisite = Entity(1, "ZuluPrerequisite");
        TrackedEntity highestTarget = Entity(2, "AlphaTarget", requestedPriority: 1);
        TrackedEntity lowerTarget = Entity(3, "LowerTarget", requestedPriority: 2);
        TrackedEntity unprioritized = Entity(4, "Unprioritized");
        EntityOverviewService service = CreateService(
            [unprioritized, lowerTarget, highestTarget, prerequisite],
            [Dependency(highestTarget, prerequisite)]);

        EntityOverviewResult result = await service.GetAsync();

        Assert.Equal(
            ["ZuluPrerequisite", "AlphaTarget", "LowerTarget", "Unprioritized"],
            result.Items.Select(static item => item.SourceName));
        Assert.Equal([1, 1, 2, null], result.Items.Select(static item => item.EffectivePriority));
        Assert.Equal([1, 2, 3, 4], result.Items.Select(static item => item.Rank));
    }

    [Fact]
    public async Task GetAsync_OrdersByEffectiveDueDateBeforePriorityAndKeepsPrerequisitesFirst()
    {
        // The urgent entity has the highest priority but no due date; the dated target comes first,
        // after the prerequisite it inherits its due date to.
        TrackedEntity prerequisite = Entity(1, "ZuluPrerequisite");
        TrackedEntity datedTarget = Entity(2, "DatedTarget", requestedPriority: 3,
            dueDate: new DateOnly(2026, 11, 1));
        TrackedEntity earlierDated = Entity(3, "EarlierDated", dueDate: new DateOnly(2026, 10, 30));
        TrackedEntity urgent = Entity(4, "Urgent", requestedPriority: 1);
        EntityOverviewService service = CreateService(
            [urgent, datedTarget, earlierDated, prerequisite],
            [Dependency(datedTarget, prerequisite)]);

        EntityOverviewResult result = await service.GetAsync();

        Assert.Equal(
            ["EarlierDated", "ZuluPrerequisite", "DatedTarget", "Urgent"],
            result.Items.Select(static item => item.SourceName));
        Assert.Equal(
            [new DateOnly(2026, 10, 30), new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1), (DateOnly?)null],
            result.Items.Select(static item => item.EffectiveDueDate));
        Assert.Equal(
            [new DateOnly(2026, 10, 30), null, new DateOnly(2026, 11, 1), (DateOnly?)null],
            result.Items.Select(static item => item.DueDate));
    }

    [Fact]
    public async Task GetAsync_PrerequisiteKeepsItsOwnEarlierDueDate()
    {
        TrackedEntity prerequisite = Entity(1, "Prerequisite", dueDate: new DateOnly(2026, 10, 1));
        TrackedEntity target = Entity(2, "Target", dueDate: new DateOnly(2026, 12, 1));
        EntityOverviewService service = CreateService(
            [target, prerequisite],
            [Dependency(target, prerequisite)]);

        EntityOverviewResult result = await service.GetAsync();

        Assert.Equal(
            [new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 1)],
            result.Items.Select(static item => item.EffectiveDueDate));
    }

    [Fact]
    public async Task GetAsync_UnrankedPrioritySegmentKeepsKnownDependencyBeforeDependent()
    {
        TrackedEntity prerequisite = Entity(1, "ZuluPrerequisite");
        TrackedEntity target = Entity(2, "AlphaTarget", requestedPriority: 1);
        EntityOverviewService service = CreateService(
            [target, prerequisite],
            [Dependency(target, prerequisite)],
            [Unresolved(prerequisite, "Missing")]);

        EntityOverviewResult result = await service.GetAsync();

        Assert.Equal(
            ["ZuluPrerequisite", "AlphaTarget"],
            result.Items.Select(static item => item.SourceName));
        Assert.All(result.Items, static item => Assert.Equal(1, item.EffectivePriority));
        Assert.All(result.Items, static item => Assert.Null(item.Rank));
    }

    [Fact]
    public async Task GetAsync_UsesInjectedRankingService()
    {
        TrackedEntity dependency = Entity(1, "ZuluDependency");
        TrackedEntity owner = Entity(2, "AlphaOwner");
        RecordingRankingService rankingService = new();
        EntityOverviewService service = new(
            new StubEntityRepository([dependency, owner]),
            new StubEntityAuditReader([dependency, owner]),
            new StubDependencyRepository(
                TestDependencies.Named([dependency, owner], [Dependency(owner, dependency)])),
            rankingService,
            new EffectiveDependencyResolver(),
            new WorkflowReadinessEvaluator(),
            new PriorityPlanningService());

        EntityOverviewResult result = await service.GetAsync();

        Assert.Equal(2, rankingService.CallCount);
        Assert.Equal(["AlphaOwner", "ZuluDependency"], result.Items.Select(
            static item => item.SourceName));
    }

    private static EntityOverviewService CreateService(
        IEnumerable<TrackedEntity> entities,
        IEnumerable<PersistedDependency> dependencies,
        IEnumerable<PersistedUnresolvedDependency>? unresolvedDependencies = null)
    {
        return new EntityOverviewService(
            new StubEntityRepository(TestDependencies.WithPlaceholders(entities, unresolvedDependencies)),
            new StubEntityAuditReader(entities.ToArray()),
            new StubDependencyRepository(
                TestDependencies.Named(entities, dependencies, unresolvedDependencies)),
            new DependencyRanker(),
            new EffectiveDependencyResolver(),
            new WorkflowReadinessEvaluator(),
            new PriorityPlanningService());
    }

    private static TrackedEntity Entity(
        int id,
        string name,
        DevelopmentStatus status = DevelopmentStatus.NotStarted,
        string notes = "",
        int? requestedPriority = null,
        string? responsibleDeveloper = null,
        string? groupName = null,
        DateOnly? dueDate = null)
    {
        return new TrackedEntity(
            new EntityId(new Guid(id, 0, 0, new byte[8])),
            TestTrackerId,
            name,
            status,
            notes,
            requestedPriority: requestedPriority,
            responsibleDeveloper: responsibleDeveloper,
            groupName: groupName,
            dueDate: dueDate);
    }

    private static PersistedDependency Dependency(
        TrackedEntity dependent,
        TrackedEntity dependency)
    {
        return new PersistedDependency(new DependencyEdge(dependent.Id, dependency.Id));
    }

    private static PersistedUnresolvedDependency Unresolved(
        TrackedEntity dependent,
        string dependencySourceName)
    {
        return new PersistedUnresolvedDependency(
            new UnresolvedDependency(dependent.Id, dependencySourceName));
    }

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

    private sealed class StubEntityAuditReader(IReadOnlyList<TrackedEntity> entities)
        : IEntityAuditReader
    {
        private static readonly DateTimeOffset Timestamp =
            new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        public Task<IReadOnlyList<EntityAuditTimestamps>> GetAllAsync(
            TrackerId trackerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EntityAuditTimestamps>>(entities
                .Select(static entity => new EntityAuditTimestamps(
                    entity.Id,
                    Timestamp,
                    Timestamp,
                    Timestamp))
                .ToArray());
    }

    private sealed class RecordingRankingService : IDependencyRankingService
    {
        private readonly DependencyRanker _ranker = new();

        public int CallCount { get; private set; }

        public DependencyRankingResult Rank(
            IEnumerable<TrackedEntity> entities,
            IEnumerable<DependencyEdge> dependencyEdges,
            IEnumerable<UnresolvedDependency> unresolvedDependencies)
        {
            CallCount++;
            return _ranker.Rank(entities, [], unresolvedDependencies);
        }
    }
}

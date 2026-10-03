using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Importing;
using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Planning;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.Synchronization;
using EntityTracker.Domain;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class EntityDependencyEditorViewModelTests
{
    [Fact]
    public async Task FilterActive_IsEditableAndCancelDiscardsTheDraft()
    {
        TrackedEntity owner = Entity(1, "Owner");
        owner.ChangeFilterActive("Original instructions");
        EntityDependencyEditorViewModel viewModel = ViewModel([owner]);

        await viewModel.BeginStandaloneAsync(owner.Id);
        Assert.Equal("Original instructions", viewModel.EditedFilterActive);
        viewModel.EditedFilterActive = "Active only\nBy region";
        Assert.True(viewModel.IsDirty);

        viewModel.CancelCommand.Execute(null);
        Assert.Equal("Original instructions", owner.FilterActive);
    }

    [Fact]
    public async Task SelectingExistingSuggestion_AddsDependencyAndClearsSearch()
    {
        TrackedEntity owner = Entity(1, "Owner");
        TrackedEntity target = Entity(2, "Target");
        EntityDependencyEditorViewModel viewModel = ViewModel([owner, target]);

        await viewModel.BeginStandaloneAsync(owner.Id);
        viewModel.DependencyQuery = "target";
        await WaitUntilAsync(() => viewModel.Suggestions.Count == 1);

        ManualDependencySuggestion suggestion = viewModel.Suggestions[0];
        viewModel.SelectedDependencySuggestion = suggestion;
        viewModel.SelectedDependencySuggestion = suggestion;

        await WaitUntilAsync(() => viewModel.Dependencies.Count == 1);
        EntityDependencyEditRow dependency = Assert.Single(viewModel.Dependencies);
        Assert.Equal(target.SourceName, dependency.SourceName);
        Assert.Equal("Manual addition", dependency.Origin);
        Assert.Equal(string.Empty, viewModel.DependencyQuery);
        Assert.False(viewModel.IsDependencySuggestionsOpen);
        Assert.Empty(viewModel.Errors);
    }

    [Fact]
    public async Task Search_HidesCurrentDependencyUntilItIsRemoved()
    {
        TrackedEntity owner = Entity(1, "Owner");
        TrackedEntity target = Entity(2, "Target");
        EntityDependencyEditorViewModel viewModel = ViewModel([owner, target]);
        await viewModel.BeginStandaloneAsync(owner.Id);
        viewModel.DependencyQuery = "target";
        await WaitUntilAsync(() => viewModel.Suggestions.Count == 1);
        viewModel.SelectedDependencySuggestion = viewModel.Suggestions[0];
        await WaitUntilAsync(() => viewModel.Dependencies.Count == 1);

        viewModel.DependencyQuery = "target";
        await WaitUntilAsync(() => viewModel.SearchMessage?.Contains("already",
            StringComparison.OrdinalIgnoreCase) == true);
        Assert.Empty(viewModel.Suggestions);
        Assert.False(viewModel.CanAddAsUnresolved);

        viewModel.RemoveManualCommand.Execute(viewModel.Dependencies[0]);
        await WaitUntilAsync(() => viewModel.Dependencies.Count == 0);
        viewModel.DependencyQuery = "target";
        await WaitUntilAsync(() => viewModel.Suggestions.Count == 1);
        Assert.Equal(target.Id, viewModel.Suggestions[0].EntityId);
    }

    private static EntityDependencyEditorViewModel ViewModel(
        IReadOnlyList<TrackedEntity> entities)
    {
        StubEntityRepository entityRepository = new(entities);
        StubDependencyRepository dependencyRepository = new();
        StubManualDependencyOverrideRepository overrideRepository = new();
        StubStore store = new();
        EntityDependencyEditorService editorService = new(
            entityRepository,
            dependencyRepository,
            overrideRepository,
            new EffectiveDependencyResolver(),
            new DependencyRanker(),
            store,
            new PriorityPlanningService());
        return new EntityDependencyEditorViewModel(
            TestTrackerId,
            editorService,
            new EntityLifecycleService(
                entityRepository,
                dependencyRepository,
                overrideRepository,
                store,
                new EffectiveDependencyResolver(),
                new DependencyRanker()),
            new SchemaSynchronizationService(
                new StubSchemaImportFileParser(),
                entityRepository,
                dependencyRepository,
                overrideRepository,
                new SchemaSynchronizationPlanner(
                    new DependencyRanker(),
                    new EffectiveDependencyResolver()),
                editorService,
                new StubSynchronizationStore()),
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            _ => { });
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static TrackedEntity Entity(int id, string name) => new(
        new EntityId(new Guid(id, 0, 0, new byte[8])),
        TestTrackerId,
        name);

    private sealed class StubEntityRepository(IReadOnlyList<TrackedEntity> entities) : IEntityRepository
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

    private sealed class StubDependencyRepository : IDependencyRepository
    {
        public Task<IReadOnlyList<PersistedDependency>> GetAllAsync(
            TrackerId trackerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PersistedDependency>>([]);

        public Task<IReadOnlyList<PersistedUnresolvedDependency>> GetAllUnresolvedAsync(
            TrackerId trackerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PersistedUnresolvedDependency>>([]);
    }

    private sealed class StubManualDependencyOverrideRepository : IManualDependencyOverrideRepository
    {
        public Task<IReadOnlyList<ManualDependencyOverride>> GetAllAsync(
            TrackerId trackerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ManualDependencyOverride>>([]);
    }

    private sealed class StubStore : ITrackedStateStore
    {
        public Task ApplyAsync(
            TrackerId trackerId,
            TrackedStateChangeSet changeSet,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task EnsureHistoryBaselineAsync(
            TrackerId trackerId,
            IEnumerable<TrackedEntity> entities,
            ProgressSnapshotState snapshot,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubSynchronizationStore : ISchemaSynchronizationStore
    {
        public Task<SchemaImportSummary> ApplyAsync(
            TrackerId trackerId,
            TrackedStateChangeSet changeSet,
            SchemaImportCompletion completion,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SchemaImportSummary(DateTimeOffset.UtcNow, completion));

        public Task<SchemaImportSummary?> GetLatestImportAsync(
            TrackerId trackerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SchemaImportSummary?>(null);
    }

    private sealed class StubSchemaImportFileParser : ISchemaImportFileParser
    {
        public Task<SchemaImportResult> ParseAsync(
            string filePath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SchemaImportResult.Failure([]));
    }
}

using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Importing;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Ranking;
using EntityTracker.Domain;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class ManualEntityCreationViewModelTests
{
    [Fact]
    public async Task Search_AllowsExplicitUnresolvedSelectionAndRemoval()
    {
        ManualEntityCreationViewModel viewModel = ViewModel(
            [],
            out RecordingStore store,
            out _,
            out _);
        viewModel.EntityName = "Owner";
        viewModel.DependencyQuery = "Future";

        await viewModel.SearchDependenciesAsync();
        viewModel.AddUnresolvedCommand.Execute(null);

        ManualDependencyRow selected = Assert.Single(viewModel.SelectedDependencies);
        Assert.True(selected.IsUnresolved);
        Assert.Equal("⚠ Missing", selected.ResolutionLabel);
        Assert.True(viewModel.HasWarnings);

        viewModel.RemoveDependencyCommand.Execute(selected);

        Assert.Empty(viewModel.SelectedDependencies);
        Assert.False(viewModel.HasWarnings);
        Assert.Null(store.LastChangeSet);
    }

    [Fact]
    public async Task Search_ExistingActiveEntityCanBeSelected()
    {
        TrackedEntity existing = Entity(1, "ExistingTable");
        ManualEntityCreationViewModel viewModel = ViewModel(
            [existing],
            out _,
            out _,
            out _);
        viewModel.EntityName = "Owner";
        viewModel.DependencyQuery = "existing";

        await viewModel.SearchDependenciesAsync();
        ManualDependencySuggestion suggestion = Assert.Single(viewModel.Suggestions);
        viewModel.AddExistingCommand.Execute(suggestion);

        ManualDependencyRow selected = Assert.Single(viewModel.SelectedDependencies);
        Assert.False(selected.IsUnresolved);
        Assert.Equal(existing.Id, selected.Selection.EntityId);
        Assert.Equal("Resolved", selected.ResolutionLabel);
    }

    [Fact]
    public async Task Search_ArchivedExactMatchShowsMessageAndCannotBeAddedUnresolved()
    {
        ManualEntityCreationViewModel viewModel = ViewModel(
            [Entity(1, "Legacy", EntityLifecycleState.Archived)],
            out _,
            out _,
            out _);
        viewModel.EntityName = "Owner";
        viewModel.DependencyQuery = "legacy";

        await viewModel.SearchDependenciesAsync();

        Assert.False(viewModel.CanAddAsUnresolved);
        Assert.Contains("archived", viewModel.SearchMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(viewModel.Suggestions);
    }

    [Fact]
    public async Task Create_SuccessResetsDraftAndInvokesRefreshCallback()
    {
        ManualEntityCreationViewModel viewModel = ViewModel(
            [],
            out RecordingStore store,
            out CallbackCounter created,
            out _);
        viewModel.EntityName = "NewEntity";

        await viewModel.CreateAsync();

        Assert.Single(store.LastChangeSet!.EntitiesToAdd);
        Assert.Equal(1, created.Count);
        Assert.Equal(string.Empty, viewModel.EntityName);
        Assert.Empty(viewModel.SelectedDependencies);
        Assert.False(viewModel.HasErrors);
    }

    [Fact]
    public void Cancel_ResetsDraftWithoutPersisting()
    {
        ManualEntityCreationViewModel viewModel = ViewModel(
            [],
            out RecordingStore store,
            out _,
            out CallbackCounter cancelled);
        viewModel.EntityName = "Discarded";

        viewModel.CancelCommand.Execute(null);

        Assert.Equal(1, cancelled.Count);
        Assert.Equal(string.Empty, viewModel.EntityName);
        Assert.Null(store.LastChangeSet);
    }

    [Fact]
    public async Task ArchivedDuplicate_OffersExistingEntityDetailsInsteadOfCreating()
    {
        TrackedEntity archived = Entity(1, "Legacy", EntityLifecycleState.Archived);
        List<EntityId> restoreRequests = [];
        ManualEntityCreationViewModel viewModel = ViewModel(
            [archived],
            out RecordingStore store,
            out _,
            out _,
            restoreRequests);
        viewModel.EntityName = " legacy ";

        await viewModel.CreateAsync();

        Assert.True(viewModel.HasArchivedEntityMatch);
        Assert.Equal(archived.Id, viewModel.ArchivedEntityMatch?.EntityId);
        Assert.Null(store.LastChangeSet);

        viewModel.RestoreArchivedCommand.Execute(null);
        await WaitUntilAsync(() => restoreRequests.Count == 1);
        Assert.Equal(archived.Id, Assert.Single(restoreRequests));
    }

    private static ManualEntityCreationViewModel ViewModel(
        IReadOnlyList<TrackedEntity> entities,
        out RecordingStore store,
        out CallbackCounter created,
        out CallbackCounter cancelled,
        ICollection<EntityId>? restoreRequests = null)
    {
        store = new RecordingStore();
        created = new CallbackCounter();
        cancelled = new CallbackCounter();
        CallbackCounter createdCounter = created;
        CallbackCounter cancelledCounter = cancelled;
        ManualEntityCreationService service = new(
            new StubEntityRepository(entities),
            new StubDependencyRepository(),
            new StubManualDependencyOverrideRepository(),
            new DependencyRanker(),
            new EffectiveDependencyResolver(),
            store);
        return new ManualEntityCreationViewModel(
            service,
            () =>
            {
                createdCounter.Count++;
                return Task.CompletedTask;
            },
            id =>
            {
                restoreRequests?.Add(id);
                return Task.CompletedTask;
            },
            () => cancelledCounter.Count++);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static TrackedEntity Entity(
        int id,
        string name,
        EntityLifecycleState lifecycle = EntityLifecycleState.Active) =>
        new(
            new EntityId(new Guid(id, 0, 0, new byte[8])),
            name,
            lifecycleState: lifecycle);

    private sealed class CallbackCounter
    {
        public int Count { get; set; }
    }

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

    private sealed class StubDependencyRepository : IDependencyRepository
    {
        public Task<IReadOnlyList<PersistedDependency>> GetAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PersistedDependency>>([]);

        public Task<IReadOnlyList<PersistedUnresolvedDependency>> GetAllUnresolvedAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PersistedUnresolvedDependency>>([]);

    }

    private sealed class RecordingStore : ITrackedStateStore
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

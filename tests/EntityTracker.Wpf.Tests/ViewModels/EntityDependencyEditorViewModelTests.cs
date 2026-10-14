using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Planning;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Lifecycle;
using EntityTracker.Domain;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class EntityDependencyEditorViewModelTests
{
    [Fact]
    public async Task EmptyDropdownRefresh_ExcludesOwnerAndCurrentDependencies()
    {
        TrackedEntity owner = Entity(1, "Owner");
        TrackedEntity first = Entity(2, "TargetOne");
        TrackedEntity second = Entity(3, "TargetTwo");
        EntityDependencyEditorViewModel viewModel = ViewModel([owner, first, second]);
        await viewModel.BeginStandaloneAsync(owner.Id);
        Assert.False(viewModel.IsDependencySuggestionsOpen);

        viewModel.IsDependencySuggestionsOpen = true;
        viewModel.RefreshDependencySuggestionsCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.Suggestions.Count == 2);
        Assert.True(viewModel.IsDependencySuggestionsOpen);
        viewModel.AddExistingCommand.Execute(viewModel.Suggestions[0]);
        await WaitUntilAsync(() => viewModel.Dependencies.Count == 1);
        Assert.False(viewModel.IsDependencySuggestionsOpen);

        viewModel.IsDependencySuggestionsOpen = true;
        viewModel.RefreshDependencySuggestionsCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.Suggestions.Count == 1);
        Assert.Equal(second.Id, viewModel.Suggestions[0].EntityId);
        Assert.False(viewModel.CanAddAsUnresolved);
        Assert.Equal(string.Empty, viewModel.DependencyQuery);
    }

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

        viewModel.AddExistingCommand.Execute(viewModel.Suggestions[0]);

        await WaitUntilAsync(() => viewModel.Dependencies.Count == 1);
        EntityDependencyEditRow dependency = Assert.Single(viewModel.Dependencies);
        Assert.Equal(target.SourceName, dependency.SourceName);
        Assert.Equal("Resolved", dependency.Resolution);
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
        viewModel.AddExistingCommand.Execute(viewModel.Suggestions[0]);
        await WaitUntilAsync(() => viewModel.Dependencies.Count == 1);

        viewModel.DependencyQuery = "target";
        await WaitUntilAsync(() => viewModel.SearchMessage?.Contains("already",
            StringComparison.OrdinalIgnoreCase) == true);
        Assert.Empty(viewModel.Suggestions);
        Assert.False(viewModel.CanAddAsUnresolved);

        viewModel.RemoveDependencyCommand.Execute(viewModel.Dependencies[0]);
        await WaitUntilAsync(() => viewModel.Dependencies.Count == 0);
        viewModel.DependencyQuery = "target";
        await WaitUntilAsync(() => viewModel.Suggestions.Count == 1);
        Assert.Equal(target.Id, viewModel.Suggestions[0].EntityId);
    }

    [Fact]
    public async Task Name_StartsAsTheEntityNameAndARenameMakesTheFormDirty()
    {
        TrackedEntity owner = Entity(1, "custmer");
        EntityDependencyEditorViewModel viewModel = ViewModel([owner]);
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        await viewModel.BeginStandaloneAsync(owner.Id);

        // The Name box's enabled state is bound, so it must hear when editing becomes possible.
        Assert.Contains(nameof(viewModel.CanEditName), changed);

        Assert.Equal("custmer", viewModel.EditedName);
        Assert.True(viewModel.CanEditName);
        Assert.False(viewModel.IsDirty);

        viewModel.EditedName = "customer";

        Assert.True(viewModel.IsDirty);
        Assert.False(viewModel.HasNameError);
        Assert.False(viewModel.HasRenameNotice);
        Assert.True(viewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Name_ThatIsEmptyOrAlreadyUsedCannotBeSaved()
    {
        TrackedEntity owner = Entity(1, "Owner");
        TrackedEntity other = Entity(2, "invoice");
        EntityDependencyEditorViewModel viewModel = ViewModel([owner, other]);
        await viewModel.BeginStandaloneAsync(owner.Id);

        viewModel.EditedName = "   ";
        Assert.Equal("An entity needs a name.", viewModel.NameError);
        Assert.False(viewModel.SaveCommand.CanExecute(null));

        viewModel.EditedName = "INVOICE";
        await WaitUntilAsync(() => viewModel.HasNameError);
        Assert.Equal("Another entity in this Tracker is already called invoice.", viewModel.NameError);
        Assert.False(viewModel.SaveCommand.CanExecute(null));

        viewModel.EditedName = "invoice_line";
        Assert.False(viewModel.HasNameError);
        Assert.True(viewModel.SaveCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Name_WarnsInACopiedTrackerThatTheSourceStillUsesTheOldName(bool trackerIsCopy)
    {
        TrackedEntity owner = Entity(1, "custmer");
        EntityDependencyEditorViewModel viewModel = ViewModel([owner], trackerIsCopy);
        await viewModel.BeginStandaloneAsync(owner.Id);

        viewModel.EditedName = "Custmer";
        Assert.False(viewModel.HasRenameNotice);

        // Sync from source matches entities by name, whether an entity was copied or added in the copy.
        viewModel.EditedName = "customer";
        Assert.Equal(trackerIsCopy, viewModel.HasRenameNotice);
        if (trackerIsCopy)
            Assert.Contains("copied from another Tracker", viewModel.RenameNotice, StringComparison.Ordinal);
        Assert.True(viewModel.SaveCommand.CanExecute(null));
    }

    private static EntityDependencyEditorViewModel ViewModel(
        IReadOnlyList<TrackedEntity> entities,
        bool trackerIsCopy = false)
    {
        StubEntityRepository entityRepository = new(entities);
        StubDependencyRepository dependencyRepository = new();
        StubStore store = new();
        EntityDependencyEditorService editorService = new(
            entityRepository,
            dependencyRepository,
            new EffectiveDependencyResolver(),
            new DependencyRanker(),
            store,
            new PriorityPlanningService(),
            trackerRepository: new StubTrackerRepository(trackerIsCopy));
        return new EntityDependencyEditorViewModel(
            TestTrackerId,
            editorService,
            new EntityLifecycleService(
                entityRepository,
                dependencyRepository,
                store,
                new EffectiveDependencyResolver(),
                new DependencyRanker()),
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask);
    }

    private static Task WaitUntilAsync(Func<bool> condition,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(condition))] string description = "") =>
        TestWait.UntilAsync(condition, description);

    private static TrackedEntity Entity(int id, string name) => new(
        new EntityId(new Guid(id, 0, 0, new byte[8])),
        TestTrackerId,
        name);

    private sealed class StubTrackerRepository(bool isCopy) : ITrackerRepository
    {
        private Tracker Tracker => new(TestTrackerId, new ProjectId(Guid.Parse("10000000-0000-0000-0000-000000000001")), "Tracker",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            copiedFromTrackerId: isCopy ? new TrackerId(Guid.NewGuid()) : null);

        public Task<Tracker?> GetAsync(TrackerId trackerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Tracker?>(trackerId == TestTrackerId ? Tracker : null);

        public Task<IReadOnlyList<Tracker>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Tracker>>([Tracker]);

        public Task<IReadOnlyList<Tracker>> GetByProjectAsync(
            ProjectId projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Tracker>>([Tracker]);

        public Task<bool> IsNameReservedAsync(ProjectId projectId, string name,
            TrackerId? excludingTrackerId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

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
}

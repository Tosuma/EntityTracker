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
        viewModel.AddExistingCommand.Execute(viewModel.Suggestions[0]);
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

    [Fact]
    public async Task Name_StartsAsTheEntityNameAndARenameMakesTheFormDirty()
    {
        TrackedEntity owner = Entity(1, "custmer", EntityProvenance.ManualOnly);
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
    [InlineData(EntityProvenance.Imported, "CSV import")]
    [InlineData(EntityProvenance.ManualAndImported, "CSV import")]
    [InlineData(EntityProvenance.Copied, "copied from another Tracker")]
    public async Task Name_WarnsWhenTheOldNameMayComeBack(EntityProvenance provenance, string reason)
    {
        TrackedEntity owner = Entity(1, "custmer", provenance);
        EntityDependencyEditorViewModel viewModel = ViewModel([owner]);
        await viewModel.BeginStandaloneAsync(owner.Id);

        viewModel.EditedName = "Custmer";
        Assert.False(viewModel.HasRenameNotice);

        viewModel.EditedName = "customer";
        Assert.True(viewModel.HasRenameNotice);
        Assert.Contains(reason, viewModel.RenameNotice, StringComparison.Ordinal);
        Assert.True(viewModel.SaveCommand.CanExecute(null));
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

    private static TrackedEntity Entity(
        int id,
        string name,
        EntityProvenance provenance = EntityProvenance.Imported) => new(
        new EntityId(new Guid(id, 0, 0, new byte[8])),
        TestTrackerId,
        name,
        provenance: provenance);

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

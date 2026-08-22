using EntityTracker.Application.Importing;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Ranking;
using EntityTracker.Domain;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task InitializeAsync_PopulatesRankedRowsAndProgressSummary()
    {
        TrackedEntity foundation = Entity(1, "Foundation", DevelopmentStatus.Completed, "Ready");
        TrackedEntity service = Entity(2, "Service", DevelopmentStatus.InProgress);
        TrackedEntity screen = Entity(3, "Screen");
        MainWindowViewModel viewModel = CreateViewModel(
            [screen, foundation, service],
            [
                Dependency(service, foundation, ImportedDependencyKind.Optional),
                Dependency(screen, service, ImportedDependencyKind.Mandatory)
            ]);

        await viewModel.InitializeAsync();

        Assert.Equal(
            ["Foundation", "Service", "Screen"],
            viewModel.OverviewItems.Select(static item => item.SourceName));
        Assert.Equal(["1", "2", "3"], viewModel.OverviewItems.Select(static item => item.Rank));
        Assert.Equal([0, 1, 1], viewModel.OverviewItems.Select(static item => item.DependencyCount));
        Assert.Equal("Completed", viewModel.OverviewItems[0].Status);
        Assert.Equal("Ready", viewModel.OverviewItems[0].Notes);
        Assert.Equal(3, viewModel.TotalEntityCount);
        Assert.Equal(1, viewModel.NotStartedCount);
        Assert.Equal(1, viewModel.InProgressCount);
        Assert.Equal(1, viewModel.CompletedCount);
        Assert.Equal(100.0 / 3.0, viewModel.CompletionPercentage, 5);
        Assert.False(viewModel.HasOverviewError);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task InitializeAsync_EmptyPersistence_ShowsEmptyState()
    {
        MainWindowViewModel viewModel = CreateViewModel([], []);

        await viewModel.InitializeAsync();

        Assert.Empty(viewModel.OverviewItems);
        Assert.True(viewModel.ShowOverviewEmptyState);
        Assert.Equal(0, viewModel.CompletionPercentage);
    }

    [Fact]
    public async Task InitializeAsync_UnresolvedAndTransitivelyBlockedEntities_AreVisibleWithoutRanks()
    {
        TrackedEntity alpha = Entity(1, "Alpha");
        TrackedEntity beta = Entity(2, "Beta");
        TrackedEntity gamma = Entity(3, "Gamma");
        MainWindowViewModel viewModel = CreateViewModel(
            [alpha, beta, gamma],
            [Dependency(beta, alpha, ImportedDependencyKind.Mandatory)],
            [Unresolved(alpha, "MissingX", ImportedDependencyKind.Optional)]);

        await viewModel.InitializeAsync();

        Assert.Equal(
            ["Gamma", "Alpha", "Beta"],
            viewModel.OverviewItems.Select(static item => item.SourceName));
        Assert.Equal(["1", "—", "—"], viewModel.OverviewItems.Select(static item => item.Rank));
        Assert.Equal(
            ["Resolved", "Unresolved", "Blocked"],
            viewModel.OverviewItems.Select(static item => item.DependencyState));
        Assert.Equal(
            ["—", "MissingX", "MissingX"],
            viewModel.OverviewItems.Select(static item => item.MissingDependencies));
        Assert.Equal([0, 1, 1], viewModel.OverviewItems.Select(static item => item.DependencyCount));
        Assert.False(viewModel.HasOverviewError);
    }

    [Fact]
    public async Task InitializeAsync_InvalidPersistedGraph_ShowsRankingErrorWithoutRows()
    {
        TrackedEntity alpha = Entity(1, "Alpha");
        TrackedEntity beta = Entity(2, "Beta");
        MainWindowViewModel viewModel = CreateViewModel(
            [alpha, beta],
            [
                Dependency(alpha, beta, ImportedDependencyKind.Mandatory),
                Dependency(beta, alpha, ImportedDependencyKind.Mandatory)
            ]);

        await viewModel.InitializeAsync();

        Assert.Empty(viewModel.OverviewItems);
        Assert.True(viewModel.HasOverviewError);
        Assert.Contains("cycle", viewModel.OverviewErrorMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.ShowOverviewEmptyState);
    }

    [Fact]
    public async Task RefreshAsync_RepositoryFailure_ShowsLoadErrorAndClearsRows()
    {
        StubEntityRepository entityRepository = new(
            (_, _) => Task.FromException<IReadOnlyList<TrackedEntity>>(
                new InvalidOperationException("Database unavailable.")));
        MainWindowViewModel viewModel = CreateViewModel(
            entityRepository,
            new StubDependencyRepository([]),
            new StubFileParser(FailureResult()),
            new StubFilePicker());

        await viewModel.RefreshAsync();

        Assert.Empty(viewModel.OverviewItems);
        Assert.True(viewModel.HasOverviewError);
        Assert.Contains("Database unavailable", viewModel.OverviewErrorMessage);
    }

    [Fact]
    public async Task ImportCsvAsync_ValidSelection_PopulatesPreviewAndSelectsTab()
    {
        SchemaImportResult importResult = SchemaImportResult.Success(Candidate(
            ["Screen", "Foundation", "Service"],
            [
                ("Service", "Foundation", ImportedDependencyKind.Optional),
                ("Screen", "Service", ImportedDependencyKind.Mandatory)
            ]));
        StubFilePicker picker = new("C:\\schemas\\current.csv");
        MainWindowViewModel viewModel = CreateViewModel(
            new StubEntityRepository([]),
            new StubDependencyRepository([]),
            new StubFileParser(importResult),
            picker);

        await viewModel.ImportCsvAsync();

        Assert.Equal(1, viewModel.SelectedTabIndex);
        Assert.Equal("current.csv", viewModel.SelectedFileName);
        Assert.Equal(
            ["Foundation", "Service", "Screen"],
            viewModel.PreviewItems.Select(static item => item.SourceName));
        Assert.Equal(3, viewModel.PreviewEntityCount);
        Assert.Equal(2, viewModel.PreviewDependencyCount);
        Assert.Empty(viewModel.PreviewDiagnostics);
    }

    [Fact]
    public async Task ImportCsvAsync_CancelledPicker_PreservesExistingPreview()
    {
        StubFilePicker picker = new("C:\\schemas\\first.csv", null);
        MainWindowViewModel viewModel = CreateViewModel(
            new StubEntityRepository([]),
            new StubDependencyRepository([]),
            new StubFileParser(SchemaImportResult.Success(Candidate(["Alpha"], []))),
            picker);
        await viewModel.ImportCsvAsync();
        IReadOnlyList<ImportPreviewRow> originalItems = viewModel.PreviewItems;

        await viewModel.ImportCsvAsync();

        Assert.Same(originalItems, viewModel.PreviewItems);
        Assert.Equal("first.csv", viewModel.SelectedFileName);
        Assert.Equal(1, viewModel.SelectedTabIndex);
    }

    [Fact]
    public async Task ImportCsvAsync_ValidationFailure_FormatsLocationAndShowsNoRows()
    {
        ImportDiagnostic diagnostic = new(
            ImportDiagnosticCode.CountMismatch,
            "Declared count does not match.",
            4,
            "mandatory_dependency_count");
        MainWindowViewModel viewModel = CreateViewModel(
            new StubEntityRepository([]),
            new StubDependencyRepository([]),
            new StubFileParser(SchemaImportResult.Failure([diagnostic])),
            new StubFilePicker("invalid.csv"));

        await viewModel.ImportCsvAsync();

        Assert.Empty(viewModel.PreviewItems);
        Assert.Equal(
            "Row 4, mandatory_dependency_count: Declared count does not match.",
            Assert.Single(viewModel.PreviewDiagnostics));
        Assert.True(viewModel.HasPreviewDiagnostics);
    }

    [Fact]
    public async Task ImportCsvAsync_UnresolvedWarning_ShowsRecognizedAndBlockedEntities()
    {
        SchemaImportCandidate candidate = Candidate(
            ["Alpha", "Beta", "Gamma"],
            [("Beta", "Alpha", ImportedDependencyKind.Mandatory)],
            [("Alpha", "MissingX", ImportedDependencyKind.Optional)]);
        ImportDiagnostic warning = new(
            ImportDiagnosticCode.UnknownDependency,
            "Dependency 'MissingX' remains unresolved.",
            2,
            "optional_dependencies",
            ImportDiagnosticSeverity.Warning);
        MainWindowViewModel viewModel = CreateViewModel(
            new StubEntityRepository([]),
            new StubDependencyRepository([]),
            new StubFileParser(SchemaImportResult.Success(candidate, [warning])),
            new StubFilePicker("unresolved.csv"));

        await viewModel.ImportCsvAsync();

        Assert.Equal(
            ["Gamma", "Alpha", "Beta"],
            viewModel.PreviewItems.Select(static item => item.SourceName));
        Assert.Equal(["1", "—", "—"], viewModel.PreviewItems.Select(static item => item.Rank));
        Assert.Equal(
            ["Resolved", "Unresolved", "Blocked"],
            viewModel.PreviewItems.Select(static item => item.DependencyState));
        Assert.Equal(
            ["—", "MissingX", "MissingX"],
            viewModel.PreviewItems.Select(static item => item.MissingDependencies));
        Assert.Equal(
            "Row 2, optional_dependencies: Dependency 'MissingX' remains unresolved.",
            Assert.Single(viewModel.PreviewWarnings));
        Assert.True(viewModel.HasPreviewWarnings);
        Assert.Empty(viewModel.PreviewDiagnostics);
    }

    [Fact]
    public async Task ImportCsvAsync_Cycle_ShowsCycleDiagnosticWithoutRows()
    {
        SchemaImportCandidate candidate = Candidate(
            ["Alpha", "Beta"],
            [
                ("Alpha", "Beta", ImportedDependencyKind.Mandatory),
                ("Beta", "Alpha", ImportedDependencyKind.Optional)
            ]);
        MainWindowViewModel viewModel = CreateViewModel(
            new StubEntityRepository([]),
            new StubDependencyRepository([]),
            new StubFileParser(SchemaImportResult.Success(candidate)),
            new StubFilePicker("cycle.csv"));

        await viewModel.ImportCsvAsync();

        Assert.Empty(viewModel.PreviewItems);
        Assert.Contains(viewModel.PreviewDiagnostics, static message =>
            message.Contains("cycle", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RefreshAsync_WhileLoading_DisablesBothCommandsAndRejectsReentry()
    {
        TaskCompletionSource<IReadOnlyList<TrackedEntity>> entitiesSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        int loadCount = 0;
        StubEntityRepository entityRepository = new((_, _) =>
        {
            loadCount++;
            return entitiesSource.Task;
        });
        MainWindowViewModel viewModel = CreateViewModel(
            entityRepository,
            new StubDependencyRepository([]),
            new StubFileParser(FailureResult()),
            new StubFilePicker("unused.csv"));

        Task firstRefresh = viewModel.RefreshAsync();
        Task secondRefresh = viewModel.RefreshAsync();

        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.RefreshCommand.CanExecute(null));
        Assert.False(viewModel.ImportCsvCommand.CanExecute(null));
        Assert.Equal(1, loadCount);
        await secondRefresh;

        entitiesSource.SetResult([]);
        await firstRefresh;

        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.RefreshCommand.CanExecute(null));
        Assert.True(viewModel.ImportCsvCommand.CanExecute(null));
    }

    private static MainWindowViewModel CreateViewModel(
        IEnumerable<TrackedEntity> entities,
        IEnumerable<PersistedDependency> dependencies,
        IEnumerable<PersistedUnresolvedDependency>? unresolvedDependencies = null)
    {
        return CreateViewModel(
            new StubEntityRepository(entities.ToArray()),
            new StubDependencyRepository(
                dependencies.ToArray(),
                unresolvedDependencies?.ToArray()),
            new StubFileParser(FailureResult()),
            new StubFilePicker());
    }

    private static MainWindowViewModel CreateViewModel(
        IEntityRepository entityRepository,
        IDependencyRepository dependencyRepository,
        ISchemaImportFileParser fileParser,
        ICsvFilePicker filePicker)
    {
        DependencyRanker ranker = new();
        return new MainWindowViewModel(
            new EntityOverviewService(entityRepository, dependencyRepository, ranker),
            new SchemaImportPreviewService(fileParser, ranker),
            filePicker);
    }

    private static SchemaImportResult FailureResult()
    {
        return SchemaImportResult.Failure(
        [
            new ImportDiagnostic(ImportDiagnosticCode.FileAccessError, "No file configured.")
        ]);
    }

    private static TrackedEntity Entity(
        int id,
        string name,
        DevelopmentStatus status = DevelopmentStatus.NotStarted,
        string notes = "")
    {
        return new TrackedEntity(
            new EntityId(new Guid(id, 0, 0, new byte[8])),
            name,
            status,
            notes);
    }

    private static PersistedDependency Dependency(
        TrackedEntity dependent,
        TrackedEntity dependency,
        ImportedDependencyKind kind)
    {
        return new PersistedDependency(
            new DependencyEdge(dependent.Id, dependency.Id),
            kind);
    }

    private static PersistedUnresolvedDependency Unresolved(
        TrackedEntity dependent,
        string dependencySourceName,
        ImportedDependencyKind kind)
    {
        return new PersistedUnresolvedDependency(
            new UnresolvedDependency(dependent.Id, dependencySourceName),
            kind);
    }

    private static SchemaImportCandidate Candidate(
        IEnumerable<string> names,
        IEnumerable<(string Dependent, string Dependency, ImportedDependencyKind Kind)> dependencies,
        IEnumerable<(string Dependent, string Dependency, ImportedDependencyKind Kind)>?
            unresolvedDependencies = null)
    {
        ImportedEntity[] entities = names
            .Select(static name => new ImportedEntity(EntitySourceKey.From(name), name))
            .ToArray();
        ImportedDependency[] importedDependencies = dependencies
            .Select(static dependency => new ImportedDependency(
                EntitySourceKey.From(dependency.Dependent),
                EntitySourceKey.From(dependency.Dependency),
                dependency.Kind))
            .ToArray();
        UnresolvedImportedDependency[] unresolved = (unresolvedDependencies ?? [])
            .Select(static dependency => new UnresolvedImportedDependency(
                EntitySourceKey.From(dependency.Dependent),
                EntitySourceKey.From(dependency.Dependency),
                dependency.Dependency,
                dependency.Kind))
            .ToArray();
        return new SchemaImportCandidate(entities, importedDependencies, unresolved);
    }

    private sealed class StubEntityRepository : IEntityRepository
    {
        private readonly Func<int, CancellationToken, Task<IReadOnlyList<TrackedEntity>>> _getAll;

        public StubEntityRepository(IReadOnlyList<TrackedEntity> entities)
            : this((_, _) => Task.FromResult(entities))
        {
        }

        public StubEntityRepository(
            Func<int, CancellationToken, Task<IReadOnlyList<TrackedEntity>>> getAll)
        {
            _getAll = getAll;
        }

        public Task<TrackedEntity?> GetAsync(
            EntityId id,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<TrackedEntity>> GetAllAsync(
            CancellationToken cancellationToken = default) => _getAll(0, cancellationToken);

        public Task<bool> TryAddAsync(
            TrackedEntity entity,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> UpdateSchemaMetadataAsync(
            TrackedEntity entity,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> UpdateProgressAsync(
            TrackedEntity entity,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubDependencyRepository(
        IReadOnlyList<PersistedDependency> dependencies,
        IReadOnlyList<PersistedUnresolvedDependency>? unresolvedDependencies = null)
        : IDependencyRepository
    {
        public Task<IReadOnlyList<PersistedDependency>> GetAllAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(dependencies);

        public Task<IReadOnlyList<PersistedUnresolvedDependency>> GetAllUnresolvedAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(unresolvedDependencies ?? []);

        public Task SaveAsync(
            PersistedDependency dependency,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SaveUnresolvedAsync(
            PersistedUnresolvedDependency dependency,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubFileParser(SchemaImportResult result) : ISchemaImportFileParser
    {
        public Task<SchemaImportResult> ParseAsync(
            string filePath,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class StubFilePicker : ICsvFilePicker
    {
        private readonly Queue<string?> _paths;

        public StubFilePicker(params string?[] paths)
        {
            _paths = new Queue<string?>(paths);
        }

        public string? SelectCsvFile()
        {
            return _paths.Count == 0 ? null : _paths.Dequeue();
        }
    }
}

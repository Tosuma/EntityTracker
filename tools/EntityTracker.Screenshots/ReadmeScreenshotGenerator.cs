using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

using EntityTracker.Application.History;
using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;
using EntityTracker.Wpf;
using EntityTracker.Wpf.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace EntityTracker.Screenshots;

internal sealed class ReadmeScreenshotGenerator
{
    internal async Task GenerateAsync(
        string repositoryRoot,
        ScreenshotWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(workspace);

        CultureInfo english = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentCulture = english;
        CultureInfo.CurrentUICulture = english;

        await ScreenshotDataSeeder.SeedAsync(repositoryRoot, workspace, cancellationToken);

        ScreenshotCsvFilePicker picker = new();
        await using ServiceProvider provider = ScreenshotServiceProviderFactory.Create(
            workspace.Paths,
            picker,
            new FixedTimeProvider(ScreenshotDataSeeder.FixedNow));
        await provider.GetRequiredService<IPersistenceInitializer>()
            .InitializeAsync(cancellationToken);
        await provider.GetRequiredService<ProgressHistoryInitializer>()
            .EnsureInitializedAsync(cancellationToken);

        MainWindow window = provider.GetRequiredService<MainWindow>();
        MainWindowViewModel viewModel = provider.GetRequiredService<MainWindowViewModel>();
        ConfigureWindow(window);
        System.Windows.Application.Current.MainWindow = window;
        window.Show();

        try
        {
            await WaitUntilAsync(
                () => !viewModel.IsBusy &&
                      viewModel.TotalEntityCount == 125 &&
                      viewModel.Progress.HasReport,
                "The screenshot window did not finish loading.",
                cancellationToken);

            WpfScreenshotRenderer renderer = new(window, workspace.StagingDirectory);
            await CaptureOverviewAsync(viewModel, renderer, cancellationToken);

            viewModel.Review.Clear();
            viewModel.SelectedTab = MainWindowTab.SchemaSynchronization;
            await renderer.CaptureAsync("schema-synchronization.png");

            await CaptureMissingReviewAsync(
                repositoryRoot,
                picker,
                viewModel,
                window,
                renderer,
                cancellationToken);
            await CaptureUnresolvedReviewAsync(
                repositoryRoot,
                picker,
                viewModel,
                window,
                renderer,
                cancellationToken);

            viewModel.Review.Clear();
            viewModel.SelectedTab = MainWindowTab.AddEntity;
            await renderer.CaptureAsync("add-entity.png");

            await CaptureEditorAsync(viewModel, renderer, cancellationToken);

            viewModel.SelectedTab = MainWindowTab.Progress;
            await renderer.CaptureAsync("progress.png", settleMilliseconds: 900);

            await CaptureArchivedEntityAsync(
                provider,
                viewModel,
                renderer,
                cancellationToken);

            viewModel.SelectedTab = MainWindowTab.SqlHelp;
            await renderer.CaptureAsync("sql-query.png");
        }
        finally
        {
            window.Close();
            if (ReferenceEquals(System.Windows.Application.Current.MainWindow, window))
            {
                System.Windows.Application.Current.MainWindow = null;
            }
        }
    }

    private static async Task CaptureOverviewAsync(
        MainWindowViewModel viewModel,
        WpfScreenshotRenderer renderer,
        CancellationToken cancellationToken)
    {
        viewModel.SelectedTab = MainWindowTab.Overview;
        viewModel.ActiveTable.ClearAllFiltersAndSort();
        await renderer.CaptureAsync("overview.png");

        viewModel.OpenOverviewSearchCommand.Execute(null);
        viewModel.SearchOverviewDependencies = true;
        viewModel.OverviewSearchQuery = "unit";
        await WaitUntilAsync(
            () => viewModel.OverviewItems.Count is > 0 and < 125,
            "The deterministic overview search did not complete.",
            cancellationToken);
        await renderer.CaptureAsync("overview-search.png");

        viewModel.CloseOverviewSearchCommand.Execute(null);
        OverviewColumnFilterState workStatusFilter = viewModel.ActiveTable.WorkStatusFilter!;
        workStatusFilter.OpenCommand.Execute(null);
        foreach (OverviewFilterOption option in workStatusFilter.Options)
        {
            option.IsSelected = option.DisplayName == "Blocked";
        }

        workStatusFilter.ApplyCommand.Execute(null);
        await renderer.CaptureGraphIssueAsync(
            "overview-missing-entities-as-dependencies.png");
        viewModel.ActiveTable.ClearAllFiltersAndSort();
    }

    private static async Task CaptureMissingReviewAsync(
        string repositoryRoot,
        ScreenshotCsvFilePicker picker,
        MainWindowViewModel viewModel,
        MainWindow window,
        WpfScreenshotRenderer renderer,
        CancellationToken cancellationToken)
    {
        viewModel.Review.Clear();
        picker.SelectedPath = Path.Combine(repositoryRoot, "extracted_dependencies.csv");
        await viewModel.ImportCsvAsync(cancellationToken);
        if (!viewModel.Review.HasMissingEntities)
        {
            throw new InvalidDataException(
                "The deterministic missing-entity review contains no missing entities.");
        }

        await renderer.CaptureReviewSectionAsync(
            (FrameworkElement)window.FindName("MissingReviewSection"),
            "schema-synchronization-import-csv-with-missing-entities.png");
    }

    private static async Task CaptureUnresolvedReviewAsync(
        string repositoryRoot,
        ScreenshotCsvFilePicker picker,
        MainWindowViewModel viewModel,
        MainWindow window,
        WpfScreenshotRenderer renderer,
        CancellationToken cancellationToken)
    {
        viewModel.Review.Clear();
        picker.SelectedPath = Path.Combine(repositoryRoot, "synthetic_dependencies_125.csv");
        await viewModel.ImportCsvAsync(cancellationToken);
        if (!viewModel.Review.HasUnresolvedEntities)
        {
            throw new InvalidDataException(
                "The deterministic unresolved-dependency review contains no unresolved entities.");
        }

        await renderer.CaptureReviewSectionAsync(
            (FrameworkElement)window.FindName("UnresolvedReviewSection"),
            "schema-synchronization-unresolved-dependencies.png");
    }

    private static async Task CaptureEditorAsync(
        MainWindowViewModel viewModel,
        WpfScreenshotRenderer renderer,
        CancellationToken cancellationToken)
    {
        viewModel.Review.Clear();
        viewModel.SelectedTab = MainWindowTab.Overview;
        viewModel.ActiveTable.ClearAllFiltersAndSort();
        EntityOverviewRow row = viewModel.OverviewItems.Single(static item =>
            item.SourceName == "time_zone");
        await viewModel.Editor.BeginStandaloneAsync(row.EntityId, cancellationToken);
        await renderer.CaptureAsync("edit-entity.png");
        viewModel.Editor.CancelCommand.Execute(null);
    }

    private static async Task CaptureArchivedEntityAsync(
        IServiceProvider provider,
        MainWindowViewModel viewModel,
        WpfScreenshotRenderer renderer,
        CancellationToken cancellationToken)
    {
        IEntityRepository entityRepository = provider.GetRequiredService<IEntityRepository>();
        IDependencyRepository dependencyRepository = provider.GetRequiredService<IDependencyRepository>();
        IReadOnlyList<TrackedEntity> entities = await entityRepository.GetAllAsync(cancellationToken);
        HashSet<EntityId> dependencyTargets = (await dependencyRepository.GetAllAsync(cancellationToken))
            .Select(static dependency => dependency.Edge.DependencyEntityId)
            .ToHashSet();
        TrackedEntity leaf = entities
            .Where(static entity => entity.LifecycleState == EntityLifecycleState.Active)
            .Where(entity => !dependencyTargets.Contains(entity.Id))
            .OrderBy(static entity => entity.SourceName, StringComparer.Ordinal)
            .First();

        bool archived = await provider.GetRequiredService<EntityLifecycleService>()
            .TryArchiveAsync(leaf.Id, cancellationToken);
        if (!archived)
        {
            throw new InvalidDataException("The deterministic archived entity could not be created.");
        }

        await viewModel.RefreshAsync(cancellationToken);
        viewModel.SelectedTab = MainWindowTab.Archived;
        EntityOverviewRow archivedRow = viewModel.ArchivedItems.Single(item => item.EntityId == leaf.Id);
        await viewModel.Editor.BeginArchivedAsync(archivedRow.EntityId, cancellationToken);
        await renderer.CaptureAsync("archived-entity.png");
        viewModel.Editor.CancelCommand.Execute(null);
    }

    private static void ConfigureWindow(MainWindow window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.ShowInTaskbar = false;
        window.Left = -32000;
        window.Top = -32000;
        window.Width = 1920;
        window.Height = 1080;
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (!condition())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException(failureMessage);
            }

            await Dispatcher.Yield(DispatcherPriority.Background);
            await Task.Delay(25, cancellationToken);
        }
    }
}

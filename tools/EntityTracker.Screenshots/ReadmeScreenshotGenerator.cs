using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using EntityTracker.Application.History;
using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Tracking;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace EntityTracker.Screenshots;

internal sealed class ReadmeScreenshotGenerator
{
    internal async Task GenerateAsync(
        string repositoryRoot,
        ScreenshotWorkspace workspace,
        ApplicationAppearance appearance,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(workspace);
        if (appearance is not (ApplicationAppearance.Light or ApplicationAppearance.Dark))
        {
            throw new ArgumentOutOfRangeException(nameof(appearance));
        }

        CultureInfo english = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentCulture = english;
        CultureInfo.CurrentUICulture = english;

        await ScreenshotDataSeeder.SeedAsync(repositoryRoot, workspace, cancellationToken);

        ScreenshotCsvFilePicker picker = new();
        await using ServiceProvider provider = ScreenshotServiceProviderFactory.Create(
            workspace.Paths,
            picker,
            new FixedTimeProvider(ScreenshotDataSeeder.FixedNow),
            appearance);
        await provider.GetRequiredService<IPersistenceInitializer>()
            .InitializeAsync(cancellationToken);
        Project project = (await provider.GetRequiredService<IProjectRepository>()
                .GetAllAsync(cancellationToken))
            .Single(static item => item.Name == ScreenshotDataSeeder.PrimaryProjectName);
        Tracker tracker = (await provider.GetRequiredService<ITrackerRepository>()
                .GetAllAsync(cancellationToken))
            .Single(static item => item.Name == ScreenshotDataSeeder.PrimaryTrackerName);
        await provider.GetRequiredService<ProgressHistoryInitializer>()
            .EnsureInitializedAsync(tracker.Id, cancellationToken);

        ShellViewModel shell = provider.GetRequiredService<ShellViewModel>();
        MainWindow window = new(shell);
        ConfigureWindow(window);
        System.Windows.Application.Current.MainWindow = window;
        window.Show();

        try
        {
            await WaitUntilAsync(
                () => !shell.IsBusy && shell.Portfolio?.Projects.Count == 2,
                "The screenshot window did not finish loading.",
                cancellationToken);
            await ExerciseLiveThemeSwitchAsync(provider, appearance, cancellationToken);

            WpfScreenshotRenderer renderer = new(window, workspace.StagingDirectory);
            await renderer.CaptureAsync("portfolio.png");

            await shell.OpenProjectAsync(project.Id);
            await WaitUntilAsync(
                () => shell.ProjectDashboard?.Trackers.Count == 2,
                "The project dashboard did not finish loading.",
                cancellationToken);
            await renderer.CaptureAsync("project-dashboard.png");

            await CaptureTrackerLifecycleAsync(shell, renderer, cancellationToken);

            shell.Catalog.OpenCreateTracker(project);
            shell.Catalog.CreationMode = TrackerCreationMode.Copy;
            await WaitUntilAsync(
                () => shell.Catalog.CopySources.Count == 3,
                "The tracker copy sources did not finish loading.",
                cancellationToken);
            shell.Catalog.Name = "Pre-production readiness";
            shell.Catalog.SelectedCopySource = shell.Catalog.CopySources.Single(
                item => item.Tracker.Id == tracker.Id);
            await WaitUntilAsync(
                () => !string.IsNullOrWhiteSpace(shell.Catalog.CopyPreview),
                "The tracker copy preview did not finish loading.",
                cancellationToken);
            await renderer.CaptureAsync("create-tracker-copy.png");
            shell.Catalog.CancelCommand.Execute(null);

            await shell.OpenTrackerAsync(tracker.Id);
            MainWindowViewModel viewModel = shell.CurrentWorkspace
                ?? throw new InvalidOperationException("The tracker workspace was not created.");
            await WaitUntilAsync(
                () => !viewModel.IsBusy &&
                      viewModel.TotalEntityCount == 125 &&
                      viewModel.Progress.HasReport,
                "The tracker workspace did not finish loading.",
                cancellationToken);
            await CaptureOverviewAsync(viewModel, renderer, cancellationToken);

            viewModel.Review.Clear();
            await shell.NavigateAsync(ShellDestination.SchemaSynchronization, cancellationToken);
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
            await shell.NavigateAsync(ShellDestination.AddEntity, cancellationToken);
            await renderer.CaptureAsync("add-entity.png");

            await CaptureEditorAsync(shell, viewModel, renderer, cancellationToken);

            await shell.NavigateAsync(ShellDestination.Reports, cancellationToken);
            await renderer.CaptureAsync("progress.png", settleMilliseconds: 900);

            await CaptureArchivedEntityAsync(
                provider,
                shell,
                viewModel,
                renderer,
                cancellationToken);

            await shell.NavigateAsync(ShellDestination.HelpSql, cancellationToken);
            await renderer.CaptureAsync("sql-query.png");

            await shell.NavigateAsync(ShellDestination.Settings, cancellationToken);
            await renderer.CaptureAsync("settings.png");
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

    private static async Task CaptureTrackerLifecycleAsync(
        ShellViewModel shell,
        WpfScreenshotRenderer renderer,
        CancellationToken cancellationToken)
    {
        Tracker tracker = shell.Trackers.Single(static item => item.Name == "Release readiness");
        shell.Catalog.RequestRecycle(tracker);
        await renderer.CaptureAsync("tracker-recycle-confirmation.png");

        shell.Catalog.ConfirmRecycleCommand.Execute(null);
        await WaitUntilAsync(
            () => !shell.Catalog.IsOpen &&
                  !shell.IsBusy &&
                  shell.SelectedDestination == ShellDestination.ProjectDashboard &&
                  shell.ProjectDashboard?.Trackers.Count == 1,
            "The Tracker recycle did not return to the Project dashboard.",
            cancellationToken);

        await shell.Catalog.OpenRecycleBinAsync(shell.SelectedProject);
        await WaitUntilAsync(
            () => shell.Catalog.RecycledTrackers.Any(item => item.Id == tracker.Id),
            "The recycled Tracker did not appear in its Project recycle bin.",
            cancellationToken);
        await renderer.CaptureAsync("tracker-recycle-bin.png");

        Tracker recycled = shell.Catalog.RecycledTrackers.Single(item => item.Id == tracker.Id);
        await shell.Catalog.RestoreAsync(recycled);
        await WaitUntilAsync(
            () => !shell.Catalog.IsOpen &&
                  !shell.IsBusy &&
                  shell.SelectedDestination == ShellDestination.ProjectDashboard &&
                  shell.ProjectDashboard?.Trackers.Count == 2,
            "The Tracker restore did not return to the Project dashboard.",
            cancellationToken);
        await renderer.CaptureAsync("project-dashboard-tracker-restored.png");
    }

    private static async Task CaptureOverviewAsync(
        MainWindowViewModel viewModel,
        WpfScreenshotRenderer renderer,
        CancellationToken cancellationToken)
    {
        viewModel.SelectedTab = MainWindowTab.Overview;
        viewModel.ActiveTable.ClearAllFiltersAndSort();
        await renderer.CaptureAsync("overview.png");

        EntityOverviewRow detailsRow = viewModel.OverviewItems.Single(static item =>
            item.SourceName == "customer_preference");
        viewModel.OpenEntityDetailsCommand.Execute(detailsRow);
        await renderer.CaptureAsync("overview-details.png");
        viewModel.CloseEntityDetails();

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
        await renderer.CaptureOpenPopupAsync("overview-filter-flyout.png");
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
            window.FindWorkspaceElement("MissingReviewSection")
                ?? throw new InvalidOperationException("Missing review section not found."),
            (ScrollViewer)(window.FindWorkspaceElement("SchemaReviewScrollViewer")
                ?? throw new InvalidOperationException("Schema review scroll viewer not found.")),
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
            window.FindWorkspaceElement("UnresolvedReviewSection")
                ?? throw new InvalidOperationException("Unresolved review section not found."),
            (ScrollViewer)(window.FindWorkspaceElement("SchemaReviewScrollViewer")
                ?? throw new InvalidOperationException("Schema review scroll viewer not found.")),
            "schema-synchronization-unresolved-dependencies.png");
    }

    private static async Task CaptureEditorAsync(
        ShellViewModel shell,
        MainWindowViewModel viewModel,
        WpfScreenshotRenderer renderer,
        CancellationToken cancellationToken)
    {
        viewModel.Review.Clear();
        await shell.NavigateAsync(ShellDestination.Overview, cancellationToken);
        viewModel.ActiveTable.ClearAllFiltersAndSort();
        EntityOverviewRow row = viewModel.OverviewItems.Single(static item =>
            item.SourceName == "time_zone");
        await viewModel.Editor.BeginStandaloneAsync(row.EntityId, cancellationToken);
        await renderer.CaptureAsync("edit-entity.png");
        viewModel.Editor.CancelCommand.Execute(null);
    }

    private static async Task CaptureArchivedEntityAsync(
        IServiceProvider provider,
        ShellViewModel shell,
        MainWindowViewModel viewModel,
        WpfScreenshotRenderer renderer,
        CancellationToken cancellationToken)
    {
        Tracker tracker = (await provider.GetRequiredService<ITrackerRepository>()
                .GetAllAsync(cancellationToken))
            .Single(static item => item.Name == ScreenshotDataSeeder.PrimaryTrackerName);
        IEntityRepository entityRepository = provider.GetRequiredService<IEntityRepository>();
        IDependencyRepository dependencyRepository = provider.GetRequiredService<IDependencyRepository>();
        IReadOnlyList<TrackedEntity> entities = await entityRepository.GetAllAsync(
            tracker.Id,
            cancellationToken);
        HashSet<EntityId> dependencyTargets = (await dependencyRepository.GetAllAsync(
                tracker.Id,
                cancellationToken))
            .Select(static dependency => dependency.Edge.DependencyEntityId)
            .ToHashSet();
        TrackedEntity leaf = entities
            .Where(static entity => entity.LifecycleState == EntityLifecycleState.Active)
            .Where(entity => !dependencyTargets.Contains(entity.Id))
            .OrderBy(static entity => entity.SourceName, StringComparer.Ordinal)
            .First();

        bool archived = await provider.GetRequiredService<EntityLifecycleService>()
            .TryArchiveAsync(tracker.Id, leaf.Id, cancellationToken);
        if (!archived)
        {
            throw new InvalidDataException("The deterministic archived entity could not be created.");
        }

        await viewModel.RefreshAsync(cancellationToken);
        await shell.NavigateAsync(ShellDestination.Archived, cancellationToken);
        EntityOverviewRow archivedRow = viewModel.ArchivedItems.Single(item => item.EntityId == leaf.Id);
        viewModel.OpenEntityDetailsCommand.Execute(archivedRow);
        await renderer.CaptureAsync("archived-details.png");
        viewModel.CloseEntityDetails();
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

    private static async Task ExerciseLiveThemeSwitchAsync(
        IServiceProvider provider,
        ApplicationAppearance appearance,
        CancellationToken cancellationToken)
    {
        IApplicationThemeService themeService =
            provider.GetRequiredService<IApplicationThemeService>();
        ApplicationAppearance opposite = appearance == ApplicationAppearance.Dark
            ? ApplicationAppearance.Light
            : ApplicationAppearance.Dark;

        themeService.Apply(opposite);
        await Dispatcher.Yield(DispatcherPriority.Render);
        themeService.Apply(appearance);
        await Dispatcher.Yield(DispatcherPriority.Render);
        await Task.Delay(100, cancellationToken);
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

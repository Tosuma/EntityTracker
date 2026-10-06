using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

using EntityTracker.Application.History;
using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Projects;
using EntityTracker.Application.Tracking;
using EntityTracker.Application.GitSync;
using EntityTracker.Wpf.Views;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf;
using EntityTracker.Wpf.Controls;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;
using EntityTracker.Wpf.ViewModels.DependencyGraph;

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
        FixedTimeProvider captureTime = new(ScreenshotDataSeeder.FixedNow);
        await using ServiceProvider provider = ScreenshotServiceProviderFactory.Create(
            workspace.Paths,
            picker,
            captureTime,
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
                () => !shell.IsBusy &&
                      shell.PortfolioReporting.Dashboard?.Projects.Count == 2 &&
                      shell.PortfolioReporting.Progress.HasReport,
                "The screenshot window did not finish loading.",
                cancellationToken);
            await ExerciseLiveThemeSwitchAsync(provider, appearance, cancellationToken);

            WpfScreenshotRenderer renderer = new(window, workspace.StagingDirectory);
            await renderer.CaptureAsync("portfolio.png");

            await shell.OpenProjectAsync(project.Id);
            await WaitUntilAsync(
                () => shell.ProjectReporting?.Dashboard?.Trackers.Count == 2 &&
                      shell.ProjectReporting.Progress.HasReport &&
                      shell.ProjectReporting.Comparison is not null,
                "The project dashboard did not finish loading.",
                cancellationToken);
            await renderer.CaptureAsync("project-dashboard.png", settleMilliseconds: 900);
            ProjectDeveloperService developerService = provider.GetRequiredService<ProjectDeveloperService>();
            await developerService.CreateAsync(project.Id, "AB", "Alice Brown", cancellationToken);
            await developerService.CreateAsync(project.Id, "CD", "Chris Davis", cancellationToken);
            await shell.NavigateAsync(ShellDestination.Developers, cancellationToken);
            await renderer.CaptureAsync("project-developers.png", settleMilliseconds: 500);
            ProjectDeveloper alice = (await developerService.ListAsync(project.Id, cancellationToken))
                .Single(developer => developer.Initials == "AB");
            shell.Developers!.BeginRetirement(alice);
            await renderer.CaptureAsync("project-developer-retirement.png", settleMilliseconds: 500);
            shell.Developers.CancelRetirement();
            ProjectDeveloper chris = (await developerService.ListAsync(project.Id, cancellationToken))
                .Single(developer => developer.Initials == "CD");
            await developerService.SetRetiredAsync(project.Id, chris.Id, true, cancellationToken);
            await shell.Developers.RefreshAsync(cancellationToken);
            shell.Developers.OpenRetired();
            await renderer.CaptureAsync("project-developers-retired.png", settleMilliseconds: 500);
            shell.Developers.CloseRetired();

            // The Project report page, plus a client and an internal export kept for review.
            await shell.NavigateAsync(ShellDestination.ProjectReport, cancellationToken);
            await renderer.CaptureAsync("project-report.png", settleMilliseconds: 500);
            string samples = Directory.CreateDirectory(Path.Combine(repositoryRoot, "artifacts", "report-samples")).FullName;
            foreach (EntityTracker.Reporting.ProjectReports.ReportAudience audience in
                     Enum.GetValues<EntityTracker.Reporting.ProjectReports.ReportAudience>())
            {
                shell.ProjectReport!.Audience = audience;
                EntityTracker.Reporting.ProjectReports.ProjectReport sample =
                    await shell.ProjectReport.BuildAsync(cancellationToken);
                await File.WriteAllTextAsync(Path.Combine(samples, $"{appearance}-{audience}.html".ToLowerInvariant()),
                    EntityTracker.Reporting.ProjectReports.ProjectReportHtmlWriter.Write(sample), cancellationToken);
            }

            shell.ProjectReport!.Audience = EntityTracker.Reporting.ProjectReports.ReportAudience.Client;
            await shell.NavigateAsync(ShellDestination.ProjectDashboard, cancellationToken);
            // Presentation fixture only. The real folder stays in the disposable workspace.
            string repositoryFixturePath = Directory.CreateDirectory(
                Path.Combine(workspace.RootDirectory, "example-repository")).FullName;
            IProjectSyncLinkStore linkStore = provider.GetRequiredService<IProjectSyncLinkStore>();
            ProjectSyncLink repositoryFixture = new(
                project.Id.Value,
                repositoryFixturePath,
                "main",
                "origin/main:illustrative-upstream",
                null,
                0,
                null,
                "Snapshot pushed",
                "Current");
            await linkStore.SaveAsync(repositoryFixture, cancellationToken);
            await shell.ProjectReporting!.RepositoryCard!.RefreshAsync(cancellationToken);
            shell.ProjectReporting.RepositoryCard.ShowTiming(new ProjectSyncTiming(
                TimeSpan.FromSeconds(18.4),
                new Dictionary<ProjectSyncPhase, TimeSpan>
                {
                    [ProjectSyncPhase.Fetching] = TimeSpan.FromSeconds(11.7),
                    [ProjectSyncPhase.Exporting] = TimeSpan.FromSeconds(2.1),
                    [ProjectSyncPhase.Validating] = TimeSpan.FromSeconds(4.6)
                },
                TimeSpan.FromSeconds(8.2), TimeSpan.FromSeconds(3.5), 142, 1, 0,
                "Completed"));
            await renderer.CaptureWithTextOverrideAsync(
                "project-git-repository.png", repositoryFixturePath,
                @"C:\Projects\order-platform", settleMilliseconds: 900);
            NotificationCenter notifications = provider.GetRequiredService<NotificationCenter>();
            NotificationItem syncNotice = notifications.BeginProgress("Project sync",
                "Fetching upstream changes…");
            await renderer.CaptureWithTextOverrideAsync(
                "project-sync-progress.png", repositoryFixturePath,
                @"C:\Projects\order-platform", settleMilliseconds: 900);
            await linkStore.SaveAsync(repositoryFixture with
            {
                LastResult = "Upstream advanced during sync; validation pending",
                SyncStatus = "PendingRemote"
            }, cancellationToken);
            await shell.ProjectReporting.RepositoryCard.RefreshAsync(cancellationToken);
            shell.ProjectReporting.RepositoryCard.ShowTiming(new ProjectSyncTiming(
                TimeSpan.FromSeconds(21.2),
                new Dictionary<ProjectSyncPhase, TimeSpan>
                {
                    [ProjectSyncPhase.Rechecking] = TimeSpan.FromSeconds(15.2),
                    [ProjectSyncPhase.Fetching] = TimeSpan.FromSeconds(4.0),
                    [ProjectSyncPhase.Validating] = TimeSpan.FromSeconds(2.0)
                },
                TimeSpan.FromSeconds(18.0), TimeSpan.FromSeconds(1.2), 142, 1, 0,
                "Action needed"));
            notifications.NeedAction(syncNotice,
                "The upstream advanced during sync. Retry to validate its changes.",
                "Retry", () => Task.CompletedTask);
            await renderer.CaptureWithTextOverrideAsync(
                "project-sync-action-needed.png", repositoryFixturePath,
                @"C:\Projects\order-platform", settleMilliseconds: 900);
            notifications.Dismiss(syncNotice);
            await linkStore.SaveAsync(repositoryFixture, cancellationToken);
            await shell.ProjectReporting.RepositoryCard.RefreshAsync(cancellationToken);
            ProjectMergeReviewDialog mergeDialog = new(new ProjectMergeReviewViewModel([
                new ProjectMergeConflict("Tracker/00000000-0000-0000-0000-000000000001",
                    ProjectConflictKind.Addition, null, "Local Tracker snapshot", "Remote Tracker snapshot")
                {
                    Display = new ProjectMergeConflictDisplay("Tracker: Delivery",
                        "Not present",
                        "Entity: Orders / Internal notes: Add invoice validation before release",
                        "Entity: Orders / Internal notes: Coordinate rollout with the fulfillment team")
                }
            ]))
            {
                Owner = window,
                Height = 550
            };
            Grid mergeContent = (Grid)mergeDialog.Content;
            mergeDialog.Content = null;
            mergeContent.Margin = new Thickness(0);
            mergeDialog.Content = new Border { Padding = new Thickness(20), Child = mergeContent };
            mergeDialog.Show();
            try
            {
                await new WpfScreenshotRenderer(mergeDialog, workspace.StagingDirectory)
                    .CaptureAsync("project-merge-review.png", settleMilliseconds: 900);
            }
            finally { mergeDialog.Close(); }
            await renderer.BringNamedElementIntoViewAndCaptureAsync(
                "ComparisonGrid",
                "project-comparison.png");

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

            EntityId featuredEntity = (await provider.GetRequiredService<IEntityRepository>()
                    .GetAllAsync(tracker.Id, cancellationToken))
                .Single(entity => entity.SourceName == "customer_preference").Id;
            ProjectDeveloper platform = (await developerService.ListAsync(project.Id, cancellationToken))
                .Single(developer => developer.Initials == "PT");
            ITrackedStateStore responsibilityStore = provider.GetRequiredService<ITrackedStateStore>();
            await responsibilityStore.ApplyAsync(tracker.Id,
                new TrackedStateChangeSet([], [], [], [], [], [],
                    responsibilitySelections: [new ResponsibilitySelection(featuredEntity,
                        [alice.Id])]), cancellationToken);
            captureTime.Advance(TimeSpan.FromMinutes(1));
            await responsibilityStore.ApplyAsync(tracker.Id,
                new TrackedStateChangeSet([], [], [], [], [], [],
                    responsibilitySelections: [new ResponsibilitySelection(featuredEntity,
                        [alice.Id, platform.Id])]), cancellationToken);
            captureTime.Advance(TimeSpan.FromMinutes(1));
            await responsibilityStore.ApplyAsync(tracker.Id,
                new TrackedStateChangeSet([], [], [], [], [], [],
                    responsibilitySelections: [new ResponsibilitySelection(featuredEntity,
                        [platform.Id])]), cancellationToken);

            captureTime.Advance(TimeSpan.FromMinutes(1));
            await responsibilityStore.ApplyAsync(tracker.Id,
                new TrackedStateChangeSet([], [], [], [], [], [],
                    responsibilitySelections: [new ResponsibilitySelection(featuredEntity,
                        [alice.Id, platform.Id])]), cancellationToken);
            captureTime.Advance(TimeSpan.FromMinutes(1));
            await responsibilityStore.ApplyAsync(tracker.Id,
                new TrackedStateChangeSet([], [], [], [], [], [],
                    responsibilitySelections: [new ResponsibilitySelection(featuredEntity,
                        [platform.Id])]), cancellationToken);

            captureTime.Advance(TimeSpan.FromMinutes(1));
            await responsibilityStore.ApplyAsync(tracker.Id,
                new TrackedStateChangeSet([], [], [], [], [], [],
                    responsibilitySelections: [new ResponsibilitySelection(featuredEntity,
                        [alice.Id, platform.Id])]), cancellationToken);
            captureTime.Advance(TimeSpan.FromMinutes(1));
            await responsibilityStore.ApplyAsync(tracker.Id,
                new TrackedStateChangeSet([], [], [], [], [], [],
                    responsibilitySelections: [new ResponsibilitySelection(featuredEntity,
                        [platform.Id])]), cancellationToken);

            // Show two separate current Developers in the overview and filter screenshots.
            captureTime.Advance(TimeSpan.FromMinutes(1));
            await responsibilityStore.ApplyAsync(tracker.Id,
                new TrackedStateChangeSet([], [], [], [], [], [],
                    responsibilitySelections: [new ResponsibilitySelection(featuredEntity,
                        [alice.Id, platform.Id])]), cancellationToken);

            await shell.OpenTrackerAsync(tracker.Id);
            MainWindowViewModel viewModel = shell.CurrentWorkspace
                ?? throw new InvalidOperationException("The tracker workspace was not created.");
            await WaitUntilAsync(
                () => !viewModel.IsBusy &&
                      viewModel.TotalEntityCount == 125 &&
                      viewModel.Progress.HasReport,
                "The tracker workspace did not finish loading.",
                cancellationToken);
            await CaptureOverviewAsync(viewModel, window, renderer, cancellationToken);
            await CaptureDependencyGraphAsync(shell, viewModel, window, renderer, cancellationToken);

            viewModel.Review.Clear();
            await shell.NavigateAsync(ShellDestination.SchemaSynchronization, cancellationToken);
            await renderer.CaptureAsync("schema-synchronization.png");

            await CaptureChangedReviewAsync(
                workspace,
                picker,
                viewModel,
                renderer,
                cancellationToken);
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
            await PopulateManualCreationAsync(viewModel, cancellationToken);
            await renderer.CaptureAsync("add-entity.png");

            viewModel.ManualCreation.CancelCommand.Execute(null);
            await CaptureEditorAsync(shell, viewModel, window, renderer, cancellationToken);

            await shell.NavigateAsync(ShellDestination.Reports, cancellationToken);
            await renderer.CaptureAsync("progress.png", settleMilliseconds: 900);

            await CaptureArchivedEntityAsync(
                provider,
                shell,
                viewModel,
                renderer,
                cancellationToken);

            await shell.NavigateAsync(ShellDestination.HelpSql, cancellationToken);
            await renderer.CaptureAsync("help-and-sql.png");
            await renderer.BringNamedElementIntoViewAndCaptureAsync(
                "QueryTextBox",
                "sql-query.png");

            shell.SelectedSettingsCategory = SettingsCategory.General;
            await shell.NavigateAsync(ShellDestination.Settings, cancellationToken);
            await renderer.CaptureAsync("settings.png");
            shell.SelectedSettingsCategory = SettingsCategory.Project;
            await renderer.CaptureAsync("settings-project.png");
            shell.SelectedSettingsCategory = SettingsCategory.Tracker;
            await renderer.CaptureAsync("settings-tracker.png");
            shell.SelectedSettingsCategory = SettingsCategory.Sync;
            await renderer.CaptureAsync("settings-sync.png");
            shell.SelectedSettingsCategory = SettingsCategory.About;
            await renderer.CaptureAsync("settings-about.png");
            window.ShowUpdatePreview("app-v1.0.0");
            await renderer.CaptureAsync("app-update-required.png");
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

    private static async Task CaptureChangedReviewAsync(
        ScreenshotWorkspace workspace,
        ScreenshotCsvFilePicker picker,
        MainWindowViewModel viewModel,
        WpfScreenshotRenderer renderer,
        CancellationToken cancellationToken)
    {
        EntityOverviewRow affectedEntity = viewModel.OverviewItems
            .Where(static row => row.DevelopmentStatus is
                DevelopmentStatus.DevelopmentCompleted or DevelopmentStatus.Reconciled)
            .Where(static row => row.DependencyNames.Count > 0)
            .OrderBy(static row => row.SourceName, StringComparer.Ordinal)
            .FirstOrDefault()
            ?? throw new InvalidDataException(
                "The deterministic tracker has no completed entity with dependencies.");
        string[] importedDependencies = affectedEntity.DependencyNames
            .Skip(1)
            .Append("screenshot_missing_dependency")
            .ToArray();
        string partialSchemaPath = Path.Combine(
            workspace.RootDirectory,
            "partial-schema-changes.csv");
        await File.WriteAllLinesAsync(
            partialSchemaPath,
            [
                "table_name;mandatory_dependencies;mandatory_dependency_count;optional_dependencies;optional_dependency_count;total_dependency_count",
                $"{affectedEntity.SourceName};{string.Join(", ", importedDependencies)};{importedDependencies.Length};;0;{importedDependencies.Length}"
            ],
            cancellationToken);

        viewModel.Review.Clear();
        viewModel.Review.IsPartialImport = true;
        picker.SelectedPath = partialSchemaPath;
        await viewModel.ImportCsvAsync(cancellationToken);
        if (!viewModel.Review.HasChangedEntities ||
            viewModel.Review.PendingProgressDecisionCount == 0)
        {
            throw new InvalidDataException(
                "The deterministic changed-entity review contains no progress decision.");
        }

        viewModel.Review.ToggleFilterCommand.Execute(
            SchemaSynchronizationReviewFilter.Changed);
        if (!viewModel.Review.IsChangedFilterSelected)
        {
            throw new InvalidDataException(
                "The deterministic changed-entity review filter was not selected.");
        }

        await renderer.CaptureAsync("schema-synchronization-changed-entities.png");
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
        await shell.Catalog.RequestPurgeAsync(recycled);
        await renderer.CaptureAsync("tracker-permanent-delete-confirmation.png");
        shell.Catalog.CancelCommand.Execute(null);

        await shell.Catalog.OpenRecycleBinAsync(shell.SelectedProject);
        recycled = shell.Catalog.RecycledTrackers.Single(item => item.Id == tracker.Id);
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
        MainWindow window,
        WpfScreenshotRenderer renderer,
        CancellationToken cancellationToken)
    {
        viewModel.SelectedTab = MainWindowTab.Overview;
        viewModel.ActiveTable.ClearAllFiltersAndSort();
        await renderer.CaptureAsync("overview.png");
        if (window.FindWorkspaceElement("OverviewExportButton") is not Button)
            throw new InvalidOperationException("The Overview export button was not rendered.");
        Popup exportPopup = (Popup)(window.FindWorkspaceElement("OverviewExportPopup")
            ?? throw new InvalidOperationException("The Overview export menu was not rendered."));
        exportPopup.IsOpen = true;
        await renderer.CapturePopupAsync(exportPopup, "overview-export-menu.png");
        exportPopup.IsOpen = false;

        EntityOverviewRow detailsRow = viewModel.OverviewItems.Single(static item =>
            item.SourceName == "customer_preference");
        DataGrid overview = (DataGrid)(window.FindWorkspaceElement("OverviewDataGrid")
            ?? throw new InvalidOperationException("The overview table was not rendered."));
        overview.SelectedItem = detailsRow;
        viewModel.OpenEntityDetailsCommand.Execute(detailsRow);
        await WaitUntilAsync(() => viewModel.SelectedEntityDetails?.ResponsibilityTimeline.Count > 0,
            "Responsibility details did not load.", cancellationToken);
        await renderer.CaptureAsync("overview-details.png");
        viewModel.ShowFullResponsibilityHistoryCommand.Execute(null);
        await renderer.CaptureAsync("responsibility-history.png");
        viewModel.CloseEntityDetails();
        overview.UnselectAll();

        viewModel.OpenOverviewSearchCommand.Execute(null);
        viewModel.SearchOverviewDependencies = true;
        viewModel.OverviewSearchQuery = "unit";
        await WaitUntilAsync(
            () => viewModel.OverviewItems.Count is > 0 and < 125,
            "The deterministic overview search did not complete.",
            cancellationToken);
        await renderer.CaptureAsync("overview-search.png");

        viewModel.CloseOverviewSearchCommand.Execute(null);
        await Task.Delay(300, cancellationToken);
        OverviewColumnFilterState responsibleFilter = viewModel.ActiveTable.ResponsibleDeveloperFilter;
        responsibleFilter.OpenCommand.Execute(null);
        if (responsibleFilter.Options.Count == 0 ||
            responsibleFilter.Options.Any(static option => option.IsSelected))
            throw new InvalidDataException(
                "The responsible filter did not open with unchecked choices.");
        responsibleFilter.Options.Single(static option =>
            option.DisplayName == "AB — Alice Brown").IsSelected = true;
        responsibleFilter.ApplyCommand.Execute(null);
        await Task.Delay(300, cancellationToken);
        responsibleFilter.OpenCommand.Execute(null);
        responsibleFilter.ClearFilterCommand.Execute(null);
        if (!responsibleFilter.IsOpen || responsibleFilter.IsApplied ||
            responsibleFilter.Options.Any(static option => option.IsSelected))
            throw new InvalidDataException(
                "Clearing the responsible filter did not leave the menu open and unchecked.");
        await renderer.CaptureOpenPopupAsync("overview-filter-flyout.png");
        responsibleFilter.CloseWithoutApplying();
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

    private static async Task CaptureDependencyGraphAsync(
        ShellViewModel shell,
        MainWindowViewModel viewModel,
        MainWindow window,
        WpfScreenshotRenderer renderer,
        CancellationToken cancellationToken)
    {
        // A still map keeps the captures deterministic.
        viewModel.DependencyGraph.IsAnimationEnabled = false;
        await shell.NavigateAsync(ShellDestination.DependencyGraph, cancellationToken);
        DependencyGraphViewModel graph = viewModel.DependencyGraph;
        for (int attempt = 0; attempt < 25 && !graph.Layout.IsSettled; attempt++)
            graph.Layout.Settle();
        if (!graph.Layout.IsSettled)
            throw new InvalidDataException("The deterministic dependency graph did not settle.");
        graph.FitToViewCommand.Execute(null);
        await renderer.CaptureAsync("dependency-graph.png", settleMilliseconds: 500);

        // Feature a readable, mid-sized dependency chain; ties resolve by name.
        DependencyGraphNode featured = graph.Model.Nodes
            .Where(static node => !node.IsPlaceholder)
            .Select(node => { graph.SelectedNode = node; return (node, graph.HighlightedNodes.Count); })
            .OrderBy(static pair => Math.Abs(pair.Count - 10))
            .ThenBy(static pair => pair.node.Label, StringComparer.Ordinal)
            .First().node;
        graph.SelectedNode = featured;
        await renderer.CaptureAsync("dependency-graph-selected.png", settleMilliseconds: 500);

        // Hover the most referenced landmark to show its info card.
        graph.SelectedNode = null;
        DependencyGraphCanvas canvas = (DependencyGraphCanvas)(window.FindWorkspaceElement("DependencyGraphCanvas")
            ?? throw new InvalidOperationException("The dependency graph was not rendered."));
        DependencyGraphNode hovered = graph.Landmarks
            .OrderByDescending(static node => node.DependentCount)
            .ThenBy(static node => node.Label, StringComparer.Ordinal)
            .First();
        canvas.ShowHover(hovered);
        await renderer.CaptureAsync("dependency-graph-hover.png", settleMilliseconds: 500);
        canvas.ShowHover(null);

        // The search suggests entities word by word, like the dependency search: "cust a" finds
        // customer_address, customer_account and the like.
        ComboBox graphSearch = (ComboBox)(window.FindWorkspaceElement("DependencyGraphSearchBox")
            ?? throw new InvalidOperationException("The dependency graph search box is missing."));
        graph.SearchText = "cust a";
        if (!graph.IsSuggestionsOpen)
            throw new InvalidOperationException("The graph search did not suggest any entity for 'cust a'.");
        graphSearch.ApplyTemplate();
        Popup suggestions = (Popup)(graphSearch.Template.FindName("PART_Popup", graphSearch)
            ?? throw new InvalidOperationException("The graph search has no suggestion popup."));
        // The ComboBox template places its list relative to itself without naming a target.
        suggestions.PlacementTarget ??= graphSearch;
        await renderer.CapturePopupAsync(suggestions, "dependency-graph-search.png");
        graph.IsSuggestionsOpen = false;
        graph.SearchText = string.Empty;

        // The same Tracker as a top-to-bottom tree, whole and with the featured chain highlighted.
        graph.View = DependencyGraphView.Tree;
        graph.FitToViewCommand.Execute(null);
        await renderer.CaptureAsync("dependency-graph-tree.png", settleMilliseconds: 500);
        // Zoom in on the featured chain so the wrapped names and status bands are readable.
        graph.SearchText = featured.Label;
        graph.FindCommand.Execute(null);
        await renderer.CaptureAsync("dependency-graph-tree-selected.png", settleMilliseconds: 500);
        graph.SearchText = string.Empty;
        graph.View = DependencyGraphView.SolarSystem;

        // Two entities Ctrl-selected with everything that depends on them highlighted.
        DependencyGraphNode[] roots = graph.Model.Nodes
            .Where(static node => node.EntityId is not null && node.TransitiveDependentCount is >= 4 and <= 20)
            .OrderBy(static node => Math.Abs(node.TransitiveDependentCount - 10))
            .ThenBy(static node => node.Label, StringComparer.Ordinal)
            .Take(2).ToArray();
        if (roots.Length < 2)
            throw new InvalidOperationException("The sample graph has no entities with a moderate number of dependents.");
        graph.HighlightMode = DependencyHighlightMode.Dependents;
        graph.SelectedNode = roots[0];
        graph.ToggleSelection(roots[1]);
        // Zoom in around the highlighted entities so their names and links are readable.
        Point[] highlighted = graph.HighlightedNodes.Select(canvas.ScreenPositionOf).ToArray();
        canvas.PointerWheel(new Point(highlighted.Average(static point => point.X),
            highlighted.Average(static point => point.Y)), 400);
        await renderer.CaptureAsync("dependency-graph-dependents.png", settleMilliseconds: 500);
        graph.HighlightMode = DependencyHighlightMode.Dependencies;
        graph.SelectedNode = featured;

        if (!graph.OpenDetails(featured))
            throw new InvalidOperationException("The featured graph entity has no details.");
        await WaitUntilAsync(() => viewModel.SelectedEntityDetails is not null && !viewModel.IsBusy,
            "Entity details did not open from the dependency graph.", cancellationToken);
        await renderer.CaptureAsync("dependency-graph-details.png", settleMilliseconds: 500);
        viewModel.CloseEntityDetails();
        graph.SelectedNode = null;
        viewModel.SelectedTab = MainWindowTab.Overview;
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
        MainWindow window,
        WpfScreenshotRenderer renderer,
        CancellationToken cancellationToken)
    {
        viewModel.Review.Clear();
        await shell.NavigateAsync(ShellDestination.Overview, cancellationToken);
        viewModel.ActiveTable.ClearAllFiltersAndSort();
        EntityOverviewRow row = viewModel.OverviewItems.Single(static item =>
            item.SourceName == "customer_preference");
        await viewModel.Editor.BeginStandaloneAsync(row.EntityId, cancellationToken);
        viewModel.Editor.SelectedRequestedPriority = 2;
        await renderer.CaptureAsync("edit-entity.png");

        viewModel.Editor.DependencyQuery = "future_customer_profile";
        await WaitUntilAsync(
            () => viewModel.Editor.CanAddAsUnresolved,
            "The editor unresolved dependency action did not become available.",
            cancellationToken);
        viewModel.Editor.AddUnresolvedCommand.Execute(null);
        await renderer.ScrollSectionIntoViewAndCaptureAsync(
            window.FindWorkspaceElement("EditorDependenciesSection")
                ?? throw new InvalidOperationException("Editor dependencies section not found."),
            (ScrollViewer)(window.FindWorkspaceElement("EditorScrollViewer")
                ?? throw new InvalidOperationException("Editor scroll viewer not found.")),
            "edit-entity-dependencies.png");

        viewModel.Editor.RequestArchiveCommand.Execute(null);
        await renderer.CaptureAsync("archive-entity-confirmation.png");
        viewModel.Editor.CancelArchiveCommand.Execute(null);
        viewModel.Editor.CancelCommand.Execute(null);
    }

    private static async Task PopulateManualCreationAsync(
        MainWindowViewModel viewModel,
        CancellationToken cancellationToken)
    {
        viewModel.ManualCreation.EntityName = "shipment_schedule";
        viewModel.ManualCreation.DeveloperPicker!.Choices
            .Single(choice => choice.Developer.Initials == "PT").IsSelected = true;
        viewModel.ManualCreation.GroupName = "Operations";
        viewModel.ManualCreation.SelectedRequestedPriority = 2;

        viewModel.ManualCreation.DependencyQuery = "time_zone";
        await viewModel.ManualCreation.SearchDependenciesAsync(cancellationToken);
        ManualDependencySuggestion existing = viewModel.ManualCreation.Suggestions
            .Single(static suggestion => suggestion.SourceName == "time_zone");
        viewModel.ManualCreation.AddExistingCommand.Execute(existing);

        viewModel.ManualCreation.DependencyQuery = "future_carrier_feed";
        await viewModel.ManualCreation.SearchDependenciesAsync(cancellationToken);
        if (!viewModel.ManualCreation.CanAddAsUnresolved)
        {
            throw new InvalidDataException(
                "The deterministic Add Entity screenshot could not stage an unresolved dependency.");
        }

        viewModel.ManualCreation.AddUnresolvedCommand.Execute(null);
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

        ProjectDeveloper platform = (await provider.GetRequiredService<ProjectDeveloperService>()
                .ListAsync(tracker.ProjectId, cancellationToken))
            .Single(developer => developer.Initials == "PT");
        await provider.GetRequiredService<ITrackedStateStore>().ApplyAsync(tracker.Id,
            new TrackedStateChangeSet([], [], [], [], [], [],
                responsibilitySelections: [new ResponsibilitySelection(leaf.Id, [platform.Id])]),
            cancellationToken);

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
        await WaitUntilAsync(() => viewModel.SelectedEntityDetails?.ResponsibilityTimeline.Count > 0,
            "Archived responsibility details did not load.", cancellationToken);
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

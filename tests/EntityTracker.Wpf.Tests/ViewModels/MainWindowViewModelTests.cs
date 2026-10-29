using System.IO;
using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Planning;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Reporting;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task OverviewExport_UsesScopeAndReportsCompletionOrFailure()
    {
        string directory = Path.Combine(Path.GetTempPath(), "EntityTracker.ExportCommandTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            EntityTrackerSettingsStore store = new(Path.Combine(directory, "settings.json"));
            OverviewExportSettingsViewModel settings = new(store, (await store.LoadAsync()).Settings);
            NotificationCenter notifications = new(displayTime: TimeSpan.FromMinutes(1));
            StubOverviewExportFilePicker picker = new(Path.Combine(directory, "overview.csv"));
            MainWindowViewModel viewModel = CreateViewModel(
                [Entity(1, "First"), Entity(2, "Second")], [], out _, overviewExportService: new OverviewExportService(),
                overviewExportFilePicker: picker, overviewExportSettings: settings,
                notifications: notifications);
            await viewModel.InitializeAsync();
            viewModel.OverviewSearchQuery = "First";
            using (CancellationTokenSource searchTimeout = new(TimeSpan.FromSeconds(5)))
                while (viewModel.OverviewItems.Count != 1)
                    await Task.Delay(10, searchTimeout.Token);
            viewModel.ExportOverviewCommand.Execute(OverviewExportFormat.Csv);
            await WaitForExportAsync(notifications);
            Assert.Equal(NotificationKind.Success, notifications.Items.Last().Kind);
            Assert.Equal(2, File.ReadAllLines(picker.Path!).Length);

            await settings.SetRowsAsync(OverviewExportRows.AllActiveEntities);
            viewModel.ExportOverviewCommand.Execute(OverviewExportFormat.Csv);
            await WaitForExportAsync(notifications);
            Assert.Equal(3, File.ReadAllLines(picker.Path!).Length);

            picker.Path = Path.Combine(directory, "missing", "overview.csv");
            viewModel.ExportOverviewCommand.Execute(OverviewExportFormat.Csv);
            await WaitForExportAsync(notifications);
            Assert.Equal(NotificationKind.Failure, notifications.Items.Last().Kind);

            int noticeCount = notifications.Items.Count;
            picker.Path = null;
            viewModel.ExportOverviewCommand.Execute(OverviewExportFormat.Csv);
            Assert.Equal(noticeCount, notifications.Items.Count);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task WaitForExportAsync(NotificationCenter notifications)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (notifications.Items.Count == 0 ||
               notifications.Items.Last().Kind == NotificationKind.Progress)
            await Task.Delay(10, timeout.Token);
    }

    private sealed class StubOverviewExportFilePicker(string path) : IOverviewExportFilePicker
    {
        public string? Path { get; set; } = path;
        public string? SelectPath(OverviewExportFormat format, string suggestedFileName) => Path;
    }

    [Fact]
    public async Task InitializeAsync_PopulatesRankedRowsAndProgressSummary()
    {
        TrackedEntity foundation = Entity(1, "Foundation", DevelopmentStatus.DevelopmentCompleted, "Ready");
        TrackedEntity service = Entity(2, "Service", DevelopmentStatus.InProgress);
        MainWindowViewModel viewModel = CreateViewModel(
            [service, foundation],
            [Dependency(service, foundation)],
            out _);

        await viewModel.InitializeAsync();

        Assert.Equal(
            ["Foundation", "Service"],
            viewModel.OverviewItems.Select(static item => item.SourceName));
        Assert.Equal(["1", "2"], viewModel.OverviewItems.Select(static item => item.Rank));
        Assert.Equal(2, viewModel.TotalEntityCount);
        Assert.Equal(0, viewModel.NotStartedCount);
        Assert.Equal(1, viewModel.InProgressCount);
        Assert.Equal(1, viewModel.DevelopmentCompletedCount);
        Assert.Equal(50, viewModel.ImplementedPercentage);
        Assert.Equal(0, viewModel.ReconciledCount);
    }

    [Fact]
    public async Task ReworkNeeded_IsImplementedAndHasItsOwnOverviewPresentation()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            [Entity(1, "NeedsReview", DevelopmentStatus.ReworkNeeded)],
            [],
            out _);

        await viewModel.InitializeAsync();

        Assert.Equal(1, viewModel.ReworkNeededCount);
        Assert.Equal(100, viewModel.ImplementedPercentage);
        Assert.Equal(100, viewModel.ReworkNeededPercentage);
        EntityOverviewRow row = Assert.Single(viewModel.OverviewItems);
        Assert.Equal("Rework needed", row.Status);
        Assert.Equal("Ready", row.WorkStatus);
        viewModel.ActiveTable.SetSingleStatusFilter(DevelopmentStatus.ReworkNeeded);
        Assert.Single(viewModel.OverviewItems);
        Assert.Contains(
            viewModel.Editor.StatusOptions,
            static option => option.Value == DevelopmentStatus.ReworkNeeded);
    }

    [Fact]
    public async Task NotRelevantEntitiesAreListedButLeftOutOfProgress()
    {
        // #73: counted on their own card, out of "% implemented", and blocking nothing.
        TrackedEntity completed = Entity(1, "Completed", DevelopmentStatus.DevelopmentCompleted);
        TrackedEntity waiting = Entity(2, "Waiting");
        TrackedEntity notRelevant = Entity(3, "Legacy fax number", DevelopmentStatus.NotRelevant);
        MainWindowViewModel viewModel = CreateViewModel(
            [completed, waiting, notRelevant],
            [new PersistedDependency(new DependencyEdge(waiting.Id, notRelevant.Id))],
            out _);

        await viewModel.InitializeAsync();

        Assert.Equal(3, viewModel.TotalEntityCount);
        Assert.Equal(1, viewModel.NotRelevantCount);
        Assert.Equal(50, viewModel.ImplementedPercentage);
        viewModel.ActiveTable.SetSingleStatusFilter(DevelopmentStatus.NotRelevant);
        Assert.Equal("Legacy fax number", Assert.Single(viewModel.OverviewItems).SourceName);
        Assert.True(viewModel.IsNotRelevantSummarySelected);
        viewModel.ActiveTable.ClearAllFiltersAndSort();
        ApplySingleFilter(viewModel.ActiveTable.WorkStatusFilter!, "Ready");
        Assert.Equal("Waiting", Assert.Single(viewModel.OverviewItems).SourceName);
        Assert.Contains(viewModel.BulkStatusOptions, static option => option.Value == DevelopmentStatus.NotRelevant);
        Assert.Contains(viewModel.BulkEdit.StatusOptions, static option => option.Value == DevelopmentStatus.NotRelevant);
        Assert.Contains(viewModel.Editor.StatusOptions, static option => option.Value == DevelopmentStatus.NotRelevant);
    }

    [Fact]
    public async Task ColumnFilters_ComposeAndArchivedEntitiesStayOutsideActiveProgress()
    {
        TrackedEntity ready = Entity(1, "Ready");
        TrackedEntity blocked = Entity(2, "Blocked");
        TrackedEntity inProgress = Entity(3, "InProgress", DevelopmentStatus.InProgress);
        TrackedEntity developmentCompleted = Entity(
            4,
            "DevelopmentCompleted",
            DevelopmentStatus.DevelopmentCompleted);
        TrackedEntity reconciled = Entity(5, "Reconciled", DevelopmentStatus.Reconciled);
        TrackedEntity archived = Entity(
            6,
            "Archived",
            DevelopmentStatus.Reconciled,
            lifecycle: EntityLifecycleState.Archived);
        MainWindowViewModel viewModel = CreateViewModel(
            [ready, blocked, inProgress, developmentCompleted, reconciled, archived],
            [],
            out _,
            [Unresolved(blocked, "Missing")]);

        await viewModel.InitializeAsync();

        Assert.Equal(5, viewModel.TotalEntityCount);
        Assert.Equal(1, viewModel.ArchivedEntityCount);
        Assert.Equal(40, viewModel.ImplementedPercentage);
        Assert.Equal(20, viewModel.ReconciledPercentage);

        ApplySingleFilter(viewModel.ActiveTable.WorkStatusFilter!, "Ready");
        Assert.Equal("Ready", Assert.Single(viewModel.OverviewItems).SourceName);
        ApplySingleFilter(viewModel.ActiveTable.WorkStatusFilter!, "Waiting on dependencies");
        Assert.Equal("Blocked", Assert.Single(viewModel.OverviewItems).SourceName);
        viewModel.ActiveTable.ClearAllFiltersAndSort();
        viewModel.ActiveTable.SetSingleStatusFilter(DevelopmentStatus.InProgress);
        Assert.Equal("InProgress", Assert.Single(viewModel.OverviewItems).SourceName);
        viewModel.ActiveTable.SetSingleStatusFilter(DevelopmentStatus.DevelopmentCompleted);
        Assert.Equal("DevelopmentCompleted", Assert.Single(viewModel.OverviewItems).SourceName);
        viewModel.ActiveTable.SetSingleStatusFilter(DevelopmentStatus.Reconciled);
        Assert.Equal("Reconciled", Assert.Single(viewModel.OverviewItems).SourceName);

        EntityOverviewRow archivedRow = Assert.Single(viewModel.ArchivedItems);
        Assert.Equal("Archived", archivedRow.SourceName);
        Assert.False(archivedRow.HasGraphIssue);
        Assert.Empty(archivedRow.GraphIssueTitle);
    }

    [Fact]
    public async Task StatusSummaryFilterCommand_FiltersByPersistedStatusAndTotalShowsAllActive()
    {
        TrackedEntity ready = Entity(1, "Ready");
        TrackedEntity blocked = Entity(2, "Blocked");
        TrackedEntity inProgress = Entity(3, "InProgress", DevelopmentStatus.InProgress);
        TrackedEntity rework = Entity(4, "Rework", DevelopmentStatus.ReworkNeeded);
        TrackedEntity developmentCompleted = Entity(
            5,
            "DevelopmentCompleted",
            DevelopmentStatus.DevelopmentCompleted);
        TrackedEntity reconciled = Entity(6, "Reconciled", DevelopmentStatus.Reconciled);
        TrackedEntity archived = Entity(
            7,
            "Archived",
            lifecycle: EntityLifecycleState.Archived);
        MainWindowViewModel viewModel = CreateViewModel(
            [ready, blocked, inProgress, rework, developmentCompleted, reconciled, archived],
            [],
            out _,
            [Unresolved(blocked, "Missing")]);

        await viewModel.InitializeAsync();

        viewModel.SelectOverviewStatusCommand.Execute(DevelopmentStatus.NotStarted);

        Assert.True(viewModel.IsNotStartedSummarySelected);
        Assert.Equal(
            ["Blocked", "Ready"],
            viewModel.OverviewItems
                .Select(static row => row.SourceName)
                .OrderBy(static name => name));

        AssertStatusFilter(viewModel, DevelopmentStatus.InProgress, "InProgress");
        AssertStatusFilter(viewModel, DevelopmentStatus.ReworkNeeded, "Rework");
        AssertStatusFilter(
            viewModel,
            DevelopmentStatus.DevelopmentCompleted,
            "DevelopmentCompleted");
        AssertStatusFilter(viewModel, DevelopmentStatus.Reconciled, "Reconciled");

        viewModel.ActiveTable.ClearAllFiltersCommand.Execute(null);

        Assert.True(viewModel.IsTotalSummarySelected);
        Assert.Equal(6, viewModel.OverviewItems.Count);
        Assert.DoesNotContain(viewModel.OverviewItems, row => row.SourceName == "Archived");
    }

    [Fact]
    public async Task StatusSummaryFilterCommand_CanShowEmptyStatusTogglesBackToTotalAndKeepsActiveSearch()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            [Entity(1, "Matching entity"), Entity(2, "Other entity")],
            [],
            out _);
        await viewModel.InitializeAsync();

        Assert.True(viewModel.SelectOverviewStatusCommand.CanExecute(
            DevelopmentStatus.NotStarted));
        Assert.True(viewModel.SelectOverviewStatusCommand.CanExecute(
            DevelopmentStatus.Reconciled));

        viewModel.SelectOverviewStatusCommand.Execute(DevelopmentStatus.Reconciled);
        Assert.Empty(viewModel.OverviewItems);

        viewModel.ActiveTable.ClearAllFiltersAndSort();
        viewModel.OverviewSearchQuery = "Matching";
        await WaitUntilAsync(() => viewModel.OverviewItems.Count == 1);
        viewModel.ActiveTable.SetSort(OverviewColumnKey.WorkStatus, OverviewSortDirection.Descending);
        viewModel.SelectOverviewStatusCommand.Execute(DevelopmentStatus.NotStarted);

        Assert.True(viewModel.IsNotStartedSummarySelected);
        Assert.False(viewModel.IsTotalSummarySelected);
        Assert.Equal("Matching entity", Assert.Single(viewModel.OverviewItems).SourceName);

        // Clicking the selected card again deselects it and returns to Total, keeping the search and sort.
        viewModel.SelectOverviewStatusCommand.Execute(DevelopmentStatus.NotStarted);

        Assert.False(viewModel.IsNotStartedSummarySelected);
        Assert.True(viewModel.IsTotalSummarySelected);
        Assert.True(viewModel.ActiveTable.HasSort);
        Assert.Equal("Matching", viewModel.OverviewSearchQuery);
        Assert.Equal("Matching entity", Assert.Single(viewModel.OverviewItems).SourceName);

        viewModel.SelectOverviewStatusCommand.Execute(DevelopmentStatus.Reconciled);
        viewModel.ActiveTable.ClearAllFiltersCommand.Execute(null);

        Assert.True(viewModel.IsTotalSummarySelected);
        Assert.Equal(string.Empty, viewModel.OverviewSearchQuery);
        Assert.Equal(2, viewModel.OverviewItems.Count);
    }

    [Fact]
    public async Task ImplementedCard_FiltersToWhatItCountsAndTogglesBackToTotal()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            [
                Entity(1, "Completed", DevelopmentStatus.DevelopmentCompleted),
                Entity(2, "Reconciled", DevelopmentStatus.Reconciled),
                Entity(3, "Rework", DevelopmentStatus.ReworkNeeded),
                Entity(4, "Reworking", DevelopmentStatus.Reworking),
                Entity(5, "In progress", DevelopmentStatus.InProgress),
                Entity(6, "Not started")
            ],
            [],
            out _);
        await viewModel.InitializeAsync();
        viewModel.ActiveTable.SetSort(OverviewColumnKey.WorkStatus, OverviewSortDirection.Descending);

        viewModel.SelectImplementedSummaryCommand.Execute(null);

        Assert.True(viewModel.IsImplementedSummarySelected);
        Assert.False(viewModel.IsTotalSummarySelected);
        Assert.Equal(
            ["Completed", "Reconciled", "Rework", "Reworking"],
            viewModel.OverviewItems.Select(static row => row.SourceName).Order());
        // The status cards it is made of show as selected too.
        Assert.True(viewModel.IsDevelopmentCompletedSummarySelected && viewModel.IsReconciledSummarySelected &&
            viewModel.IsReworkNeededSummarySelected && viewModel.IsReworkingSummarySelected);
        Assert.False(viewModel.IsInProgressSummarySelected);

        viewModel.SelectImplementedSummaryCommand.Execute(null);

        Assert.False(viewModel.IsImplementedSummarySelected);
        Assert.True(viewModel.IsTotalSummarySelected);
        Assert.True(viewModel.ActiveTable.HasSort);
        Assert.Equal(6, viewModel.OverviewItems.Count);

        // From "% implemented", a status card narrows to its own status rather than clearing the filter.
        viewModel.SelectImplementedSummaryCommand.Execute(null);
        viewModel.SelectOverviewStatusCommand.Execute(DevelopmentStatus.Reconciled);

        Assert.False(viewModel.IsImplementedSummarySelected);
        Assert.True(viewModel.IsReconciledSummarySelected);
        Assert.False(viewModel.IsDevelopmentCompletedSummarySelected);
        Assert.Equal("Reconciled", Assert.Single(viewModel.OverviewItems).SourceName);

        // Clicking it again, now the only status shown, returns to Total.
        viewModel.SelectOverviewStatusCommand.Execute(DevelopmentStatus.Reconciled);
        Assert.True(viewModel.IsTotalSummarySelected);
    }

    [Fact]
    public async Task OverviewRows_ExposeCompactDirectAndUpstreamGraphIssueDetails()
    {
        TrackedEntity resolved = Entity(1, "Resolved");
        TrackedEntity direct = Entity(2, "Direct");
        TrackedEntity upstream = Entity(3, "Upstream");
        MainWindowViewModel viewModel = CreateViewModel(
            [upstream, direct, resolved],
            [Dependency(upstream, direct)],
            out _,
            [Unresolved(direct, "Zulu Missing"), Unresolved(direct, "alpha missing")]);

        await viewModel.InitializeAsync();

        EntityOverviewRow resolvedRow = viewModel.OverviewItems.Single(row =>
            row.SourceName == resolved.SourceName);
        EntityOverviewRow directRow = viewModel.OverviewItems.Single(row =>
            row.SourceName == direct.SourceName);
        EntityOverviewRow upstreamRow = viewModel.OverviewItems.Single(row =>
            row.SourceName == upstream.SourceName);

        Assert.Equal("Ready", resolvedRow.WorkStatus);
        Assert.False(resolvedRow.HasGraphIssue);
        Assert.Empty(resolvedRow.DependencyResolutionIssueNames);

        Assert.True(directRow.HasGraphIssue);
        Assert.True(directRow.IsDirectlyUnresolved);
        Assert.Equal("Unresolved dependency", directRow.GraphIssueTitle);
        Assert.Contains("does not match an active entity", directRow.GraphIssueDescription);
        Assert.Equal(
            ["alpha missing", "Zulu Missing"],
            directRow.DependencyResolutionIssueNames);
        Assert.Equal(
            "Unresolved names affecting this entity: alpha missing, Zulu Missing",
            directRow.GraphIssueNames);

        Assert.True(upstreamRow.HasGraphIssue);
        Assert.False(upstreamRow.IsDirectlyUnresolved);
        Assert.Equal("Upstream unresolved", upstreamRow.GraphIssueTitle);
        Assert.Contains("dependency chain", upstreamRow.GraphIssueDescription);
        Assert.Equal(
            ["alpha missing", "Zulu Missing"],
            upstreamRow.DependencyResolutionIssueNames);
    }

    [Fact]
    public async Task OverviewSearch_FiltersNamesAfterDebounceAndKeepsOverallSummary()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            [
                Entity(1, "Customer Orders", DevelopmentStatus.DevelopmentCompleted),
                Entity(2, "Invoice"),
                Entity(3, "Customer Profile", DevelopmentStatus.InProgress)
            ],
            [],
            out _);
        await viewModel.InitializeAsync();
        viewModel.OpenOverviewSearchCommand.Execute(null);

        viewModel.OverviewSearchQuery = "invoice";
        viewModel.OverviewSearchQuery = "CUSTOMER";

        Assert.Equal(3, viewModel.OverviewItems.Count);
        await WaitUntilAsync(() => viewModel.OverviewItems.Count == 2);
        Assert.Equal(
            ["Customer Orders", "Customer Profile"],
            viewModel.OverviewItems.Select(static row => row.SourceName));
        Assert.Equal(3, viewModel.TotalEntityCount);
        Assert.Equal(1, viewModel.DevelopmentCompletedCount);
        Assert.Equal(1, viewModel.InProgressCount);
        Assert.Equal("Showing 2 of 3", viewModel.OverviewSearchResultSummary);
    }

    [Fact]
    public async Task OverviewSearch_NoMatchesClearCloseAndRefreshBehaveConsistently()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            [Entity(1, "Alpha"), Entity(2, "Beta")],
            [],
            out _);
        await viewModel.InitializeAsync();
        viewModel.OpenOverviewSearchCommand.Execute(null);
        viewModel.OverviewSearchQuery = "missing";
        await WaitUntilAsync(() => viewModel.ShowOverviewSearchEmptyState);

        Assert.Empty(viewModel.OverviewItems);
        Assert.False(viewModel.ShowOverviewEmptyState);
        await viewModel.RefreshAsync();
        Assert.True(viewModel.ShowOverviewSearchEmptyState);
        Assert.Empty(viewModel.OverviewItems);

        viewModel.ClearOverviewSearchCommand.Execute(null);
        Assert.Equal(2, viewModel.OverviewItems.Count);
        Assert.True(viewModel.IsOverviewSearchOpen);
        viewModel.CloseOverviewSearchCommand.Execute(null);
        Assert.False(viewModel.IsOverviewSearchOpen);
        Assert.Empty(viewModel.OverviewSearchQuery);
    }

    [Fact]
    public void ActiveAndArchivedTables_HaveIndependentSearchState()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            [], [], out _);

        viewModel.OpenOverviewSearchCommand.Execute(null);
        Assert.True(viewModel.IsOverviewSearchOpen);
        Assert.False(viewModel.ArchivedTable.IsSearchOpen);

        viewModel.SelectedTab = MainWindowTab.Archived;
        viewModel.ArchivedTable.OpenSearchCommand.Execute(null);

        Assert.True(viewModel.IsOverviewSearchOpen);
        Assert.True(viewModel.ArchivedTable.IsSearchOpen);
    }

    [Fact]
    public async Task EditOverviewEntityCommand_OpensStandaloneModalWithoutChangingTab()
    {
        TrackedEntity entity = Entity(1, "Customer");
        MainWindowViewModel viewModel = CreateViewModel(
            [entity],
            [],
            out _);
        await viewModel.InitializeAsync();

        viewModel.EditOverviewEntityCommand.Execute(Assert.Single(viewModel.OverviewItems));

        Assert.True(viewModel.Editor.IsOpen);
        Assert.Equal(EntityEditorMode.Standalone, viewModel.Editor.Mode);
        Assert.True(viewModel.Editor.CanArchive);
        Assert.Equal("Customer", viewModel.Editor.SelectedEntityName);
        Assert.Equal(MainWindowTab.Overview, viewModel.SelectedTab);
    }

    [Fact]
    public async Task EntityDetails_EditActionOpensSelectedEntityInEditor()
    {
        TrackedEntity entity = Entity(1, "Customer");
        MainWindowViewModel viewModel = CreateViewModel(
            [entity],
            [],
            out _);
        await viewModel.InitializeAsync();
        EntityOverviewRow row = Assert.Single(viewModel.OverviewItems);
        viewModel.OpenEntityDetailsCommand.Execute(row);
        EntityDetailsViewModel details = Assert.IsType<EntityDetailsViewModel>(
            viewModel.SelectedEntityDetails);

        Assert.True(details.CanEdit);
        Assert.Same(row, details.OverviewRow);
        viewModel.EditOverviewEntityCommand.Execute(details.OverviewRow);

        await WaitUntilAsync(() => viewModel.Editor.IsOpen);
        Assert.False(viewModel.IsEntityDetailsOpen);
        Assert.Equal("Customer", viewModel.Editor.SelectedEntityName);
    }

    [Fact]
    public async Task EntityDetails_AreReadOnlyPreserveSelectionAndCloseOnProjectionChange()
    {
        TrackedEntity dependency = Entity(1, "Foundation");
        TrackedEntity owner = Entity(
            2,
            "Customer",
            notes: "Full implementation notes",
            requestedPriority: 2,
            responsibleDeveloper: "Alice",
            groupName: "Billing");
        MainWindowViewModel viewModel = CreateViewModel(
            [owner, dependency],
            [Dependency(owner, dependency)],
            out _);
        await viewModel.InitializeAsync();
        EntityOverviewRow row = viewModel.OverviewItems.Single(item => item.EntityId == owner.Id);
        viewModel.UpdateOverviewSelection([row]);

        viewModel.OpenEntityDetailsCommand.Execute(row);

        EntityDetailsViewModel details = Assert.IsType<EntityDetailsViewModel>(
            viewModel.SelectedEntityDetails);
        Assert.True(viewModel.IsEntityDetailsOpen);
        Assert.Equal(1, viewModel.SelectedActiveEntityCount);
        Assert.Equal("Customer", details.SourceName);
        Assert.Equal("2", details.RequestedPriority);
        Assert.Equal("Full implementation notes", details.Notes);
        Assert.Equal("Alice", details.ResponsibleDeveloper);
        Assert.Equal("Billing", details.GroupName);
        Assert.Equal("Foundation", Assert.Single(details.Dependencies).Name);
        Assert.Equal("Foundation", Assert.Single(details.Blockers).Name);

        viewModel.ShowFullResponsibilityHistoryCommand.Execute(null);
        Assert.True(details.IsFullHistoryOpen);
        viewModel.BackFromResponsibilityHistoryCommand.Execute(null);
        await WaitUntilAsync(() => details.IsSummaryOpen);
        Assert.Same(details, viewModel.SelectedEntityDetails);

        viewModel.CloseEntityDetailsCommand.Execute(null);
        Assert.False(viewModel.IsEntityDetailsOpen);
        Assert.Equal(1, viewModel.SelectedActiveEntityCount);

        viewModel.OpenEntityDetailsCommand.Execute(row);
        viewModel.OverviewSearchQuery = "Customer";
        Assert.False(viewModel.IsEntityDetailsOpen);
        Assert.Equal(0, viewModel.SelectedActiveEntityCount);
    }

    [Fact]
    public async Task SearchPreferenceChangeClearsBulkSelectionBeforeReprojection()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            [Entity(1, "Invoice")], [], out _);
        await viewModel.InitializeAsync();
        viewModel.UpdateOverviewSelection([Assert.Single(viewModel.OverviewItems)]);
        Assert.Equal(1, viewModel.SelectedActiveEntityCount);

        viewModel.SetSearchResponsibleNames(false);

        Assert.Equal(0, viewModel.SelectedActiveEntityCount);
        Assert.False(viewModel.ActiveTable.SearchResponsibleNames);
        Assert.False(viewModel.ArchivedTable.SearchResponsibleNames);
    }


    [Fact]
    public async Task ArchivedDetails_UsePreservedContextAndRestoreRemainsInExistingEditor()
    {
        TrackedEntity target = Entity(1, "Legacy target");
        TrackedEntity archived = Entity(
            2,
            "Legacy owner",
            notes: "Preserved notes",
            lifecycle: EntityLifecycleState.Archived,
            requestedPriority: 4);
        MainWindowViewModel viewModel = CreateViewModel(
            [target, archived],
            [Dependency(archived, target)],
            out _);
        await viewModel.InitializeAsync();
        viewModel.SelectedTab = MainWindowTab.Archived;
        EntityOverviewRow row = Assert.Single(viewModel.ArchivedItems);

        viewModel.OpenEntityDetailsCommand.Execute(row);

        EntityDetailsViewModel details = Assert.IsType<EntityDetailsViewModel>(
            viewModel.SelectedEntityDetails);
        Assert.True(details.IsArchived);
        Assert.False(details.CanEdit);
        Assert.Equal("4", details.RequestedPriority);
        Assert.Equal("Not applicable while archived", details.EffectivePriority);
        Assert.Equal("Legacy target", Assert.Single(details.Dependencies).Name);
        Assert.False(viewModel.Editor.IsOpen);

        viewModel.EditOverviewEntityCommand.Execute(row);
        await WaitUntilAsync(() => viewModel.Editor.IsOpen);
        Assert.False(viewModel.IsEntityDetailsOpen);
        Assert.True(viewModel.Editor.CanRestoreEntity);

        viewModel.ShowArchivedResponsibilityHistoryCommand.Execute(null);
        Assert.False(viewModel.Editor.IsOpen);
        Assert.True(Assert.IsType<EntityDetailsViewModel>(viewModel.SelectedEntityDetails).IsFullHistoryOpen);
        viewModel.BackFromResponsibilityHistoryCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.Editor.IsOpen);
        Assert.True(viewModel.Editor.IsArchivedMode);
        Assert.False(viewModel.IsEntityDetailsOpen);
    }

    [Fact]
    public async Task StandaloneEditor_SavesStatusNotesAndDependenciesAsOneRefresh()
    {
        TrackedEntity entity = Entity(1, "Customer");
        MainWindowViewModel viewModel = CreateViewModel(
            [entity],
            [],
            out StubStateStore store);
        await viewModel.InitializeAsync();
        viewModel.EditOverviewEntityCommand.Execute(Assert.Single(viewModel.OverviewItems));
        await WaitUntilAsync(() => viewModel.Editor.IsOpen && !viewModel.Editor.IsBusy);

        viewModel.Editor.SelectedStatus = DevelopmentStatus.Reconciled;
        viewModel.Editor.EditedNotes = "Verified implementation";
        viewModel.Editor.EditedResponsibleDeveloper = "  Platform Team  ";
        viewModel.Editor.EditedGroupName = "  Core Data  ";
        viewModel.Editor.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.Editor.IsOpen);

        TrackedEntity progress = Assert.Single(
            store.AppliedChangeSet!.EntitiesWithProgressToUpdate);
        Assert.Equal(DevelopmentStatus.Reconciled, progress.Status);
        Assert.Equal("Verified implementation", progress.Notes);
        TrackedEntity assignment = Assert.Single(
            store.AppliedChangeSet.EntitiesWithResponsibleDeveloperToUpdate);
        Assert.Equal("Platform Team", assignment.ResponsibleDeveloper);
        TrackedEntity groupUpdate = Assert.Single(
            store.AppliedChangeSet.EntitiesWithGroupNameToUpdate);
        Assert.Equal("Core Data", groupUpdate.GroupName);
        EntityOverviewRow row = Assert.Single(viewModel.OverviewItems);
        Assert.Equal("Reconciled", row.Status);
        Assert.Equal("Verified implementation", row.Notes);
        Assert.Equal("Platform Team", row.ResponsibleDeveloper);
        Assert.Equal("Core Data", row.GroupName);
        Assert.Equal(100, viewModel.ImplementedPercentage);
        Assert.Equal(100, viewModel.ReconciledPercentage);
    }

    [Fact]
    public async Task StandaloneEditor_SavesInternalAndSharedNotesSeparately()
    {
        TrackedEntity entity = Entity(1, "Customer");
        MainWindowViewModel viewModel = CreateViewModel(
            [entity],
            [],
            out StubStateStore store);
        await viewModel.InitializeAsync();
        viewModel.EditOverviewEntityCommand.Execute(Assert.Single(viewModel.OverviewItems));
        await WaitUntilAsync(() => viewModel.Editor.IsOpen && !viewModel.Editor.IsBusy);

        viewModel.Editor.EditedNotes = "Internal: vendor contract";
        viewModel.Editor.EditedSharedNotes = "Migrates in June";
        Assert.True(viewModel.Editor.IsDirty);
        viewModel.Editor.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.Editor.IsOpen);

        TrackedEntity progress = Assert.Single(store.AppliedChangeSet!.EntitiesWithProgressToUpdate);
        Assert.Equal("Internal: vendor contract", progress.Notes);
        Assert.Equal("Migrates in June", progress.SharedNotes);
        EntityOverviewRow row = Assert.Single(viewModel.OverviewItems);
        Assert.Equal("Migrates in June", row.SharedNotes);
        Assert.Equal("Internal: vendor contract", row.Notes);
    }

    [Fact]
    public async Task TryCloseEditor_ProtectsDirtyWorkButClosesUnchangedEditorImmediately()
    {
        TrackedEntity entity = Entity(1, "Customer");
        RecordingDiscardConfirmation discard = new(false);
        MainWindowViewModel viewModel = CreateViewModel(
            [entity],
            [],
            out _,
            discardConfirmation: discard);
        await viewModel.InitializeAsync();
        EntityOverviewRow row = Assert.Single(viewModel.OverviewItems);

        viewModel.EditOverviewEntityCommand.Execute(row);
        await WaitUntilAsync(() => viewModel.Editor.IsOpen && !viewModel.Editor.IsBusy);
        Assert.True(viewModel.TryCloseEditor());
        Assert.False(viewModel.Editor.IsOpen);
        Assert.Equal(0, discard.CallCount);

        viewModel.EditOverviewEntityCommand.Execute(row);
        await WaitUntilAsync(() => viewModel.Editor.IsOpen && !viewModel.Editor.IsBusy);
        viewModel.Editor.EditedNotes = "Unsaved note";

        Assert.False(viewModel.TryCloseEditor());
        Assert.True(viewModel.Editor.IsOpen);
        Assert.Equal("Unsaved note", viewModel.Editor.EditedNotes);
        Assert.Equal(1, discard.CallCount);

        discard.Result = true;
        Assert.True(viewModel.TryCloseEditor());
        Assert.False(viewModel.Editor.IsOpen);
        Assert.Equal(2, discard.CallCount);
    }

    [Fact]
    public async Task Overview_ShowsEffectivePriorityAndKeepsPrerequisiteBeforeTarget()
    {
        TrackedEntity prerequisite = Entity(1, "ZuluPrerequisite");
        TrackedEntity target = Entity(
            2,
            "AlphaTarget",
            requestedPriority: 1,
            responsibleDeveloper: "Core Team");
        TrackedEntity unrelated = Entity(3, "Unrelated");
        MainWindowViewModel viewModel = CreateViewModel(
            [unrelated, target, prerequisite],
            [Dependency(target, prerequisite)],
            out _);

        await viewModel.InitializeAsync();

        Assert.Equal(
            ["ZuluPrerequisite", "AlphaTarget", "Unrelated"],
            viewModel.OverviewItems.Select(static item => item.SourceName));
        Assert.Equal(["1", "1", "—"], viewModel.OverviewItems.Select(
            static item => item.Priority));
        Assert.Equal(
            ["—", "Core Team", "—"],
            viewModel.OverviewItems.Select(static item => item.ResponsibleDeveloperDisplay));
    }

    [Fact]
    public async Task StandaloneEditor_GroupSuggestionsIncludeArchivedGroupsAndUseCanonicalCasing()
    {
        TrackedEntity owner = Entity(1, "Owner");
        TrackedEntity archived = Entity(
            2,
            "Archived",
            lifecycle: EntityLifecycleState.Archived,
            groupName: "Legacy Data");
        MainWindowViewModel viewModel = CreateViewModel(
            [owner, archived],
            [],
            out _);
        await viewModel.InitializeAsync();
        viewModel.EditOverviewEntityCommand.Execute(Assert.Single(viewModel.OverviewItems));
        await WaitUntilAsync(() => viewModel.Editor.IsOpen && !viewModel.Editor.IsBusy);

        viewModel.Editor.EditedGroupName = "legacy";
        await WaitUntilAsync(() => viewModel.Editor.HasGroupSuggestions);
        string suggestion = Assert.Single(viewModel.Editor.GroupSuggestions);
        viewModel.Editor.UseGroupSuggestionCommand.Execute(suggestion);

        Assert.Equal("Legacy Data", viewModel.Editor.EditedGroupName);
        Assert.Empty(viewModel.Editor.GroupSuggestions);
    }

    [Fact]
    public async Task StandaloneEditor_PreviewsAndSavesRequestedPriority()
    {
        TrackedEntity prerequisite = Entity(1, "Prerequisite");
        TrackedEntity owner = Entity(2, "Owner");
        MainWindowViewModel viewModel = CreateViewModel(
            [owner, prerequisite],
            [Dependency(owner, prerequisite)],
            out StubStateStore store,
            [Unresolved(prerequisite, "Missing")]);
        await viewModel.InitializeAsync();
        EntityOverviewRow ownerRow = viewModel.OverviewItems.Single(item => item.EntityId == owner.Id);
        viewModel.EditOverviewEntityCommand.Execute(ownerRow);
        await WaitUntilAsync(() => viewModel.Editor.IsOpen && !viewModel.Editor.IsBusy);

        viewModel.Editor.SelectedRequestedPriority = 2;

        Assert.True(viewModel.Editor.HasPendingPriorityChange);
        Assert.Equal("2", viewModel.Editor.EffectivePriority);
        Assert.Equal(
            ["Prerequisite", "Owner"],
            viewModel.Editor.PriorityPreviewRows.Select(static item => item.SourceName));
        Assert.Equal(["Missing"], viewModel.Editor.PriorityUnresolvedDependencyNames);

        viewModel.Editor.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.Editor.IsOpen);

        TrackedEntity update = Assert.Single(
            store.AppliedChangeSet!.EntitiesWithRequestedPriorityToUpdate);
        Assert.Equal(2, update.RequestedPriority);
        Assert.Equal(["2", "2"], viewModel.OverviewItems.Select(static item => item.Priority));
    }

    [Fact]
    public async Task StandaloneEditor_SavesDueDateAndTheDueColumnFollowsTheInheritedSetting()
    {
        TrackedEntity prerequisite = Entity(1, "Prerequisite");
        TrackedEntity owner = Entity(2, "Owner");
        MainWindowViewModel viewModel = CreateViewModel(
            [owner, prerequisite],
            [Dependency(owner, prerequisite)],
            out StubStateStore store);
        await viewModel.InitializeAsync();
        EntityOverviewRow ownerRow = viewModel.OverviewItems.Single(item => item.EntityId == owner.Id);
        viewModel.EditOverviewEntityCommand.Execute(ownerRow);
        await WaitUntilAsync(() => viewModel.Editor.IsOpen && !viewModel.Editor.IsBusy);
        Assert.Null(viewModel.Editor.SelectedDueDate);
        Assert.False(viewModel.Editor.IsDirty);

        viewModel.Editor.SelectedDueDate = new DateTime(2026, 11, 5, 14, 30, 0);

        Assert.True(viewModel.Editor.IsDirty);
        viewModel.Editor.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.Editor.IsOpen &&
            viewModel.OverviewItems.Any(item => item.Due == "05-11-2026"));

        TrackedEntity update = Assert.Single(store.AppliedChangeSet!.EntitiesWithDueDateToUpdate);
        Assert.Equal(new DateOnly(2026, 11, 5), update.DueDate);
        Assert.Empty(store.AppliedChangeSet.EntitiesWithRequestedPriorityToUpdate);
        Assert.Equal(["Prerequisite", "Owner"], viewModel.OverviewItems.Select(static item => item.SourceName));
        Assert.Equal(["—", "05-11-2026"], viewModel.OverviewItems.Select(static item => item.Due));

        viewModel.SetShowInheritedDueDates(true);
        Assert.Equal(["05-11-2026", "05-11-2026"], viewModel.OverviewItems.Select(static item => item.Due));

        viewModel.SetShowInheritedDueDates(false);
        Assert.Equal(["—", "05-11-2026"], viewModel.OverviewItems.Select(static item => item.Due));
    }

    [Fact]
    public async Task ArchivedDetails_CancelDoesNothingAndRestoreReusesSameEntity()
    {
        TrackedEntity archived = Entity(
            1,
            "Legacy",
            DevelopmentStatus.Reconciled,
            "Keep notes",
            lifecycle: EntityLifecycleState.Archived,
            requestedPriority: 4,
            responsibleDeveloper: "Legacy Team",
            groupName: "Legacy Data");
        MainWindowViewModel viewModel = CreateViewModel(
            [archived],
            [],
            out StubStateStore store);
        await viewModel.InitializeAsync();
        viewModel.SelectedTab = MainWindowTab.Archived;

        EntityOverviewRow archivedRow = Assert.Single(viewModel.ArchivedItems);
        Assert.Equal("Legacy Team", archivedRow.ResponsibleDeveloper);
        Assert.Equal("Legacy Data", archivedRow.GroupName);
        viewModel.EditOverviewEntityCommand.Execute(archivedRow);
        await WaitUntilAsync(() => viewModel.Editor.IsOpen && !viewModel.Editor.IsBusy);
        Assert.True(viewModel.Editor.IsArchivedMode);
        Assert.False(viewModel.Editor.CanEditProgress);
        Assert.False(viewModel.Editor.CanEditPriority);
        Assert.Equal(4, viewModel.Editor.SelectedRequestedPriority);
        Assert.Equal("Legacy Team", viewModel.Editor.EditedResponsibleDeveloper);
        Assert.Equal("Legacy Data", viewModel.Editor.EditedGroupName);
        Assert.Equal("—", viewModel.Editor.EffectivePriority);
        viewModel.Editor.SelectedRequestedPriority = 1;
        viewModel.Editor.EditedResponsibleDeveloper = "Changed Team";
        viewModel.Editor.EditedGroupName = "Changed Group";
        Assert.Equal(4, viewModel.Editor.SelectedRequestedPriority);
        Assert.Equal("Legacy Team", viewModel.Editor.EditedResponsibleDeveloper);
        Assert.Equal("Legacy Data", viewModel.Editor.EditedGroupName);
        Assert.False(viewModel.Editor.ShowSave);
        Assert.True(viewModel.Editor.CanRestoreEntity);

        viewModel.Editor.CancelCommand.Execute(null);
        Assert.False(viewModel.Editor.IsOpen);
        Assert.Equal(0, store.ApplyCount);

        viewModel.ActiveTable.SetSingleStatusFilter(DevelopmentStatus.NotStarted);
        viewModel.OverviewSearchQuery = "does not match restored entity";
        viewModel.EditOverviewEntityCommand.Execute(Assert.Single(viewModel.ArchivedItems));
        await WaitUntilAsync(() => viewModel.Editor.CanRestoreEntity);
        viewModel.Editor.RestoreEntityCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.Editor.IsOpen);

        Assert.Equal(MainWindowTab.Overview, viewModel.SelectedTab);
        Assert.Empty(viewModel.ArchivedItems);
        Assert.False(viewModel.ActiveTable.HasFiltersOrSort);
        Assert.Empty(viewModel.OverviewSearchQuery);
        EntityOverviewRow restored = Assert.Single(viewModel.OverviewItems);
        Assert.Equal(archived.Id, restored.EntityId);
        Assert.Equal("Reconciled", restored.Status);
        Assert.Equal("Keep notes", restored.Notes);
        Assert.Equal("Legacy Team", restored.ResponsibleDeveloper);
        Assert.Equal("Legacy Data", restored.GroupName);
        Assert.Equal(archived.Id, Assert.Single(store.AppliedChangeSet!.EntityIdsToRestore));
    }

    [Fact]
    public async Task ArchiveConfirmation_CancelKeepsEntityAndEditorOpenWithoutWriting()
    {
        TrackedEntity entity = Entity(1, "Customer");
        MainWindowViewModel viewModel = CreateViewModel(
            [entity],
            [],
            out StubStateStore store);
        await viewModel.InitializeAsync();
        viewModel.EditOverviewEntityCommand.Execute(Assert.Single(viewModel.OverviewItems));

        viewModel.Editor.RequestArchiveCommand.Execute(null);

        Assert.True(viewModel.Editor.IsArchiveConfirmationOpen);
        Assert.False(viewModel.Editor.SaveCommand.CanExecute(null));
        viewModel.Editor.CancelArchiveCommand.Execute(null);
        Assert.False(viewModel.Editor.IsArchiveConfirmationOpen);
        Assert.True(viewModel.Editor.IsOpen);
        Assert.True(viewModel.Editor.CanArchive);
        Assert.Equal(0, store.ApplyCount);
    }

    [Fact]
    public async Task ConfirmArchive_ArchivesEntityClosesModalAndRefreshesOverview()
    {
        TrackedEntity entity = Entity(1, "Customer");
        MainWindowViewModel viewModel = CreateViewModel(
            [entity],
            [],
            out StubStateStore store);
        await viewModel.InitializeAsync();
        viewModel.EditOverviewEntityCommand.Execute(Assert.Single(viewModel.OverviewItems));
        viewModel.Editor.RequestArchiveCommand.Execute(null);

        Assert.True(viewModel.Editor.ConfirmArchiveCommand.CanExecute(null));
        viewModel.Editor.ConfirmArchiveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.Editor.IsOpen);

        Assert.False(viewModel.Editor.IsOpen);
        Assert.False(viewModel.Editor.IsArchiveConfirmationOpen);
        Assert.Empty(viewModel.OverviewItems);
        Assert.Equal(entity.Id, Assert.Single(store.AppliedChangeSet!.EntityIdsToArchive));
    }

    [Fact]
    public async Task ConfirmArchive_StoreFailureKeepsConfirmationOpenAndPostsTheError()
    {
        TrackedEntity entity = Entity(1, "Customer");
        NotificationCenter notifications = new();
        MainWindowViewModel viewModel = CreateViewModel(
            [entity],
            [],
            out StubStateStore store,
            notifications: notifications);
        store.Exception = new InvalidOperationException("Database unavailable");
        await viewModel.InitializeAsync();
        viewModel.EditOverviewEntityCommand.Execute(Assert.Single(viewModel.OverviewItems));
        viewModel.Editor.RequestArchiveCommand.Execute(null);

        viewModel.Editor.ConfirmArchiveCommand.Execute(null);
        await WaitUntilAsync(() => notifications.HasItems);

        Assert.True(viewModel.Editor.IsOpen);
        Assert.True(viewModel.Editor.IsArchiveConfirmationOpen);
        NotificationItem notice = Assert.Single(notifications.Items);
        Assert.Equal(("Archive entity", NotificationKind.Failure), (notice.Title, notice.Kind));
        Assert.Contains("Database unavailable", notice.Message);
        Assert.Single(viewModel.OverviewItems);
    }

    [Fact]
    public async Task ManualCreation_SuccessClosesTheOverlayAndRevealsTheRowOnOverview()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            [],
            [],
            out _);
        EntityId? revealedEntityId = null;
        viewModel.EntityRevealRequested += id => revealedEntityId = id;
        viewModel.OpenCreateEntityCommand.Execute(null);
        Assert.True(viewModel.ManualCreation.IsOpen);
        viewModel.ManualCreation.EntityName = "NewManualEntity";

        await viewModel.ManualCreation.CreateAsync();

        Assert.False(viewModel.ManualCreation.IsOpen);
        Assert.Equal(MainWindowTab.Overview, viewModel.SelectedTab);
        EntityOverviewRow row = Assert.Single(viewModel.OverviewItems);
        Assert.Equal("NewManualEntity", row.SourceName);
        Assert.Equal("Not started", row.Status);
        Assert.Equal(row.EntityId, revealedEntityId);
        Assert.Equal(row.EntityId, viewModel.SelectedEntityDetails?.EntityId);
    }

    [Fact]
    public async Task ManualCreation_CreateAndAddAnotherKeepsAClearedFormOpen()
    {
        MainWindowViewModel viewModel = CreateViewModel([], [], out _);
        viewModel.OpenCreateEntityCommand.Execute(null);
        viewModel.ManualCreation.EntityName = "First";
        Assert.False(viewModel.OpenCreateEntityCommand.CanExecute(null));

        viewModel.ManualCreation.CreateAnotherCommand.Execute(null);
        // The confirmation is set after the list refresh, so wait for both.
        await WaitUntilAsync(() => viewModel.OverviewItems.Count == 1 && !viewModel.IsBusy &&
            viewModel.ManualCreation.OperationMessage?.Contains("First", StringComparison.Ordinal) == true);

        Assert.True(viewModel.ManualCreation.IsOpen);
        Assert.Equal(string.Empty, viewModel.ManualCreation.EntityName);
        Assert.Contains("First", viewModel.ManualCreation.OperationMessage, StringComparison.Ordinal);
        Assert.Null(viewModel.SelectedEntityDetails);
    }

    [Fact]
    public void ManualCreation_CancelAsksBeforeDiscardingADraft()
    {
        RecordingDiscardConfirmation confirmation = new(false);
        MainWindowViewModel viewModel = CreateViewModel([], [], out _, discardConfirmation: confirmation);
        viewModel.OpenCreateEntityCommand.Execute(null);
        viewModel.ManualCreation.EntityName = "Draft";

        viewModel.ManualCreation.CancelCommand.Execute(null);
        Assert.True(viewModel.ManualCreation.IsOpen);
        Assert.Equal("Draft", viewModel.ManualCreation.EntityName);

        confirmation.Result = true;
        viewModel.ManualCreation.CancelCommand.Execute(null);
        Assert.False(viewModel.ManualCreation.IsOpen);
        Assert.Equal(2, confirmation.CallCount);

        viewModel.OpenCreateEntityCommand.Execute(null);
        viewModel.ManualCreation.CancelCommand.Execute(null);
        Assert.False(viewModel.ManualCreation.IsOpen);
        Assert.Equal(2, confirmation.CallCount);
    }

    [Fact]
    public async Task ManualCreation_ArchivedDuplicateNavigatesToArchivedDetails()
    {
        TrackedEntity archived = Entity(
            1,
            "Legacy",
            lifecycle: EntityLifecycleState.Archived);
        MainWindowViewModel viewModel = CreateViewModel(
            [archived],
            [],
            out _);
        await viewModel.InitializeAsync();
        viewModel.OpenCreateEntityCommand.Execute(null);
        viewModel.ManualCreation.EntityName = " legacy ";

        await viewModel.ManualCreation.CreateAsync();
        viewModel.ManualCreation.RestoreArchivedCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.Editor.IsOpen);

        Assert.Equal(MainWindowTab.Archived, viewModel.SelectedTab);
        Assert.False(viewModel.ManualCreation.IsOpen);
        Assert.True(viewModel.Editor.IsArchivedMode);
        Assert.Equal("Legacy", viewModel.Editor.SelectedEntityName);
    }

    [Fact]
    public async Task ApplyBulkEditAsync_StoreFailurePostsAFailureAndKeepsTheOverview()
    {
        NotificationCenter notifications = new();
        MainWindowViewModel viewModel = CreateViewModel(
            [Entity(1, "First")],
            [],
            out StubStateStore store,
            notifications: notifications);
        await viewModel.InitializeAsync();
        viewModel.UpdateOverviewSelection(viewModel.OverviewItems);
        await viewModel.OpenBulkEditAsync();
        viewModel.BulkEdit.SelectedStatus = viewModel.BulkEdit.StatusOptions.Single(static option =>
            option.Value == DevelopmentStatus.DevelopmentCompleted);
        store.Exception = new InvalidOperationException("Database unavailable");

        await viewModel.ApplyBulkEditAsync();

        NotificationItem notice = Assert.Single(notifications.Items);
        Assert.Equal(("Bulk update", NotificationKind.Failure), (notice.Title, notice.Kind));
        Assert.Equal("The bulk update was not applied: Database unavailable", notice.Message);
        Assert.False(viewModel.BulkEdit.IsOpen);
        Assert.Single(viewModel.OverviewItems);
        Assert.False(viewModel.HasOverviewError);
    }

    [Fact]
    public async Task ApplyBulkStatusAsync_SetsOnlyTheStatusWithoutOpeningThePanel()
    {
        TrackedEntity first = Entity(1, "First");
        TrackedEntity second = Entity(
            2,
            "Second",
            DevelopmentStatus.DevelopmentCompleted);
        NotificationCenter notifications = new();
        MainWindowViewModel viewModel = CreateViewModel(
            [first, second],
            [],
            out StubStateStore store,
            notifications: notifications);
        await viewModel.InitializeAsync();
        viewModel.UpdateOverviewSelection(viewModel.OverviewItems);
        viewModel.SelectedBulkStatus = DevelopmentStatus.DevelopmentCompleted;

        Assert.True(viewModel.ApplyBulkStatusCommand.CanExecute(null));
        await viewModel.OpenBulkEditAsync();
        Assert.False(viewModel.ApplyBulkStatusCommand.CanExecute(null));
        Assert.True(viewModel.TryCloseBulkEdit());
        Assert.True(viewModel.ApplyBulkStatusCommand.CanExecute(null));

        await viewModel.ApplyBulkStatusAsync();

        Assert.Equal(1, store.ApplyCount);
        TrackedStateChangeSet changeSet = Assert.IsType<TrackedStateChangeSet>(store.AppliedChangeSet);
        Assert.Single(changeSet.EntitiesWithProgressToUpdate);
        Assert.Empty(changeSet.EntitiesWithRequestedPriorityToUpdate);
        Assert.False(viewModel.BulkEdit.IsOpen);
        Assert.Equal(0, viewModel.SelectedActiveEntityCount);
        Assert.False(viewModel.ApplyBulkStatusCommand.CanExecute(null));
        Assert.Equal(0, viewModel.NotStartedCount);
        Assert.Equal(2, viewModel.DevelopmentCompletedCount);
        Assert.All(
            viewModel.OverviewItems,
            row => Assert.Equal(DevelopmentStatus.DevelopmentCompleted, row.DevelopmentStatus));
        NotificationItem notice = Assert.Single(notifications.Items);
        Assert.Equal(("Bulk update", NotificationKind.Success), (notice.Title, notice.Kind));
        Assert.Equal("1 entity updated to Completed; 1 already matched.", notice.Message);
    }

    [Fact]
    public async Task ApplyBulkEditAsync_AppliesEveryChosenFieldAndRefreshesSummary()
    {
        TrackedEntity first = Entity(1, "First");
        TrackedEntity second = Entity(
            2,
            "Second",
            DevelopmentStatus.DevelopmentCompleted);
        NotificationCenter notifications = new();
        MainWindowViewModel viewModel = CreateViewModel(
            [first, second],
            [],
            out StubStateStore store,
            notifications: notifications);
        await viewModel.InitializeAsync();
        viewModel.UpdateOverviewSelection(viewModel.OverviewItems);

        Assert.Equal(2, viewModel.SelectedActiveEntityCount);
        Assert.Equal("Edit 2 entities", viewModel.BulkEditButtonText);
        Assert.True(viewModel.OpenBulkEditCommand.CanExecute(null));
        await viewModel.OpenBulkEditAsync();
        Assert.True(viewModel.BulkEdit.IsOpen);
        Assert.Equal("Edit 2 entities", viewModel.BulkEdit.Title);
        Assert.False(viewModel.OpenBulkEditCommand.CanExecute(null));
        Assert.False(viewModel.EditOverviewEntityCommand.CanExecute(viewModel.OverviewItems[0]));
        viewModel.BulkEdit.SelectedStatus = viewModel.BulkEdit.StatusOptions.Single(static option =>
            option.Value == DevelopmentStatus.DevelopmentCompleted);
        viewModel.BulkEdit.SelectedPriority = viewModel.BulkEdit.PriorityOptions.Single(static option =>
            option.Value?.Value == 2);

        await viewModel.ApplyBulkEditAsync();

        Assert.Equal(1, store.ApplyCount);
        TrackedStateChangeSet changeSet = Assert.IsType<TrackedStateChangeSet>(store.AppliedChangeSet);
        Assert.Single(changeSet.EntitiesWithProgressToUpdate);
        Assert.Equal(2, changeSet.EntitiesWithRequestedPriorityToUpdate.Count);
        Assert.False(viewModel.BulkEdit.IsOpen);
        Assert.Equal(0, viewModel.SelectedActiveEntityCount);
        Assert.False(viewModel.OpenBulkEditCommand.CanExecute(null));
        Assert.Equal(0, viewModel.NotStartedCount);
        Assert.Equal(2, viewModel.DevelopmentCompletedCount);
        Assert.All(viewModel.OverviewItems, row =>
        {
            Assert.Equal(DevelopmentStatus.DevelopmentCompleted, row.DevelopmentStatus);
            Assert.Equal(2, row.RequestedPriorityValue);
        });
        NotificationItem notice = Assert.Single(notifications.Items);
        Assert.Equal(("Bulk update", NotificationKind.Success), (notice.Title, notice.Kind));
        Assert.Equal("2 entities updated; 0 already matched.", notice.Message);
    }

    [Fact]
    public async Task BulkEdit_AppliesOnlyCompleteChangesAndConfirmsBeforeDiscardingThem()
    {
        RecordingDiscardConfirmation confirmation = new(false);
        MainWindowViewModel viewModel = CreateViewModel(
            [Entity(1, "Alpha"), Entity(2, "Beta")],
            [],
            out StubStateStore store,
            discardConfirmation: confirmation);
        await viewModel.InitializeAsync();
        viewModel.UpdateOverviewSelection(viewModel.OverviewItems);
        await viewModel.OpenBulkEditAsync();

        Assert.False(viewModel.BulkEdit.HasChanges);
        Assert.False(viewModel.ApplyBulkEditCommand.CanExecute(null));
        Assert.False(viewModel.BulkEdit.CanChangeResponsibility);
        viewModel.BulkEdit.SelectedDueDateMode = viewModel.BulkEdit.DueDateModeOptions.Single(static option =>
            option.Value == BulkDueDateMode.Set);
        Assert.True(viewModel.BulkEdit.IsDueDateVisible);
        Assert.False(viewModel.ApplyBulkEditCommand.CanExecute(null));
        viewModel.BulkEdit.DueDate = new DateTime(2026, 11, 15);
        Assert.True(viewModel.ApplyBulkEditCommand.CanExecute(null));
        Assert.True(viewModel.HasUnsavedWork);

        Assert.False(viewModel.TryCloseBulkEdit());
        Assert.Equal(1, confirmation.CallCount);
        Assert.True(viewModel.BulkEdit.IsOpen);

        confirmation.Result = true;
        viewModel.CancelBulkEditCommand.Execute(null);
        Assert.Equal(2, confirmation.CallCount);
        Assert.False(viewModel.BulkEdit.IsOpen);
        Assert.False(viewModel.HasUnsavedWork);
        Assert.Equal(2, viewModel.SelectedActiveEntityCount);
        Assert.Equal(0, store.ApplyCount);

        // Reopened, every field is back at Keep as is, so closing needs no confirmation.
        await viewModel.OpenBulkEditAsync();
        Assert.False(viewModel.BulkEdit.HasChanges);
        Assert.Null(viewModel.BulkEdit.DueDate);
        Assert.True(viewModel.TryCloseBulkEdit());
        Assert.Equal(2, confirmation.CallCount);
    }

    [Fact]
    public async Task OverviewSelection_ResultSetAndTabChanges_ClearSelection()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            [Entity(1, "Alpha"), Entity(2, "Beta")],
            [],
            out _);
        await viewModel.InitializeAsync();

        viewModel.UpdateOverviewSelection(viewModel.OverviewItems);
        viewModel.OverviewSearchQuery = "Alpha";
        Assert.Equal(0, viewModel.SelectedActiveEntityCount);

        viewModel.UpdateOverviewSelection(viewModel.OverviewItems);
        viewModel.ActiveTable.SetSingleStatusFilter(DevelopmentStatus.NotStarted);
        Assert.Equal(0, viewModel.SelectedActiveEntityCount);

        viewModel.UpdateOverviewSelection(viewModel.OverviewItems);
        viewModel.ActiveTable.StatusFilter.SortDescendingCommand.Execute(null);
        Assert.Equal(0, viewModel.SelectedActiveEntityCount);

        viewModel.UpdateOverviewSelection(viewModel.OverviewItems);
        await viewModel.RefreshAsync();
        Assert.Equal(0, viewModel.SelectedActiveEntityCount);

        viewModel.UpdateOverviewSelection(viewModel.OverviewItems);
        viewModel.SelectedTab = MainWindowTab.Archived;
        Assert.Equal(0, viewModel.SelectedActiveEntityCount);
    }

    [Fact]
    public async Task ClearOverviewSelection_ClearsSelectionAndRequestsVisualReset()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            [Entity(1, "Alpha"), Entity(2, "Beta")],
            [],
            out _);
        await viewModel.InitializeAsync();
        int clearRequestCount = 0;
        viewModel.OverviewSelectionClearRequested += (_, _) => clearRequestCount++;
        viewModel.UpdateOverviewSelection(viewModel.OverviewItems);

        viewModel.ClearOverviewSelection();

        Assert.Equal(0, viewModel.SelectedActiveEntityCount);
        Assert.Equal("No active entities selected", viewModel.BulkSelectionSummary);
        Assert.Equal("Edit entities", viewModel.BulkEditButtonText);
        Assert.False(viewModel.ApplyBulkStatusCommand.CanExecute(null));
        Assert.False(viewModel.OpenBulkEditCommand.CanExecute(null));
        Assert.Equal(1, clearRequestCount);
    }

    [Fact]
    public async Task BulkEditAction_IgnoresArchivedRowsAndDisablesWhileEditorIsOpen()
    {
        TrackedEntity active = Entity(1, "Active");
        TrackedEntity archived = Entity(
            2,
            "Archived",
            lifecycle: EntityLifecycleState.Archived);
        MainWindowViewModel viewModel = CreateViewModel(
            [active, archived],
            [],
            out _);
        await viewModel.InitializeAsync();

        viewModel.SelectedTab = MainWindowTab.Archived;
        viewModel.UpdateOverviewSelection(viewModel.ArchivedItems);
        Assert.Equal(0, viewModel.SelectedActiveEntityCount);
        Assert.False(viewModel.ApplyBulkStatusCommand.CanExecute(null));
        Assert.False(viewModel.OpenBulkEditCommand.CanExecute(null));

        viewModel.SelectedTab = MainWindowTab.Overview;
        EntityOverviewRow activeRow = Assert.Single(viewModel.OverviewItems);
        viewModel.UpdateOverviewSelection([activeRow]);
        viewModel.EditOverviewEntityCommand.Execute(activeRow);
        await WaitUntilAsync(() => viewModel.Editor.IsOpen);

        Assert.False(viewModel.ApplyBulkStatusCommand.CanExecute(null));
        Assert.False(viewModel.OpenBulkEditCommand.CanExecute(null));
    }

    [Fact]
    public void DirtyCreationFlow_IsReportedAndCanBeExplicitlyDiscarded()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            [],
            [],
            out _);

        viewModel.ManualCreation.EntityName = "Pending entity";

        Assert.True(viewModel.HasUnsavedWork);
        viewModel.DiscardTransientWork();
        Assert.False(viewModel.HasUnsavedWork);
        Assert.Equal(string.Empty, viewModel.ManualCreation.EntityName);
    }

    private static MainWindowViewModel CreateViewModel(
        IReadOnlyList<TrackedEntity> entities,
        IReadOnlyList<PersistedDependency> dependencies,
        out StubStateStore store,
        IReadOnlyList<PersistedUnresolvedDependency>? unresolvedDependencies = null,
        IContextDiscardConfirmation? discardConfirmation = null,
        OverviewExportService? overviewExportService = null,
        IOverviewExportFilePicker? overviewExportFilePicker = null,
        OverviewExportSettingsViewModel? overviewExportSettings = null,
        NotificationCenter? notifications = null)
    {
        StubEntityRepository entityRepository = new(TestDependencies.WithPlaceholders(entities, unresolvedDependencies));
        StubDependencyRepository dependencyRepository = new(
            TestDependencies.Named(entities, dependencies, unresolvedDependencies));
        DependencyRanker ranker = new();
        EffectiveDependencyResolver resolver = new();
        store = new StubStateStore(entityRepository);
        EntityDependencyEditorService editorService = new(
            entityRepository,
            dependencyRepository,
            resolver,
            ranker,
            store,
            new PriorityPlanningService());
        return new MainWindowViewModel(
            TestTrackerId,
            new EntityOverviewService(
                entityRepository,
                entityRepository,
                dependencyRepository,
                ranker,
                resolver,
                new WorkflowReadinessEvaluator(),
                new PriorityPlanningService()),
            new BulkEntityUpdateService(
                entityRepository,
                dependencyRepository,
                resolver,
                store),
            new ManualEntityCreationService(
                entityRepository,
                dependencyRepository,
                ranker,
                resolver,
                store),
            editorService,
            new EntityLifecycleService(
                entityRepository,
                dependencyRepository,
                store,
                resolver,
                ranker),
            discardConfirmation ?? new AlwaysDiscardConfirmation(),
            overviewExportService: overviewExportService,
            overviewExportFilePicker: overviewExportFilePicker,
            overviewExportSettings: overviewExportSettings,
            notifications: notifications);
    }

    private static Task WaitUntilAsync(Func<bool> condition,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(condition))] string description = "") =>
        TestWait.UntilAsync(condition, description);

    private static void AssertStatusFilter(
        MainWindowViewModel viewModel,
        DevelopmentStatus status,
        string expectedEntityName)
    {
        Assert.True(viewModel.SelectOverviewStatusCommand.CanExecute(status));
        viewModel.SelectOverviewStatusCommand.Execute(status);
        Assert.True(viewModel.ActiveTable.IncludesStatus(status));
        Assert.Equal(expectedEntityName, Assert.Single(viewModel.OverviewItems).SourceName);
    }

    private static void ApplySingleFilter(
        OverviewColumnFilterState filter,
        string selectedDisplayName)
    {
        filter.OpenCommand.Execute(null);
        foreach (OverviewFilterOption option in filter.Options)
        {
            option.IsSelected = option.DisplayName == selectedDisplayName;
        }

        filter.ApplyCommand.Execute(null);
    }

    private static TrackedEntity Entity(
        int id,
        string name,
        DevelopmentStatus status = DevelopmentStatus.NotStarted,
        string notes = "",
        EntityLifecycleState lifecycle = EntityLifecycleState.Active,
        int? requestedPriority = null,
        string? responsibleDeveloper = null,
        string? groupName = null) =>
        new(
            new EntityId(new Guid(id, 0, 0, new byte[8])),
            TestTrackerId,
            name,
            status,
            notes,
            lifecycle,
            requestedPriority,
            responsibleDeveloper,
            groupName);

    private static PersistedDependency Dependency(TrackedEntity owner, TrackedEntity target) =>
        new(new DependencyEdge(owner.Id, target.Id));

    private static PersistedUnresolvedDependency Unresolved(
        TrackedEntity owner,
        string dependencySourceName) =>
        new(
            new UnresolvedDependency(owner.Id, dependencySourceName));

    private sealed class StubEntityRepository : IEntityRepository, IEntityAuditReader
    {
        private readonly List<TrackedEntity> _entities;

        public StubEntityRepository(IReadOnlyList<TrackedEntity> entities)
        {
            _entities = entities.ToList();
        }

        public Task<TrackedEntity?> GetAsync(TrackerId trackerId, EntityId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_entities.SingleOrDefault(entity => entity.Id == id));

        public Task<IReadOnlyList<TrackedEntity>> GetAllAsync(
            TrackerId trackerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TrackedEntity>>(_entities.ToArray());

        Task<IReadOnlyList<EntityAuditTimestamps>> IEntityAuditReader.GetAllAsync(
            TrackerId trackerId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EntityAuditTimestamps>>(_entities
                .Select(static entity => new EntityAuditTimestamps(
                    entity.Id,
                    new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 8, 24, 11, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero)))
                .ToArray());

        public void Add(TrackedEntity entity) => _entities.Add(entity);

        public void Archive(EntityId entityId) =>
            _entities.Single(entity => entity.Id == entityId)
                .ChangeLifecycleState(EntityLifecycleState.Archived);

        public void Restore(EntityId entityId) =>
            _entities.Single(entity => entity.Id == entityId)
                .ChangeLifecycleState(EntityLifecycleState.Active);

        public void UpdateProgress(TrackedEntity updated)
        {
            TrackedEntity entity = _entities.Single(item => item.Id == updated.Id);
            entity.ChangeStatus(updated.Status);
            entity.ChangeNotes(updated.Notes);
            entity.ChangeFilterActive(updated.FilterActive);
            entity.ChangeSharedNotes(updated.SharedNotes);
        }

        public void UpdateRequestedPriority(TrackedEntity updated) =>
            _entities.Single(item => item.Id == updated.Id)
                .ChangeRequestedPriority(updated.RequestedPriority);

        public void UpdateResponsibleDeveloper(TrackedEntity updated) =>
            _entities.Single(item => item.Id == updated.Id)
                .ChangeResponsibleDeveloper(updated.ResponsibleDeveloper);

        public void UpdateGroupName(TrackedEntity updated) =>
            _entities.Single(item => item.Id == updated.Id)
                .ChangeGroupName(updated.GroupName);

        public void UpdateDueDate(TrackedEntity updated) =>
            _entities.Single(item => item.Id == updated.Id)
                .ChangeDueDate(updated.DueDate);

    }

    private sealed class AlwaysDiscardConfirmation : IContextDiscardConfirmation
    {
        public bool ConfirmDiscard(string description) => true;
    }

    private sealed class RecordingDiscardConfirmation(bool result) : IContextDiscardConfirmation
    {
        public bool Result { get; set; } = result;

        public int CallCount { get; private set; }

        public bool ConfirmDiscard(string description)
        {
            CallCount++;
            return Result;
        }
    }

    private sealed class StubStateStore(StubEntityRepository entityRepository)
        : ITrackedStateStore
    {
        public int ApplyCount { get; private set; }

        public TrackedStateChangeSet? AppliedChangeSet { get; private set; }

        public Exception? Exception { get; set; }

        public Task ApplyAsync(
            TrackerId trackerId,
            TrackedStateChangeSet changeSet,
            CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            AppliedChangeSet = changeSet;
            if (Exception is not null)
            {
                return Task.FromException(Exception);
            }

            foreach (TrackedEntity entity in changeSet.EntitiesToAdd)
            {
                entityRepository.Add(entity);
            }

            foreach (EntityId entityId in changeSet.EntityIdsToArchive)
            {
                entityRepository.Archive(entityId);
            }

            foreach (EntityId entityId in changeSet.EntityIdsToRestore)
            {
                entityRepository.Restore(entityId);
            }

            foreach (TrackedEntity entity in changeSet.EntitiesWithProgressToUpdate)
            {
                entityRepository.UpdateProgress(entity);
            }

            foreach (TrackedEntity entity in changeSet.EntitiesWithRequestedPriorityToUpdate)
            {
                entityRepository.UpdateRequestedPriority(entity);
            }

            foreach (TrackedEntity entity in changeSet.EntitiesWithResponsibleDeveloperToUpdate)
            {
                entityRepository.UpdateResponsibleDeveloper(entity);
            }

            foreach (TrackedEntity entity in changeSet.EntitiesWithGroupNameToUpdate)
            {
                entityRepository.UpdateGroupName(entity);
            }

            foreach (TrackedEntity entity in changeSet.EntitiesWithDueDateToUpdate)
            {
                entityRepository.UpdateDueDate(entity);
            }

            return Task.CompletedTask;
        }

        public Task EnsureHistoryBaselineAsync(
            TrackerId trackerId,
            IEnumerable<TrackedEntity> entities,
            ProgressSnapshotState snapshot,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

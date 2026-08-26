using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Planning;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Synchronization;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using EntityTracker.Reporting;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task InitializeAsync_PopulatesRankedRowsAndProgressSummary()
    {
        TrackedEntity foundation = Entity(1, "Foundation", DevelopmentStatus.DevelopmentCompleted, "Ready");
        TrackedEntity service = Entity(2, "Service", DevelopmentStatus.InProgress);
        MainWindowViewModel viewModel = CreateViewModel(
            [service, foundation],
            [Dependency(service, foundation)],
            FailureResult(),
            new StubFilePicker(),
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
            FailureResult(),
            new StubFilePicker(),
            out _);

        await viewModel.InitializeAsync();

        Assert.Equal(1, viewModel.ReworkNeededCount);
        Assert.Equal(100, viewModel.ImplementedPercentage);
        Assert.Equal(100, viewModel.ReworkNeededPercentage);
        EntityOverviewRow row = Assert.Single(viewModel.OverviewItems);
        Assert.Equal("Rework needed", row.Status);
        Assert.Equal("Rework needed", row.WorkStatus);
        viewModel.SelectedOverviewFilter = OverviewManagerFilter.ReworkNeeded;
        Assert.Single(viewModel.OverviewItems);
        Assert.Contains(
            viewModel.Editor.StatusOptions,
            static option => option.Value == DevelopmentStatus.ReworkNeeded);
    }

    [Fact]
    public async Task SynchronizationReview_RequiresAndStagesCompletedEntityDecision()
    {
        TrackedEntity target = Entity(1, "Target");
        TrackedEntity owner = Entity(
            2,
            "Owner",
            DevelopmentStatus.DevelopmentCompleted);
        MainWindowViewModel viewModel = CreateViewModel(
            [target, owner],
            [],
            SchemaImportResult.Success(Candidate(
                ["Target", "Owner"],
                [("Owner", "Target", ImportedDependencyKind.Mandatory)])),
            new StubFilePicker("schema.csv"),
            out _);

        await viewModel.ImportCsvAsync();

        SynchronizationProgressImpactRow row = Assert.Single(viewModel.Review.ProgressImpacts);
        Assert.Equal("Decision required", row.DecisionText);
        Assert.False(viewModel.Review.CanApply);
        Assert.False(viewModel.ApplySynchronizationCommand.CanExecute(null));

        viewModel.MarkSynchronizationReworkCommand.Execute(row);

        Assert.True(viewModel.Review.CanApply);
        Assert.Equal(
            SynchronizationProgressDecision.MarkReworkNeeded,
            Assert.Single(viewModel.Review.ProgressImpacts).Decision);
        Assert.Equal(
            DevelopmentStatus.ReworkNeeded,
            Assert.Single(viewModel.Review.CurrentPlan!.ChangeSet.EntitiesWithProgressToUpdate).Status);
    }

    [Fact]
    public async Task ManagerFilters_AreExclusiveAndArchivedEntitiesStayOutsideActiveProgress()
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
            FailureResult(),
            new StubFilePicker(),
            out _,
            [Unresolved(blocked, "Missing")]);

        await viewModel.InitializeAsync();

        Assert.Equal(5, viewModel.TotalEntityCount);
        Assert.Equal(1, viewModel.ArchivedEntityCount);
        Assert.Equal(40, viewModel.ImplementedPercentage);
        Assert.Equal(20, viewModel.ReconciledPercentage);

        viewModel.SelectedOverviewFilter = OverviewManagerFilter.Ready;
        Assert.Equal("Ready", Assert.Single(viewModel.OverviewItems).SourceName);
        viewModel.SelectedOverviewFilter = OverviewManagerFilter.Blocked;
        Assert.Equal("Blocked", Assert.Single(viewModel.OverviewItems).SourceName);
        viewModel.SelectedOverviewFilter = OverviewManagerFilter.InProgress;
        Assert.Equal("InProgress", Assert.Single(viewModel.OverviewItems).SourceName);
        viewModel.SelectedOverviewFilter = OverviewManagerFilter.DevelopmentCompleted;
        Assert.Equal("DevelopmentCompleted", Assert.Single(viewModel.OverviewItems).SourceName);
        viewModel.SelectedOverviewFilter = OverviewManagerFilter.Reconciled;
        Assert.Equal("Reconciled", Assert.Single(viewModel.OverviewItems).SourceName);

        viewModel.SearchOverviewDependencies = true;
        viewModel.SelectedOverviewFilter = OverviewManagerFilter.Archived;
        EntityOverviewRow archivedRow = Assert.Single(viewModel.OverviewItems);
        Assert.Equal("Archived", archivedRow.SourceName);
        Assert.False(archivedRow.HasGraphIssue);
        Assert.Empty(archivedRow.GraphIssueTitle);
        Assert.False(viewModel.SearchOverviewDependencies);
        Assert.False(viewModel.CanSearchOverviewDependencies);
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
            FailureResult(),
            new StubFilePicker(),
            out _,
            [Unresolved(blocked, "Missing")]);

        await viewModel.InitializeAsync();

        viewModel.SelectOverviewFilterCommand.Execute(OverviewManagerFilter.NotStarted);

        Assert.Equal(OverviewManagerFilter.NotStarted, viewModel.SelectedOverviewFilter);
        Assert.Equal(
            ["Blocked", "Ready"],
            viewModel.OverviewItems
                .Select(static row => row.SourceName)
                .OrderBy(static name => name));

        AssertStatusFilter(viewModel, OverviewManagerFilter.InProgress, "InProgress");
        AssertStatusFilter(viewModel, OverviewManagerFilter.ReworkNeeded, "Rework");
        AssertStatusFilter(
            viewModel,
            OverviewManagerFilter.DevelopmentCompleted,
            "DevelopmentCompleted");
        AssertStatusFilter(viewModel, OverviewManagerFilter.Reconciled, "Reconciled");

        viewModel.SelectOverviewFilterCommand.Execute(OverviewManagerFilter.AllActive);

        Assert.Equal(OverviewManagerFilter.AllActive, viewModel.SelectedOverviewFilter);
        Assert.Equal(6, viewModel.OverviewItems.Count);
        Assert.DoesNotContain(viewModel.OverviewItems, row => row.SourceName == "Archived");
    }

    [Fact]
    public async Task StatusSummaryFilterCommand_DisablesEmptyStatusesAndKeepsActiveSearch()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            [Entity(1, "Matching entity"), Entity(2, "Other entity")],
            [],
            FailureResult(),
            new StubFilePicker(),
            out _);
        await viewModel.InitializeAsync();

        Assert.True(viewModel.SelectOverviewFilterCommand.CanExecute(
            OverviewManagerFilter.NotStarted));
        Assert.False(viewModel.SelectOverviewFilterCommand.CanExecute(
            OverviewManagerFilter.Reconciled));

        viewModel.SelectOverviewFilterCommand.Execute(OverviewManagerFilter.Reconciled);
        Assert.Equal(OverviewManagerFilter.AllActive, viewModel.SelectedOverviewFilter);

        viewModel.OverviewSearchQuery = "Matching";
        await WaitUntilAsync(() => viewModel.OverviewItems.Count == 1);
        viewModel.SelectOverviewFilterCommand.Execute(OverviewManagerFilter.NotStarted);
        viewModel.SelectOverviewFilterCommand.Execute(OverviewManagerFilter.NotStarted);

        Assert.Equal(OverviewManagerFilter.NotStarted, viewModel.SelectedOverviewFilter);
        Assert.Equal("Matching entity", Assert.Single(viewModel.OverviewItems).SourceName);

        viewModel.SelectOverviewFilterCommand.Execute(OverviewManagerFilter.AllActive);

        Assert.Equal("Matching", viewModel.OverviewSearchQuery);
        Assert.Equal("Matching entity", Assert.Single(viewModel.OverviewItems).SourceName);
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
            FailureResult(),
            new StubFilePicker(),
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
            FailureResult(),
            new StubFilePicker(),
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
        Assert.Equal("Showing 2 of 3 in All active", viewModel.OverviewSearchResultSummary);
    }

    [Fact]
    public async Task OverviewSearch_DependencyModeFindsDirectResolvedAndUnresolvedDependents()
    {
        TrackedEntity alpha = Entity(1, "Alpha");
        TrackedEntity beta = Entity(2, "Beta");
        TrackedEntity downstream = Entity(3, "Downstream");
        TrackedEntity unresolvedOwner = Entity(4, "Unresolved Owner");
        MainWindowViewModel viewModel = CreateViewModel(
            [alpha, beta, downstream, unresolvedOwner],
            [Dependency(beta, alpha), Dependency(downstream, beta)],
            FailureResult(),
            new StubFilePicker(),
            out _,
            [Unresolved(unresolvedOwner, "Legacy API")]);
        await viewModel.InitializeAsync();
        viewModel.OpenOverviewSearchCommand.Execute(null);
        viewModel.SearchOverviewDependencies = true;

        viewModel.OverviewSearchQuery = "alpha";
        await WaitUntilAsync(() => viewModel.OverviewItems.Count == 1);

        Assert.Equal("Beta", Assert.Single(viewModel.OverviewItems).SourceName);
        Assert.DoesNotContain(viewModel.OverviewItems, row => row.SourceName == "Downstream");

        viewModel.OverviewSearchQuery = "legacy";
        await WaitUntilAsync(() =>
            viewModel.OverviewItems.Count == 1 &&
            viewModel.OverviewItems[0].SourceName == "Unresolved Owner");
    }

    [Fact]
    public async Task OverviewSearch_NoMatchesClearCloseAndRefreshBehaveConsistently()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            [Entity(1, "Alpha"), Entity(2, "Beta")],
            [],
            FailureResult(),
            new StubFilePicker(),
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
    public void OpenOverviewSearchCommand_IsAvailableOnlyOnOverview()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            [], [], FailureResult(), new StubFilePicker(), out _);

        Assert.True(viewModel.OpenOverviewSearchCommand.CanExecute(null));
        viewModel.SelectedTabIndex = 1;
        Assert.False(viewModel.OpenOverviewSearchCommand.CanExecute(null));
        viewModel.SelectedTabIndex = 0;
        viewModel.OpenOverviewSearchCommand.Execute(null);
        Assert.True(viewModel.IsOverviewSearchOpen);
    }

    [Fact]
    public async Task EditOverviewEntityCommand_OpensStandaloneModalWithoutChangingTab()
    {
        TrackedEntity entity = Entity(1, "Customer");
        MainWindowViewModel viewModel = CreateViewModel(
            [entity],
            [],
            FailureResult(),
            new StubFilePicker(),
            out _);
        await viewModel.InitializeAsync();

        viewModel.EditOverviewEntityCommand.Execute(Assert.Single(viewModel.OverviewItems));

        Assert.True(viewModel.Editor.IsOpen);
        Assert.False(viewModel.Editor.IsReviewMode);
        Assert.True(viewModel.Editor.CanArchive);
        Assert.Equal("Customer", viewModel.Editor.SelectedEntityName);
        Assert.Equal(0, viewModel.SelectedTabIndex);
    }

    [Fact]
    public async Task StandaloneEditor_SavesStatusNotesAndDependenciesAsOneRefresh()
    {
        TrackedEntity entity = Entity(1, "Customer");
        MainWindowViewModel viewModel = CreateViewModel(
            [entity],
            [],
            FailureResult(),
            new StubFilePicker(),
            out StubSynchronizationStore store);
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
            FailureResult(),
            new StubFilePicker(),
            out _);

        await viewModel.InitializeAsync();

        Assert.Equal(
            ["ZuluPrerequisite", "AlphaTarget", "Unrelated"],
            viewModel.OverviewItems.Select(static item => item.SourceName));
        Assert.Equal(["1", "1", "—"], viewModel.OverviewItems.Select(
            static item => item.Priority));
        Assert.Equal(
            ["—", "Core Team", "—"],
            viewModel.OverviewItems.Select(static item => item.ResponsibleDeveloper));
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
            FailureResult(),
            new StubFilePicker(),
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
            FailureResult(),
            new StubFilePicker(),
            out StubSynchronizationStore store,
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
            FailureResult(),
            new StubFilePicker(),
            out StubSynchronizationStore store);
        await viewModel.InitializeAsync();
        viewModel.SelectedOverviewFilter = OverviewManagerFilter.Archived;

        EntityOverviewRow archivedRow = Assert.Single(viewModel.OverviewItems);
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

        viewModel.EditOverviewEntityCommand.Execute(Assert.Single(viewModel.OverviewItems));
        await WaitUntilAsync(() => viewModel.Editor.CanRestoreEntity);
        viewModel.Editor.RestoreEntityCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.Editor.IsOpen);

        Assert.Equal(OverviewManagerFilter.AllActive, viewModel.SelectedOverviewFilter);
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
            FailureResult(),
            new StubFilePicker(),
            out StubSynchronizationStore store);
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
            FailureResult(),
            new StubFilePicker(),
            out StubSynchronizationStore store);
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
    public async Task ConfirmArchive_StoreFailureKeepsConfirmationOpenWithError()
    {
        TrackedEntity entity = Entity(1, "Customer");
        MainWindowViewModel viewModel = CreateViewModel(
            [entity],
            [],
            FailureResult(),
            new StubFilePicker(),
            out StubSynchronizationStore store);
        store.Exception = new InvalidOperationException("Database unavailable");
        await viewModel.InitializeAsync();
        viewModel.EditOverviewEntityCommand.Execute(Assert.Single(viewModel.OverviewItems));
        viewModel.Editor.RequestArchiveCommand.Execute(null);

        viewModel.Editor.ConfirmArchiveCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.Editor.HasArchiveError);

        Assert.True(viewModel.Editor.IsOpen);
        Assert.True(viewModel.Editor.IsArchiveConfirmationOpen);
        Assert.Contains("Database unavailable", viewModel.Editor.ArchiveErrorMessage);
        Assert.Single(viewModel.OverviewItems);
    }

    [Fact]
    public async Task Overview_ShowsManualProvenance()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            [Entity(1, "Planned", provenance: EntityProvenance.ManualOnly)],
            [],
            FailureResult(),
            new StubFilePicker(),
            out _);

        await viewModel.InitializeAsync();

        Assert.Equal("Manual only", Assert.Single(viewModel.OverviewItems).Provenance);
    }

    [Fact]
    public async Task CompleteReview_ShowsAbsentManualOnlyEntityAsKeptActive()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            [Entity(1, "Planned", provenance: EntityProvenance.ManualOnly)],
            [],
            SchemaImportResult.Success(Candidate([], [])),
            new StubFilePicker("empty.csv"),
            out _);

        await viewModel.ImportCsvAsync();

        SchemaSynchronizationReviewRow row =
            Assert.Single(viewModel.Review.ManualOnlyEntities);
        Assert.Equal("Planned", row.SourceName);
        Assert.Contains("kept active", row.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(viewModel.Review.MissingEntities);
    }

    [Fact]
    public async Task ManualCreation_SuccessReturnsToRefreshedOverview()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            [],
            [],
            FailureResult(),
            new StubFilePicker(),
            out _);
        viewModel.SelectedTabIndex = 2;
        viewModel.ManualCreation.EntityName = "NewManualEntity";

        await viewModel.ManualCreation.CreateAsync();

        Assert.Equal(0, viewModel.SelectedTabIndex);
        EntityOverviewRow row = Assert.Single(viewModel.OverviewItems);
        Assert.Equal("NewManualEntity", row.SourceName);
        Assert.Equal("Manual only", row.Provenance);
        Assert.Equal("Not started", row.Status);
    }

    [Fact]
    public void ImportMode_DefaultsToCompleteAndPartialRequiresSelection()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            [], [], FailureResult(), new StubFilePicker(), out _);

        Assert.True(viewModel.Review.IsCompleteImport);
        Assert.Equal(SchemaImportMode.Complete, viewModel.Review.Mode);
        Assert.True(viewModel.Review.CanSelectImportMode);

        viewModel.Review.IsPartialImport = true;

        Assert.False(viewModel.Review.IsCompleteImport);
        Assert.Equal(SchemaImportMode.Partial, viewModel.Review.Mode);
        Assert.True(viewModel.Review.CanSelectImportMode);
    }

    [Fact]
    public async Task ImportCsvAsync_CompleteImport_ShowsOnlyActionableReviewAndUnchangedCount()
    {
        TrackedEntity a = Entity(1, "A");
        TrackedEntity removed = Entity(2, "Removed");
        MainWindowViewModel viewModel = CreateViewModel(
            [a, removed],
            [],
            SchemaImportResult.Success(Candidate(["A", "NewEntity"], [])),
            new StubFilePicker("C:\\schemas\\current.csv"),
            out _);

        await viewModel.ImportCsvAsync();

        Assert.Equal(1, viewModel.SelectedTabIndex);
        Assert.Equal("current.csv", viewModel.Review.SelectedFileName);
        Assert.Equal("NewEntity", Assert.Single(viewModel.Review.NewEntities).SourceName);
        Assert.Equal("Removed", Assert.Single(viewModel.Review.MissingEntities).SourceName);
        Assert.Empty(viewModel.Review.ChangedEntities);
        Assert.Equal(1, viewModel.Review.UnchangedEntityCount);
        Assert.False(viewModel.Review.CanSelectImportMode);
        Assert.True(viewModel.ApplySynchronizationCommand.CanExecute(null));
    }

    [Fact]
    public async Task ImportCsvAsync_PartialImport_DoesNotShowAbsentPersistedEntity()
    {
        TrackedEntity a = Entity(1, "A");
        TrackedEntity b = Entity(2, "B");
        MainWindowViewModel viewModel = CreateViewModel(
            [a, b],
            [],
            SchemaImportResult.Success(Candidate(["A"], [])),
            new StubFilePicker("partial.csv"),
            out _);
        viewModel.Review.IsPartialImport = true;

        await viewModel.ImportCsvAsync();

        Assert.Equal("Partial", viewModel.Review.ImportModeLabel);
        Assert.Empty(viewModel.Review.MissingEntities);
        Assert.Equal(1, viewModel.Review.UnchangedEntityCount);
    }

    [Fact]
    public async Task ImportCsvAsync_UnknownCsvWarning_IsReplacedByCandidateUnresolvedSummary()
    {
        ImportDiagnostic warning = new(
            ImportDiagnosticCode.UnknownDependency,
            "CSV-relative warning",
            2,
            "mandatory_dependencies",
            ImportDiagnosticSeverity.Warning);
        MainWindowViewModel viewModel = CreateViewModel(
            [],
            [],
            SchemaImportResult.Success(
                Candidate(
                    ["Owner"],
                    [],
                    [("Owner", "Missing", ImportedDependencyKind.Mandatory)]),
                [warning]),
            new StubFilePicker("unknown.csv"),
            out _);

        await viewModel.ImportCsvAsync();

        Assert.Empty(viewModel.Review.Warnings);
        SchemaSynchronizationReviewRow unresolved =
            Assert.Single(viewModel.Review.UnresolvedEntities);
        Assert.Equal("Owner", unresolved.SourceName);
        Assert.Contains("Missing", unresolved.Details);
    }

    [Fact]
    public async Task ImportCsvAsync_InvalidFile_ShowsDiagnosticAndNoReview()
    {
        ImportDiagnostic diagnostic = new(
            ImportDiagnosticCode.CountMismatch,
            "Declared count does not match.",
            4,
            "mandatory_dependency_count");
        MainWindowViewModel viewModel = CreateViewModel(
            [],
            [],
            SchemaImportResult.Failure([diagnostic]),
            new StubFilePicker("invalid.csv"),
            out _);

        await viewModel.ImportCsvAsync();

        Assert.False(viewModel.Review.HasReview);
        Assert.Equal(
            "Row 4, mandatory_dependency_count: Declared count does not match.",
            Assert.Single(viewModel.Review.Diagnostics));
    }

    [Fact]
    public async Task CancelSynchronizationAsync_DiscardsPlanWithoutCallingStore()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            [],
            [],
            SchemaImportResult.Success(Candidate(["New"], [])),
            new StubFilePicker("new.csv"),
            out StubSynchronizationStore store);
        await viewModel.ImportCsvAsync();

        await viewModel.CancelSynchronizationAsync();

        Assert.Equal(0, store.ApplyCount);
        Assert.False(viewModel.Review.HasReview);
        Assert.Equal("No file selected", viewModel.Review.SelectedFileName);
        Assert.Equal(SchemaImportMode.Complete, viewModel.Review.Mode);
        Assert.True(viewModel.Review.CanSelectImportMode);
    }

    [Fact]
    public async Task ApplySynchronizationAsync_AppliesWholePlanAndReturnsToFreshOverview()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            [],
            [],
            SchemaImportResult.Success(Candidate(["New"], [])),
            new StubFilePicker("new.csv"),
            out StubSynchronizationStore store);
        await viewModel.ImportCsvAsync();

        await viewModel.ApplySynchronizationAsync();

        Assert.Equal(1, store.ApplyCount);
        Assert.NotNull(store.AppliedChangeSet);
        Assert.Equal(0, viewModel.SelectedTabIndex);
        Assert.False(viewModel.Review.HasReview);
        Assert.Equal(SchemaImportMode.Complete, viewModel.Review.Mode);
        Assert.True(viewModel.HasLatestImport);
        Assert.Contains("new.csv", viewModel.LatestImportHeadline);
        Assert.Contains("Applied new.csv", viewModel.OperationMessage);
    }

    [Fact]
    public async Task ApplySynchronizationAsync_WhenArchivingIsDeclined_ChangesNothing()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            [Entity(1, "Missing")],
            [],
            SchemaImportResult.Success(Candidate([], [])),
            new StubFilePicker("complete.csv"),
            out StubSynchronizationStore store,
            confirmationService: new DeclineConfirmationService());
        await viewModel.ImportCsvAsync();

        await viewModel.ApplySynchronizationAsync();

        Assert.Equal(0, store.ApplyCount);
        Assert.True(viewModel.Review.HasReview);
        Assert.Equal(1, viewModel.SelectedTabIndex);
    }

    [Fact]
    public async Task ApplySynchronizationAsync_StoreFailure_PreservesReviewForInspection()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            [],
            [],
            SchemaImportResult.Success(Candidate(["New"], [])),
            new StubFilePicker("new.csv"),
            out StubSynchronizationStore store);
        store.Exception = new InvalidOperationException("Transaction failed");
        await viewModel.ImportCsvAsync();

        await viewModel.ApplySynchronizationAsync();

        Assert.True(viewModel.Review.HasReview);
        Assert.Contains("Transaction failed", Assert.Single(viewModel.Review.Diagnostics));
    }

    [Fact]
    public async Task CyclicReview_CanStageSuppressionBeforeAtomicApply()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            [],
            [],
            SchemaImportResult.Success(Candidate(
                ["A", "B"],
                [
                    ("A", "B", ImportedDependencyKind.Mandatory),
                    ("B", "A", ImportedDependencyKind.Mandatory)
                ])),
            new StubFilePicker("cycle.csv"),
            out StubSynchronizationStore store);
        await viewModel.ImportCsvAsync();
        SchemaSynchronizationReviewRow b = viewModel.Review.NewEntities.Single(
            row => row.SourceName == "B");

        viewModel.EditReviewEntityCommand.Execute(b);

        Assert.Equal(1, viewModel.SelectedTabIndex);
        Assert.True(viewModel.Editor.IsOpen);
        Assert.True(viewModel.Editor.IsReviewMode);
        Assert.False(viewModel.Editor.CanArchive);
        Assert.False(viewModel.Editor.RequestArchiveCommand.CanExecute(null));
        Assert.Equal("B", viewModel.Editor.SelectedEntityName);
        EntityDependencyEditRow imported = Assert.Single(viewModel.Editor.Dependencies);
        viewModel.Editor.SuppressCommand.Execute(imported);
        Assert.False(viewModel.Editor.HasErrors);
        Assert.True(viewModel.Editor.SaveCommand.CanExecute(null));

        viewModel.Editor.SaveCommand.Execute(null);

        Assert.Equal(1, viewModel.SelectedTabIndex);
        Assert.True(viewModel.Review.CanApply);
        Assert.Equal(0, store.ApplyCount);

        await viewModel.ApplySynchronizationAsync();

        Assert.Equal(1, store.ApplyCount);
        Assert.Equal(
            ManualDependencyOverrideAction.Suppress,
            Assert.Single(store.AppliedChangeSet!.ManualDependencyOverrides).Action);
    }

    [Fact]
    public async Task ImportCsvAsync_CancelledPicker_PreservesExistingReview()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            [],
            [],
            SchemaImportResult.Success(Candidate(["New"], [])),
            new StubFilePicker("first.csv", null),
            out _);
        await viewModel.ImportCsvAsync();
        SchemaSynchronizationPlan originalPlan = viewModel.Review.CurrentPlan!;

        await viewModel.ImportCsvAsync();

        Assert.Same(originalPlan, viewModel.Review.CurrentPlan);
        Assert.Equal("first.csv", viewModel.Review.SelectedFileName);
    }

    [Fact]
    public async Task ApplyBulkStatusAsync_UpdatesSelectedRowsAndRefreshesSummary()
    {
        TrackedEntity first = Entity(1, "First");
        TrackedEntity second = Entity(
            2,
            "Second",
            DevelopmentStatus.DevelopmentCompleted);
        MainWindowViewModel viewModel = CreateViewModel(
            [first, second],
            [],
            FailureResult(),
            new StubFilePicker(),
            out StubSynchronizationStore store);
        await viewModel.InitializeAsync();
        viewModel.UpdateOverviewSelection(viewModel.OverviewItems);
        viewModel.SelectedBulkStatus = DevelopmentStatus.DevelopmentCompleted;

        Assert.Equal(2, viewModel.SelectedActiveEntityCount);
        Assert.True(viewModel.ApplyBulkStatusCommand.CanExecute(null));

        await viewModel.ApplyBulkStatusAsync();

        Assert.Equal(1, store.ApplyCount);
        Assert.Equal(0, viewModel.SelectedActiveEntityCount);
        Assert.False(viewModel.ApplyBulkStatusCommand.CanExecute(null));
        Assert.Equal(0, viewModel.NotStartedCount);
        Assert.Equal(2, viewModel.DevelopmentCompletedCount);
        Assert.All(
            viewModel.OverviewItems,
            row => Assert.Equal(DevelopmentStatus.DevelopmentCompleted, row.DevelopmentStatus));
        Assert.Equal(
            "1 entity updated to Dev. completed; 1 already matched.",
            viewModel.OperationMessage);
    }

    [Fact]
    public async Task OverviewSelection_ResultSetAndTabChanges_ClearSelection()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            [Entity(1, "Alpha"), Entity(2, "Beta")],
            [],
            FailureResult(),
            new StubFilePicker(),
            out _);
        await viewModel.InitializeAsync();

        viewModel.UpdateOverviewSelection(viewModel.OverviewItems);
        viewModel.OverviewSearchQuery = "Alpha";
        Assert.Equal(0, viewModel.SelectedActiveEntityCount);

        viewModel.UpdateOverviewSelection(viewModel.OverviewItems);
        viewModel.SelectedOverviewFilter = OverviewManagerFilter.NotStarted;
        Assert.Equal(0, viewModel.SelectedActiveEntityCount);

        viewModel.UpdateOverviewSelection(viewModel.OverviewItems);
        await viewModel.RefreshAsync();
        Assert.Equal(0, viewModel.SelectedActiveEntityCount);

        viewModel.UpdateOverviewSelection(viewModel.OverviewItems);
        viewModel.SelectedTabIndex = 1;
        Assert.Equal(0, viewModel.SelectedActiveEntityCount);
    }

    [Fact]
    public async Task ClearOverviewSelection_ClearsSelectionAndRequestsVisualReset()
    {
        MainWindowViewModel viewModel = CreateViewModel(
            [Entity(1, "Alpha"), Entity(2, "Beta")],
            [],
            FailureResult(),
            new StubFilePicker(),
            out _);
        await viewModel.InitializeAsync();
        int clearRequestCount = 0;
        viewModel.OverviewSelectionClearRequested += (_, _) => clearRequestCount++;
        viewModel.UpdateOverviewSelection(viewModel.OverviewItems);

        viewModel.ClearOverviewSelection();

        Assert.Equal(0, viewModel.SelectedActiveEntityCount);
        Assert.Equal("No active entities selected", viewModel.BulkSelectionSummary);
        Assert.False(viewModel.ApplyBulkStatusCommand.CanExecute(null));
        Assert.Equal(1, clearRequestCount);
    }

    [Fact]
    public async Task BulkStatusAction_IgnoresArchivedRowsAndDisablesWhileEditorIsOpen()
    {
        TrackedEntity active = Entity(1, "Active");
        TrackedEntity archived = Entity(
            2,
            "Archived",
            lifecycle: EntityLifecycleState.Archived);
        MainWindowViewModel viewModel = CreateViewModel(
            [active, archived],
            [],
            FailureResult(),
            new StubFilePicker(),
            out _);
        await viewModel.InitializeAsync();

        viewModel.SelectedOverviewFilter = OverviewManagerFilter.Archived;
        viewModel.UpdateOverviewSelection(viewModel.OverviewItems);
        Assert.Equal(0, viewModel.SelectedActiveEntityCount);
        Assert.False(viewModel.ApplyBulkStatusCommand.CanExecute(null));

        viewModel.SelectedOverviewFilter = OverviewManagerFilter.AllActive;
        EntityOverviewRow activeRow = Assert.Single(viewModel.OverviewItems);
        viewModel.UpdateOverviewSelection([activeRow]);
        viewModel.EditOverviewEntityCommand.Execute(activeRow);
        await WaitUntilAsync(() => viewModel.Editor.IsOpen);

        Assert.False(viewModel.ApplyBulkStatusCommand.CanExecute(null));
    }

    private static MainWindowViewModel CreateViewModel(
        IReadOnlyList<TrackedEntity> entities,
        IReadOnlyList<PersistedDependency> dependencies,
        SchemaImportResult importResult,
        ICsvFilePicker picker,
        out StubSynchronizationStore store,
        IReadOnlyList<PersistedUnresolvedDependency>? unresolvedDependencies = null,
        ISchemaSynchronizationConfirmation? confirmationService = null)
    {
        StubEntityRepository entityRepository = new(entities);
        StubDependencyRepository dependencyRepository = new(
            dependencies,
            unresolvedDependencies ?? []);
        DependencyRanker ranker = new();
        EffectiveDependencyResolver resolver = new();
        StubManualDependencyOverrideRepository overrideRepository = new();
        store = new StubSynchronizationStore(entityRepository);
        EntityDependencyEditorService editorService = new(
            entityRepository,
            dependencyRepository,
            overrideRepository,
            resolver,
            ranker,
            store,
            new PriorityPlanningService());
        SchemaSynchronizationService synchronizationService = new(
            new StubFileParser(importResult),
            entityRepository,
            dependencyRepository,
            overrideRepository,
            new SchemaSynchronizationPlanner(ranker, resolver),
            editorService,
            store);
        return new MainWindowViewModel(
            new EntityOverviewService(
                entityRepository,
                dependencyRepository,
                overrideRepository,
                ranker,
                resolver,
                new WorkflowReadinessEvaluator(),
                new PriorityPlanningService()),
            synchronizationService,
            new BulkStatusUpdateService(
                entityRepository,
                dependencyRepository,
                overrideRepository,
                resolver,
                store),
            new ManualEntityCreationService(
                entityRepository,
                dependencyRepository,
                overrideRepository,
                ranker,
                resolver,
                store),
            editorService,
            new EntityLifecycleService(
                entityRepository,
                dependencyRepository,
                overrideRepository,
                store,
                resolver,
                ranker),
            picker,
            CreateProgressDashboardViewModel(),
            new NoOpImageClipboard(),
            confirmationService ?? new AlwaysConfirmService());
    }

    private static ProgressDashboardViewModel CreateProgressDashboardViewModel()
    {
        ProgressChartPresentationBuilder presentationBuilder = new();
        return new ProgressDashboardViewModel(
            new ProgressReportingService(
                new EmptyProgressHistoryRepository(),
                TimeZoneInfo.Utc),
            presentationBuilder,
            new ProgressChartPngExporter(presentationBuilder),
            new CancelledProgressChartFilePicker(),
            new NoOpImageClipboard());
    }

    private sealed class CancelledProgressChartFilePicker : IProgressChartFilePicker
    {
        public string? SelectPngPath(string suggestedFileName) => null;
    }

    private sealed class NoOpImageClipboard : IClipboardService
    {
        public void SetPng(byte[] png)
        {
        }

        public void SetText(string text)
        {
        }
    }

    private sealed class AlwaysConfirmService : ISchemaSynchronizationConfirmation
    {
        public bool ConfirmArchiveMissingEntities(int entityCount) => true;
    }

    private sealed class DeclineConfirmationService : ISchemaSynchronizationConfirmation
    {
        public bool ConfirmArchiveMissingEntities(int entityCount) => false;
    }

    private static SchemaImportResult FailureResult() => SchemaImportResult.Failure(
        [new ImportDiagnostic(ImportDiagnosticCode.FileAccessError, "No file configured.")]);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static void AssertStatusFilter(
        MainWindowViewModel viewModel,
        OverviewManagerFilter filter,
        string expectedEntityName)
    {
        Assert.True(viewModel.SelectOverviewFilterCommand.CanExecute(filter));
        viewModel.SelectOverviewFilterCommand.Execute(filter);
        Assert.Equal(filter, viewModel.SelectedOverviewFilter);
        Assert.Equal(expectedEntityName, Assert.Single(viewModel.OverviewItems).SourceName);
    }

    private static TrackedEntity Entity(
        int id,
        string name,
        DevelopmentStatus status = DevelopmentStatus.NotStarted,
        string notes = "",
        EntityProvenance provenance = EntityProvenance.Imported,
        EntityLifecycleState lifecycle = EntityLifecycleState.Active,
        int? requestedPriority = null,
        string? responsibleDeveloper = null,
        string? groupName = null) =>
        new(
            new EntityId(new Guid(id, 0, 0, new byte[8])),
            name,
            status,
            notes,
            lifecycle,
            provenance,
            requestedPriority,
            responsibleDeveloper,
            groupName);

    private static PersistedDependency Dependency(TrackedEntity owner, TrackedEntity target) =>
        new(new DependencyEdge(owner.Id, target.Id), ImportedDependencyKind.Mandatory);

    private static PersistedUnresolvedDependency Unresolved(
        TrackedEntity owner,
        string dependencySourceName) =>
        new(
            new UnresolvedDependency(owner.Id, dependencySourceName),
            ImportedDependencyKind.Mandatory);

    private static SchemaImportCandidate Candidate(
        IEnumerable<string> names,
        IEnumerable<(string Owner, string Target, ImportedDependencyKind Kind)> dependencies,
        IEnumerable<(string Owner, string Target, ImportedDependencyKind Kind)>? unresolved = null)
    {
        ImportedEntity[] entities = names.Select(name =>
            new ImportedEntity(EntitySourceKey.From(name), name)).ToArray();
        return new SchemaImportCandidate(
            entities,
            dependencies.Select(item => new ImportedDependency(
                EntitySourceKey.From(item.Owner),
                EntitySourceKey.From(item.Target),
                item.Kind)),
            (unresolved ?? []).Select(item => new UnresolvedImportedDependency(
                EntitySourceKey.From(item.Owner),
                EntitySourceKey.From(item.Target),
                item.Target,
                item.Kind)));
    }

    private sealed class StubEntityRepository : IEntityRepository
    {
        private readonly List<TrackedEntity> _entities;

        public StubEntityRepository(IReadOnlyList<TrackedEntity> entities)
        {
            _entities = entities.ToList();
        }

        public Task<TrackedEntity?> GetAsync(EntityId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_entities.SingleOrDefault(entity => entity.Id == id));

        public Task<IReadOnlyList<TrackedEntity>> GetAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TrackedEntity>>(_entities.ToArray());

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

    }

    private sealed class EmptyProgressHistoryRepository : IProgressHistoryRepository
    {
        public Task<IReadOnlyList<EntityStatusHistoryEntry>> GetStatusHistoryAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EntityStatusHistoryEntry>>([]);

        public Task<IReadOnlyList<ProgressSnapshot>> GetProgressSnapshotsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProgressSnapshot>>([]);
    }

    private sealed class StubDependencyRepository(
        IReadOnlyList<PersistedDependency> dependencies,
        IReadOnlyList<PersistedUnresolvedDependency> unresolvedDependencies)
        : IDependencyRepository
    {
        public Task<IReadOnlyList<PersistedDependency>> GetAllAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(dependencies);

        public Task<IReadOnlyList<PersistedUnresolvedDependency>> GetAllUnresolvedAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(unresolvedDependencies);

    }

    private sealed class StubFileParser(SchemaImportResult result) : ISchemaImportFileParser
    {
        public Task<SchemaImportResult> ParseAsync(
            string filePath,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class StubFilePicker(params string?[] paths) : ICsvFilePicker
    {
        private readonly Queue<string?> _paths = new(paths);

        public string? SelectCsvFile() => _paths.Count == 0 ? null : _paths.Dequeue();
    }

    private sealed class StubSynchronizationStore(StubEntityRepository entityRepository)
        : ITrackedStateStore, ISchemaSynchronizationStore
    {
        public int ApplyCount { get; private set; }

        public TrackedStateChangeSet? AppliedChangeSet { get; private set; }

        public Exception? Exception { get; set; }

        public SchemaImportSummary? LatestSummary { get; private set; }

        public Task ApplyAsync(
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

            return Task.CompletedTask;
        }

        public Task EnsureHistoryBaselineAsync(
            IEnumerable<TrackedEntity> entities,
            ProgressSnapshotState snapshot,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task<SchemaImportSummary> ApplyAsync(
            TrackedStateChangeSet changeSet,
            SchemaImportCompletion completion,
            CancellationToken cancellationToken = default)
        {
            await ApplyAsync(changeSet, cancellationToken);
            LatestSummary = new SchemaImportSummary(
                new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero),
                completion);
            return LatestSummary;
        }

        public Task<SchemaImportSummary?> GetLatestImportAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(LatestSummary);
    }
}

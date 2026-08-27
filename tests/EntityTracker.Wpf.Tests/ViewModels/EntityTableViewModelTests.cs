using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class EntityTableViewModelTests
{
    [Fact]
    public void Filters_UseOrWithinAColumnAndAndAcrossColumns()
    {
        EntityTableViewModel table = EntityTableViewModel.CreateActive();
        table.ReplaceSourceItems(
        [
            Row(1, "Billing blank", "", "Billing", DevelopmentStatus.NotStarted,
                EntityWorkflowState.Ready),
            Row(2, "Billing Alice", "Alice", "Billing", DevelopmentStatus.ReworkNeeded,
                EntityWorkflowState.Blocked),
            Row(3, "Core Alice", "alice", "Core", DevelopmentStatus.InProgress,
                EntityWorkflowState.InProgress),
            Row(4, "Core Bob", "Bob", "Core", DevelopmentStatus.NotStarted,
                EntityWorkflowState.Ready)
        ]);

        ApplyFilter(table.StatusFilter, "Not started", "Rework needed");
        ApplyFilter(table.GroupFilter, "Billing");

        Assert.Equal(
            ["Billing blank", "Billing Alice"],
            table.Items.Select(row => row.SourceName));

        ApplyFilter(table.ResponsibleDeveloperFilter, "Alice");
        ApplyFilter(table.WorkStatusFilter!, "Blocked");

        Assert.Equal("Billing Alice", Assert.Single(table.Items).SourceName);
    }

    [Fact]
    public void MetadataFilters_AreCaseInsensitiveExposeBlankAndPreserveCanonicalCasing()
    {
        EntityTableViewModel table = EntityTableViewModel.CreateActive();
        table.ReplaceSourceItems(
        [
            Row(1, "One", "Alice", "Billing", DevelopmentStatus.NotStarted,
                EntityWorkflowState.Ready),
            Row(2, "Two", "alice", "billing", DevelopmentStatus.InProgress,
                EntityWorkflowState.InProgress),
            Row(3, "Three", "  ", "Core", DevelopmentStatus.ReworkNeeded,
                EntityWorkflowState.ReworkNeeded)
        ]);

        table.ResponsibleDeveloperFilter.OpenCommand.Execute(null);

        Assert.Equal(
            ["(Blank)", "Alice"],
            table.ResponsibleDeveloperFilter.Options.Select(option => option.DisplayName));

        table.ResponsibleDeveloperFilter.OptionSearchQuery = "ALI";
        Assert.Equal(
            "Alice",
            Assert.Single(table.ResponsibleDeveloperFilter.VisibleOptions).DisplayName);
        table.ResponsibleDeveloperFilter.IsOpen = false;

        ApplyFilter(table.ResponsibleDeveloperFilter, "Alice");
        Assert.Equal(["One", "Two"], table.Items.Select(row => row.SourceName));

        ApplyFilter(table.ResponsibleDeveloperFilter, "(Blank)");
        Assert.Equal("Three", Assert.Single(table.Items).SourceName);
    }

    [Fact]
    public void FilterMenus_AreStagedSupportZeroSelectionsSelectAllAndClear()
    {
        EntityTableViewModel table = EntityTableViewModel.CreateActive();
        table.ReplaceSourceItems(
        [
            Row(1, "One", "Alice", "Billing", DevelopmentStatus.NotStarted,
                EntityWorkflowState.Ready),
            Row(2, "Two", "Bob", "Core", DevelopmentStatus.InProgress,
                EntityWorkflowState.InProgress)
        ]);

        table.GroupFilter.OpenCommand.Execute(null);
        table.GroupFilter.Options.Single(option => option.DisplayName == "Core").IsSelected = false;
        table.GroupFilter.IsOpen = false;

        Assert.False(table.GroupFilter.IsApplied);
        Assert.Equal(2, table.Items.Count);

        table.GroupFilter.OpenCommand.Execute(null);
        foreach (OverviewFilterOption option in table.GroupFilter.Options)
        {
            option.IsSelected = false;
        }

        table.GroupFilter.ApplyCommand.Execute(null);
        Assert.True(table.GroupFilter.IsApplied);
        Assert.Empty(table.Items);

        table.GroupFilter.OpenCommand.Execute(null);
        table.GroupFilter.SelectAllCommand.Execute(null);
        table.GroupFilter.ApplyCommand.Execute(null);
        Assert.False(table.GroupFilter.IsApplied);
        Assert.Equal(2, table.Items.Count);

        ApplyFilter(table.GroupFilter, "Billing");
        table.GroupFilter.ClearFilterCommand.Execute(null);
        Assert.False(table.GroupFilter.IsApplied);
        Assert.Equal(2, table.Items.Count);
    }

    [Fact]
    public async Task FacetedOptions_HonorSearchAndOtherFiltersButIgnoreTheirOwnFilter()
    {
        EntityTableViewModel table = EntityTableViewModel.CreateActive();
        table.ReplaceSourceItems(
        [
            Row(1, "Invoice API", "Alice", "Billing", DevelopmentStatus.NotStarted,
                EntityWorkflowState.Ready),
            Row(2, "Invoice UI", "Bob", "Billing", DevelopmentStatus.InProgress,
                EntityWorkflowState.InProgress),
            Row(3, "Customer API", "Cara", "CRM", DevelopmentStatus.NotStarted,
                EntityWorkflowState.Ready)
        ]);

        ApplyFilter(table.GroupFilter, "Billing");
        table.GroupFilter.OpenCommand.Execute(null);
        Assert.Equal(
            ["Billing", "CRM"],
            table.GroupFilter.Options.Select(option => option.DisplayName));
        table.GroupFilter.IsOpen = false;

        ApplyFilter(table.ResponsibleDeveloperFilter, "Alice");
        table.GroupFilter.OpenCommand.Execute(null);
        Assert.Equal(
            ["Billing"],
            table.GroupFilter.Options.Select(option => option.DisplayName));
        table.GroupFilter.IsOpen = false;

        table.SearchQuery = "Invoice";
        await WaitUntilAsync(() => table.Items.Count == 1);
        table.ResponsibleDeveloperFilter.OpenCommand.Execute(null);

        Assert.Equal(
            ["Alice", "Bob"],
            table.ResponsibleDeveloperFilter.Options.Select(option => option.DisplayName));
    }

    [Fact]
    public void StatusAndWorkStatusSortsUseWorkflowOrderAndReplaceEachOther()
    {
        EntityTableViewModel table = EntityTableViewModel.CreateActive();
        table.ReplaceSourceItems(
        [
            Row(1, "Reconciled", "", "", DevelopmentStatus.Reconciled,
                EntityWorkflowState.Reconciled),
            Row(2, "Rework", "", "", DevelopmentStatus.ReworkNeeded,
                EntityWorkflowState.ReworkNeeded),
            Row(3, "Not started", "", "", DevelopmentStatus.NotStarted,
                EntityWorkflowState.Blocked),
            Row(4, "In progress", "", "", DevelopmentStatus.InProgress,
                EntityWorkflowState.InProgress)
        ]);

        table.StatusFilter.SortAscendingCommand.Execute(null);
        Assert.Equal(
            ["Not started", "In progress", "Rework", "Reconciled"],
            table.Items.Select(row => row.SourceName));
        Assert.True(table.StatusFilter.IsSortAscending);

        table.WorkStatusFilter!.SortDescendingCommand.Execute(null);
        Assert.Equal(
            ["Reconciled", "Rework", "In progress", "Not started"],
            table.Items.Select(row => row.SourceName));
        Assert.False(table.StatusFilter.IsSorted);
        Assert.True(table.WorkStatusFilter.IsSortDescending);
        Assert.False(table.ResponsibleDeveloperFilter.CanSort);
        Assert.False(table.ResponsibleDeveloperFilter.SortAscendingCommand.CanExecute(null));

        table.WorkStatusFilter.ClearSortCommand.Execute(null);
        Assert.Equal(
            ["Reconciled", "Rework", "Not started", "In progress"],
            table.Items.Select(row => row.SourceName));
    }

    [Fact]
    public async Task RefreshRetainsActiveStateAndArchivedStateIsIndependent()
    {
        EntityTableViewModel active = EntityTableViewModel.CreateActive();
        EntityTableViewModel archived = EntityTableViewModel.CreateArchived();
        active.ReplaceSourceItems(
        [
            Row(1, "Invoice", "Alice", "Billing", DevelopmentStatus.NotStarted,
                EntityWorkflowState.Ready),
            Row(2, "Customer", "Bob", "CRM", DevelopmentStatus.InProgress,
                EntityWorkflowState.InProgress)
        ]);
        archived.ReplaceSourceItems(
        [
            Row(3, "Old Invoice", "Alice", "Legacy", DevelopmentStatus.Reconciled,
                EntityWorkflowState.Archived, archived: true)
        ]);

        ApplyFilter(active.GroupFilter, "Billing");
        active.StatusFilter.SortDescendingCommand.Execute(null);
        active.SearchQuery = "Invoice";
        await WaitUntilAsync(() => active.Items.Count == 1);

        ApplyFilter(archived.GroupFilter, "Legacy");
        archived.SearchQuery = "Old";
        await WaitUntilAsync(() => archived.Items.Count == 1);

        active.ReplaceSourceItems(
        [
            Row(4, "Invoice Worker", "Cara", "Billing", DevelopmentStatus.ReworkNeeded,
                EntityWorkflowState.ReworkNeeded),
            Row(5, "Other", "Bob", "CRM", DevelopmentStatus.NotStarted,
                EntityWorkflowState.Ready)
        ]);

        Assert.Equal("Invoice Worker", Assert.Single(active.Items).SourceName);
        Assert.True(active.GroupFilter.IsApplied);
        Assert.True(active.StatusFilter.IsSorted);
        Assert.Equal("Old Invoice", Assert.Single(archived.Items).SourceName);
        Assert.Null(archived.WorkStatusFilter);
        Assert.Equal(3, archived.Filters.Count);
    }

    private static void ApplyFilter(
        OverviewColumnFilterState filter,
        params string[] selectedDisplayNames)
    {
        filter.OpenCommand.Execute(null);
        foreach (OverviewFilterOption option in filter.Options)
        {
            option.IsSelected = selectedDisplayNames.Contains(
                option.DisplayName,
                StringComparer.Ordinal);
        }

        filter.ApplyCommand.Execute(null);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static EntityOverviewRow Row(
        int id,
        string name,
        string responsibleDeveloper,
        string group,
        DevelopmentStatus status,
        EntityWorkflowState workflowState,
        bool archived = false) =>
        new(
            new EntityId(new Guid(id, 0, 0, new byte[8])),
            archived ? EntityLifecycleState.Archived : EntityLifecycleState.Active,
            status,
            workflowState,
            archived ? null : DependencyResolutionState.Resolved,
            "—",
            "—",
            name,
            responsibleDeveloper,
            group,
            "CSV",
            status.ToString(),
            workflowState.ToString(),
            "0",
            [],
            [],
            string.Empty,
            string.Empty,
            string.Empty,
            "—",
            string.Empty,
            archived ? "View and restore" : "Edit entity");
}

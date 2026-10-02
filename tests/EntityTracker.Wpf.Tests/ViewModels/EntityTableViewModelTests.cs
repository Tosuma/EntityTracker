using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class EntityTableViewModelTests
{
    [Fact]
    public void ResponsibleFilter_MatchesEachAssignedDeveloperAndBlankInBothTables()
    {
        EntityOverviewDeveloper alice = Developer(1, "AB", "Alex Brown");
        EntityOverviewDeveloper bob = Developer(2, "CD", "Alex Brown");
        EntityOverviewRow assigned = Row(1, "Shared", "", "Core",
            DevelopmentStatus.NotStarted, EntityWorkflowState.Ready) with
        { CurrentDevelopers = [alice, bob] };
        EntityOverviewRow blank = Row(2, "Unassigned", "", "Core",
            DevelopmentStatus.NotStarted, EntityWorkflowState.Ready);
        EntityTableViewModel active = EntityTableViewModel.CreateActive();
        active.ReplaceSourceItems([assigned, blank]);

        active.ResponsibleDeveloperFilter.OpenCommand.Execute(null);
        Assert.Equal(["(Blank)", "AB — Alex Brown", "CD — Alex Brown"],
            active.ResponsibleDeveloperFilter.Options.Select(option => option.DisplayName));
        ApplyFilter(active.ResponsibleDeveloperFilter, "AB — Alex Brown");
        Assert.Equal("Shared", Assert.Single(active.Items).SourceName);
        ApplyFilter(active.ResponsibleDeveloperFilter, "CD — Alex Brown", "(Blank)");
        Assert.Equal(["Shared", "Unassigned"], active.Items.Select(row => row.SourceName));
        ApplyFilter(active.GroupFilter, "Core");
        Assert.Equal(2, active.Items.Count);
        ApplyFilter(active.ResponsibleDeveloperFilter, "(Blank)");
        Assert.Equal("Unassigned", Assert.Single(active.Items).SourceName);
        active.ResponsibleDeveloperFilter.OpenCommand.Execute(null);
        active.ResponsibleDeveloperFilter.ClearFilterCommand.Execute(null);
        Assert.True(active.ResponsibleDeveloperFilter.IsOpen);
        Assert.All(active.ResponsibleDeveloperFilter.Options,
            option => Assert.False(option.IsSelected));
        Assert.Equal(2, active.Items.Count);
        active.ResponsibleDeveloperFilter.IsOpen = false;

        EntityTableViewModel archived = EntityTableViewModel.CreateArchived();
        archived.ReplaceSourceItems([assigned with { LifecycleState = EntityLifecycleState.Archived },
            blank with { LifecycleState = EntityLifecycleState.Archived }]);
        ApplyFilter(archived.ResponsibleDeveloperFilter, "CD — Alex Brown");
        Assert.Equal("Shared", Assert.Single(archived.Items).SourceName);
        ApplyFilter(archived.ResponsibleDeveloperFilter, "(Blank)");
        Assert.Equal("Unassigned", Assert.Single(archived.Items).SourceName);
    }

    [Fact]
    public void ResponsibleFilter_UsesIdsWhenLabelsCoincideAndKeepsSelectionAfterRename()
    {
        EntityOverviewDeveloper first = Developer(1, "AB", "Alex");
        EntityOverviewDeveloper second = Developer(2, "AB", "Alex");
        EntityTableViewModel table = EntityTableViewModel.CreateActive();
        table.ReplaceSourceItems([
            Row(1, "First", "", "", DevelopmentStatus.NotStarted,
                EntityWorkflowState.Ready) with { CurrentDevelopers = [first] },
            Row(2, "Second", "", "", DevelopmentStatus.NotStarted,
                EntityWorkflowState.Ready) with { CurrentDevelopers = [second] }
        ]);
        table.ResponsibleDeveloperFilter.OpenCommand.Execute(null);
        Assert.Equal(2, table.ResponsibleDeveloperFilter.Options.Count);
        Assert.All(table.ResponsibleDeveloperFilter.Options,
            option => Assert.False(option.IsSelected));
        table.ResponsibleDeveloperFilter.Options[0].IsSelected = true;
        table.ResponsibleDeveloperFilter.ApplyCommand.Execute(null);
        Assert.Equal("First", Assert.Single(table.Items).SourceName);

        table.ReplaceSourceItems([
            Row(1, "First", "", "", DevelopmentStatus.NotStarted,
                EntityWorkflowState.Ready) with
            { CurrentDevelopers = [first with { Initials = "AX", DisplayName = "Alex Renamed" }] },
            Row(2, "Second", "", "", DevelopmentStatus.NotStarted,
                EntityWorkflowState.Ready) with { CurrentDevelopers = [second] }
        ]);
        Assert.Equal("First", Assert.Single(table.Items).SourceName);
        table.ResponsibleDeveloperFilter.OpenCommand.Execute(null);
        Assert.Contains(table.ResponsibleDeveloperFilter.Options,
            option => option.DisplayName == "AX — Alex Renamed" && option.IsSelected);
    }

    [Fact]
    public async Task OrdinarySearch_UsesCurrentNamesOnlyWhenEnabledAndLeavesDependencyModeIsolated()
    {
        EntityOverviewDeveloper developer = Developer(1, "AB", "Alex Brown");
        EntityOverviewRow row = Row(1, "Invoice", "", "Core",
            DevelopmentStatus.NotStarted, EntityWorkflowState.Ready) with
        { CurrentDevelopers = [developer], DependencyNames = ["Address"] };
        EntityTableViewModel active = EntityTableViewModel.CreateActive();
        EntityTableViewModel archived = EntityTableViewModel.CreateArchived();
        active.ReplaceSourceItems([row]);
        archived.ReplaceSourceItems([row with { LifecycleState = EntityLifecycleState.Archived }]);

        active.SearchQuery = "alex";
        archived.SearchQuery = "ab";
        await WaitUntilAsync(() => active.Items.Count == 1 && archived.Items.Count == 1);
        active.SearchResponsibleNames = false;
        archived.SearchResponsibleNames = false;
        Assert.Empty(active.Items);
        Assert.Empty(archived.Items);
        active.SearchResponsibleNames = true;
        archived.SearchResponsibleNames = true;
        Assert.Single(active.Items);
        Assert.Single(archived.Items);

        active.SearchDependenciesInstead = true;
        Assert.Empty(active.Items);
        active.SearchQuery = "address";
        await WaitUntilAsync(() => active.Items.Count == 1);
        active.SearchQuery = "alex";
        await WaitUntilAsync(() => active.Items.Count == 0);
    }

    [Fact]
    public void Filters_UseOrWithinAColumnAndAndAcrossColumns()
    {
        EntityTableViewModel table = EntityTableViewModel.CreateActive();
        table.ReplaceSourceItems(
        [
            Row(1, "Billing blank", "", "Billing", DevelopmentStatus.NotStarted,
                EntityWorkflowState.Ready),
            Row(2, "Billing Alice", "Alice", "Billing", DevelopmentStatus.ReworkNeeded,
                EntityWorkflowState.ReworkNeeded),
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
        ApplyFilter(table.WorkStatusFilter!, "Ready");

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
    public void EveryOverviewColumnStartsUncheckedAndNoSelectionKeepsAllRows()
    {
        EntityOverviewRow activeRow = Row(1, "Active", "Alice", "Core",
            DevelopmentStatus.NotStarted, EntityWorkflowState.Ready);
        EntityOverviewRow archivedRow = Row(2, "Archived", "", "",
            DevelopmentStatus.Reconciled, EntityWorkflowState.Archived, archived: true);
        EntityTableViewModel active = EntityTableViewModel.CreateActive();
        EntityTableViewModel archived = EntityTableViewModel.CreateArchived();
        active.ReplaceSourceItems([activeRow]);
        archived.ReplaceSourceItems([archivedRow]);

        foreach (EntityTableViewModel table in new[] { active, archived })
        {
            foreach (OverviewColumnFilterState filter in table.Filters)
            {
                filter.OpenCommand.Execute(null);
                Assert.NotEmpty(filter.Options);
                Assert.All(filter.Options, option => Assert.False(option.IsSelected));
                filter.ApplyCommand.Execute(null);
                Assert.False(filter.IsApplied);
                Assert.Single(table.Items);
            }
        }
    }

    [Fact]
    public void FilterMenus_AreStagedAndNoSelectionShowsAll()
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
        Assert.All(table.GroupFilter.Options, option => Assert.False(option.IsSelected));
        table.GroupFilter.Options.Single(option => option.DisplayName == "Core").IsSelected = true;
        table.GroupFilter.IsOpen = false;

        Assert.False(table.GroupFilter.IsApplied);
        Assert.Equal(2, table.Items.Count);

        table.GroupFilter.OpenCommand.Execute(null);
        table.GroupFilter.ApplyCommand.Execute(null);
        Assert.False(table.GroupFilter.IsApplied);
        Assert.Equal(2, table.Items.Count);

        ApplyFilter(table.GroupFilter, "Billing");
        Assert.Equal("One", Assert.Single(table.Items).SourceName);
        table.GroupFilter.OpenCommand.Execute(null);
        Assert.True(table.GroupFilter.Options.Single(option =>
            option.DisplayName == "Billing").IsSelected);
        table.GroupFilter.Options.Single(option => option.DisplayName == "Billing")
            .IsSelected = false;
        table.GroupFilter.ApplyCommand.Execute(null);
        Assert.False(table.GroupFilter.IsApplied);
        Assert.Equal(2, table.Items.Count);
    }

    [Fact]
    public void ClearFilter_UnchecksHiddenValuesAndKeepsMenuOpen()
    {
        EntityTableViewModel table = EntityTableViewModel.CreateActive();
        table.ReplaceSourceItems(
        [
            Row(1, "One", "Alice", "Billing", DevelopmentStatus.NotStarted,
                EntityWorkflowState.Ready),
            Row(2, "Two", "Bob", "Core", DevelopmentStatus.InProgress,
                EntityWorkflowState.InProgress)
        ]);
        ApplyFilter(table.GroupFilter, "Billing");
        table.GroupFilter.OpenCommand.Execute(null);
        table.GroupFilter.OptionSearchQuery = "Core";
        Assert.Equal("Core", Assert.Single(table.GroupFilter.VisibleOptions).DisplayName);
        table.GroupFilter.ClearFilterCommand.Execute(null);

        Assert.False(table.GroupFilter.IsApplied);
        Assert.True(table.GroupFilter.IsOpen);
        Assert.Empty(table.GroupFilter.OptionSearchQuery);
        Assert.Equal(2, table.GroupFilter.VisibleOptions.Count);
        Assert.All(table.GroupFilter.Options, option => Assert.False(option.IsSelected));
        Assert.Equal(2, table.Items.Count);

        table.GroupFilter.Options.Single(option => option.DisplayName == "Core").IsSelected = true;
        table.GroupFilter.ApplyCommand.Execute(null);
        Assert.False(table.GroupFilter.IsOpen);
        Assert.Equal("Two", Assert.Single(table.Items).SourceName);
    }

    [Fact]
    public void SelectingEveryValueRemainsAnAppliedFilterWhenNewValuesArrive()
    {
        EntityTableViewModel table = EntityTableViewModel.CreateActive();
        table.ReplaceSourceItems(
        [
            Row(1, "One", "Alice", "Billing", DevelopmentStatus.NotStarted,
                EntityWorkflowState.Ready),
            Row(2, "Two", "Bob", "Core", DevelopmentStatus.InProgress,
                EntityWorkflowState.InProgress)
        ]);

        ApplyFilter(table.GroupFilter, "Billing", "Core");
        Assert.True(table.GroupFilter.IsApplied);
        Assert.Equal(2, table.Items.Count);
        table.ReplaceSourceItems(
        [
            Row(1, "One", "Alice", "Billing", DevelopmentStatus.NotStarted,
                EntityWorkflowState.Ready),
            Row(2, "Two", "Bob", "Core", DevelopmentStatus.InProgress,
                EntityWorkflowState.InProgress),
            Row(3, "Three", "Cara", "New", DevelopmentStatus.NotStarted,
                EntityWorkflowState.Ready)
        ]);

        Assert.Equal(["One", "Two"], table.Items.Select(row => row.SourceName));
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
    public void StatusAndWorkStatusSortsUseDisplayGroupOrderAndReplaceEachOther()
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
                EntityWorkflowState.InProgress),
            Row(5, "Completed", "", "", DevelopmentStatus.DevelopmentCompleted,
                EntityWorkflowState.DevelopmentCompleted)
        ]);

        table.StatusFilter.SortAscendingCommand.Execute(null);
        Assert.Equal(
            ["Not started", "In progress", "Rework", "Completed", "Reconciled"],
            table.Items.Select(row => row.SourceName));
        Assert.True(table.StatusFilter.IsSortAscending);

        table.WorkStatusFilter!.SortDescendingCommand.Execute(null);
        Assert.Equal(
            ["Reconciled", "Completed", "In progress", "Not started", "Rework"],
            table.Items.Select(row => row.SourceName));
        Assert.False(table.StatusFilter.IsSorted);
        Assert.True(table.WorkStatusFilter.IsSortDescending);
        Assert.False(table.ResponsibleDeveloperFilter.CanSort);
        Assert.False(table.ResponsibleDeveloperFilter.SortAscendingCommand.CanExecute(null));

        table.WorkStatusFilter.ClearSortCommand.Execute(null);
        Assert.Equal(
            ["Reconciled", "Rework", "Not started", "In progress", "Completed"],
            table.Items.Select(row => row.SourceName));
    }

    [Fact]
    public void WorkStatusFilter_ShowsPendingReworkAsReady()
    {
        EntityTableViewModel table = EntityTableViewModel.CreateActive();
        table.ReplaceSourceItems(
        [
            Row(1, "Started", "", "", DevelopmentStatus.InProgress,
                EntityWorkflowState.InProgress),
            Row(2, "Rework", "", "", DevelopmentStatus.ReworkNeeded,
                EntityWorkflowState.ReworkNeeded),
            Row(3, "Completed", "", "", DevelopmentStatus.DevelopmentCompleted,
                EntityWorkflowState.DevelopmentCompleted)
        ]);

        table.WorkStatusFilter!.OpenCommand.Execute(null);
        Assert.Equal(["Ready", "In progress", "Completed"],
            table.WorkStatusFilter.Options.Select(option => option.DisplayName));
        table.WorkStatusFilter.IsOpen = false;

        ApplyFilter(table.WorkStatusFilter, "In progress");
        Assert.Equal(["Started"], table.Items.Select(row => row.SourceName));

        table.WorkStatusFilter.ClearFilterCommand.Execute(null);
        ApplyFilter(table.WorkStatusFilter, "Ready");
        ApplyFilter(table.StatusFilter, "Rework needed");
        Assert.Equal("Rework", Assert.Single(table.Items).SourceName);
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

    private static EntityOverviewDeveloper Developer(int id, string initials, string name) =>
        new(new DeveloperId(new Guid(id, 0, 0, new byte[8])), initials, name);

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
            archived ? "View archived entity" : "Edit entity",
            CurrentDevelopers: string.IsNullOrWhiteSpace(responsibleDeveloper)
                ? []
                : responsibleDeveloper.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(initials => initials.Trim())
                    .Select(initials => new EntityOverviewDeveloper(
                        new DeveloperId(new Guid(System.Security.Cryptography.MD5.HashData(
                            System.Text.Encoding.UTF8.GetBytes(initials.ToUpperInvariant())))),
                        initials,
                        string.Empty))
                    .ToArray());
}

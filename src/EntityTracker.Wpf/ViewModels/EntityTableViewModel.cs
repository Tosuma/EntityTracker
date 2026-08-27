using System.ComponentModel;
using System.Runtime.CompilerServices;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using EntityTracker.Wpf.Commands;

namespace EntityTracker.Wpf.ViewModels;

public sealed class EntityTableViewModel : INotifyPropertyChanged
{
    private static readonly IReadOnlyDictionary<DevelopmentStatus, int> StatusOrder =
        new Dictionary<DevelopmentStatus, int>
        {
            [DevelopmentStatus.NotStarted] = 0,
            [DevelopmentStatus.InProgress] = 1,
            [DevelopmentStatus.ReworkNeeded] = 2,
            [DevelopmentStatus.DevelopmentCompleted] = 3,
            [DevelopmentStatus.Reconciled] = 4
        };

    private static readonly IReadOnlyDictionary<EntityWorkflowState, int> WorkStatusOrder =
        new Dictionary<EntityWorkflowState, int>
        {
            [EntityWorkflowState.Ready] = 0,
            [EntityWorkflowState.Blocked] = 1,
            [EntityWorkflowState.InProgress] = 2,
            [EntityWorkflowState.ReworkNeeded] = 3,
            [EntityWorkflowState.DevelopmentCompleted] = 4,
            [EntityWorkflowState.Reconciled] = 5,
            [EntityWorkflowState.Archived] = 6
        };

    private readonly IReadOnlyList<OverviewColumnFilterState> _filters;
    private readonly bool _searchDependencies;
    private IReadOnlyList<EntityOverviewRow> _sourceItems = [];
    private IReadOnlyList<EntityOverviewRow> _items = [];
    private string _searchQuery = string.Empty;
    private bool _isSearchOpen;
    private bool _searchDependenciesInstead;
    private OverviewSort? _sort;
    private CancellationTokenSource? _searchDebounce;

    private EntityTableViewModel(bool isArchived)
    {
        IsArchived = isArchived;
        _searchDependencies = !isArchived;

        ResponsibleDeveloperFilter = CreateStringFilter(
            OverviewColumnKey.ResponsibleDeveloper,
            "Responsible dev",
            row => row.ResponsibleDeveloper);
        GroupFilter = CreateStringFilter(
            OverviewColumnKey.Group,
            "Group",
            row => row.GroupName);
        StatusFilter = new OverviewColumnFilterState<DevelopmentStatus>(
            this,
            OverviewColumnKey.Status,
            "Status",
            canSort: true,
            row => row.DevelopmentStatus,
            FormatDevelopmentStatus,
            sortComparer: Comparer<DevelopmentStatus>.Create(
                (left, right) => StatusOrder[left].CompareTo(StatusOrder[right])));

        if (!isArchived)
        {
            WorkStatusFilter = new OverviewColumnFilterState<EntityWorkflowState>(
                this,
                OverviewColumnKey.WorkStatus,
                "Work status",
                canSort: true,
                row => row.WorkflowState,
                FormatWorkStatus,
                sortComparer: Comparer<EntityWorkflowState>.Create(
                    (left, right) => WorkStatusOrder[left].CompareTo(WorkStatusOrder[right])));
        }

        _filters = WorkStatusFilter is null
            ? [ResponsibleDeveloperFilter, GroupFilter, StatusFilter]
            : [ResponsibleDeveloperFilter, GroupFilter, StatusFilter, WorkStatusFilter];

        OpenSearchCommand = new RelayCommand(() => IsSearchOpen = true);
        ClearSearchCommand = new RelayCommand(ClearSearch);
        CloseSearchCommand = new RelayCommand(CloseSearch);
        ClearAllFiltersCommand = new RelayCommand(ClearAllFiltersAndSort);
    }

    public static EntityTableViewModel CreateActive() => new(isArchived: false);

    public static EntityTableViewModel CreateArchived() => new(isArchived: true);

    public bool IsArchived { get; }

    public IReadOnlyList<EntityOverviewRow> SourceItems => _sourceItems;

    public IReadOnlyList<EntityOverviewRow> Items
    {
        get => _items;
        private set
        {
            _items = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(ResultSummary));
            OnPropertyChanged(nameof(ShowFilteredEmptyState));
        }
    }

    public OverviewColumnFilterState ResponsibleDeveloperFilter { get; }

    public OverviewColumnFilterState GroupFilter { get; }

    public OverviewColumnFilterState StatusFilter { get; }

    public OverviewColumnFilterState? WorkStatusFilter { get; }

    public IReadOnlyList<OverviewColumnFilterState> Filters => _filters;

    public OverviewSort? Sort
    {
        get => _sort;
        private set
        {
            if (_sort == value)
            {
                return;
            }

            _sort = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSort));
            OnPropertyChanged(nameof(HasFiltersOrSort));
            foreach (OverviewColumnFilterState filter in _filters)
            {
                filter.NotifySortChanged();
            }
        }
    }

    public bool HasSort => Sort is not null;

    public bool HasFilters => _filters.Any(filter => filter.IsApplied);

    public bool HasFiltersOrSort => HasFilters || HasSort;

    public bool HasSourceItems => SourceItems.Count > 0;

    public bool HasItems => Items.Count > 0;

    public bool ShowFilteredEmptyState => HasSourceItems && !HasItems;

    public string ResultSummary => $"Showing {Items.Count} of {SourceItems.Count}";

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            string normalized = value ?? string.Empty;
            if (_searchQuery == normalized)
            {
                return;
            }

            _searchQuery = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSearchQuery));
            ProjectionChanging?.Invoke(this, EventArgs.Empty);
            ScheduleSearch();
        }
    }

    public bool HasSearchQuery => !string.IsNullOrWhiteSpace(SearchQuery);

    public bool CanSearchDependencies => _searchDependencies;

    public bool SearchDependenciesInstead
    {
        get => _searchDependenciesInstead;
        set
        {
            if (!_searchDependencies || _searchDependenciesInstead == value)
            {
                return;
            }

            _searchDependenciesInstead = value;
            OnPropertyChanged();
            RebuildProjectionWithSelectionClear();
        }
    }

    public bool IsSearchOpen
    {
        get => _isSearchOpen;
        set
        {
            if (_isSearchOpen == value)
            {
                return;
            }

            _isSearchOpen = value;
            OnPropertyChanged();
        }
    }

    public RelayCommand OpenSearchCommand { get; }

    public RelayCommand ClearSearchCommand { get; }

    public RelayCommand CloseSearchCommand { get; }

    public RelayCommand ClearAllFiltersCommand { get; }

    public event EventHandler? ProjectionChanging;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ReplaceSourceItems(IReadOnlyList<EntityOverviewRow> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        CloseAllFilters();
        _sourceItems = items;
        OnPropertyChanged(nameof(SourceItems));
        OnPropertyChanged(nameof(HasSourceItems));
        RebuildProjection();
    }

    public void SetSingleStatusFilter(DevelopmentStatus status)
    {
        StatusFilter.SetSingleAppliedValue(status);
        ApplyFilterChange();
    }

    public bool IncludesStatus(DevelopmentStatus status) =>
        StatusFilter.IsApplied && StatusFilter.IncludesAppliedValue(status);

    public void ClearAllFiltersAndSort()
    {
        foreach (OverviewColumnFilterState filter in _filters)
        {
            filter.ClearAppliedSelection();
            filter.CloseWithoutApplying();
        }

        Sort = null;
        RebuildProjectionWithSelectionClear();
        OnPropertyChanged(nameof(HasFilters));
        OnPropertyChanged(nameof(HasFiltersOrSort));
    }

    public bool CloseOpenFilter()
    {
        OverviewColumnFilterState? openFilter = _filters.FirstOrDefault(filter => filter.IsOpen);
        if (openFilter is null)
        {
            return false;
        }

        openFilter.CloseWithoutApplying();
        return true;
    }

    internal void OpenFilter(OverviewColumnFilterState filter)
    {
        foreach (OverviewColumnFilterState other in _filters.Where(other => other != filter))
        {
            other.CloseWithoutApplying();
        }

        IReadOnlyList<EntityOverviewRow> candidates = SourceItems
            .Where(MatchesSearch)
            .Where(row => _filters
                .Where(other => other != filter)
                .All(other => other.Matches(row)))
            .ToArray();
        filter.BeginEdit(candidates);
        filter.IsOpen = true;
    }

    internal void ApplyFilterChange()
    {
        RebuildProjectionWithSelectionClear();
        OnPropertyChanged(nameof(HasFilters));
        OnPropertyChanged(nameof(HasFiltersOrSort));
    }

    internal void SetSort(OverviewColumnKey column, OverviewSortDirection direction)
    {
        OverviewColumnFilterState? filter = _filters.SingleOrDefault(item => item.Key == column);
        if (filter?.CanSort != true)
        {
            return;
        }

        Sort = new OverviewSort(column, direction);
        RebuildProjectionWithSelectionClear();
    }

    internal void ClearSort()
    {
        if (Sort is null)
        {
            return;
        }

        Sort = null;
        RebuildProjectionWithSelectionClear();
    }

    private OverviewColumnFilterState<string> CreateStringFilter(
        OverviewColumnKey key,
        string title,
        Func<EntityOverviewRow, string> selector) =>
        new(
            this,
            key,
            title,
            canSort: false,
            row => NormalizeMetadataValue(selector(row)),
            value => string.IsNullOrWhiteSpace(value) ? "(Blank)" : value,
            StringComparer.OrdinalIgnoreCase,
            StringComparer.OrdinalIgnoreCase);

    private void ScheduleSearch()
    {
        _searchDebounce?.Cancel();
        CancellationTokenSource cancellation = new();
        _searchDebounce = cancellation;

        _ = ApplySearchAfterDelayAsync(cancellation);
    }

    private async Task ApplySearchAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellation.Token);
            RebuildProjectionWithSelectionClear();
        }
        catch (OperationCanceledException)
        {
            // A newer query superseded this one.
        }
        finally
        {
            if (ReferenceEquals(_searchDebounce, cancellation))
            {
                _searchDebounce = null;
            }

            cancellation.Dispose();
        }
    }

    private void CloseSearch()
    {
        IsSearchOpen = false;
        ClearSearch();
    }

    private void ClearSearch()
    {
        _searchDebounce?.Cancel();
        _searchDebounce = null;

        if (_searchQuery.Length > 0)
        {
            _searchQuery = string.Empty;
            OnPropertyChanged(nameof(SearchQuery));
            OnPropertyChanged(nameof(HasSearchQuery));
            RebuildProjectionWithSelectionClear();
        }
    }

    private void CloseAllFilters()
    {
        foreach (OverviewColumnFilterState filter in _filters)
        {
            filter.CloseWithoutApplying();
        }
    }

    private void RebuildProjectionWithSelectionClear()
    {
        ProjectionChanging?.Invoke(this, EventArgs.Empty);
        RebuildProjection();
    }

    private void RebuildProjection()
    {
        IEnumerable<(EntityOverviewRow Row, int SourcePosition)> filtered = SourceItems
            .Select((row, index) => (row, index))
            .Where(item => MatchesSearch(item.row))
            .Where(item => _filters.All(filter => filter.Matches(item.row)));

        if (Sort is { } sort)
        {
            filtered = ApplySort(filtered, sort);
        }

        Items = filtered.Select(item => item.Row).ToArray();
    }

    private IEnumerable<(EntityOverviewRow Row, int SourcePosition)> ApplySort(
        IEnumerable<(EntityOverviewRow Row, int SourcePosition)> rows,
        OverviewSort sort)
    {
        Func<(EntityOverviewRow Row, int SourcePosition), int> keySelector = sort.Column switch
        {
            OverviewColumnKey.Status => item => StatusOrder[item.Row.DevelopmentStatus],
            OverviewColumnKey.WorkStatus => item => WorkStatusOrder[item.Row.WorkflowState],
            _ => item => item.SourcePosition
        };

        return sort.Direction == OverviewSortDirection.Ascending
            ? rows.OrderBy(keySelector).ThenBy(item => item.SourcePosition)
            : rows.OrderByDescending(keySelector).ThenBy(item => item.SourcePosition);
    }

    private bool MatchesSearch(EntityOverviewRow row)
    {
        string query = SearchQuery.Trim();
        if (string.IsNullOrEmpty(query))
        {
            return true;
        }

        if (!SearchDependenciesInstead)
        {
            return row.SourceName.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        return row.DependencyNames.Any(name =>
                name.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatDevelopmentStatus(DevelopmentStatus status) => status switch
    {
        DevelopmentStatus.NotStarted => "Not started",
        DevelopmentStatus.InProgress => "In progress",
        DevelopmentStatus.ReworkNeeded => "Rework needed",
        DevelopmentStatus.DevelopmentCompleted => "Dev. completed",
        DevelopmentStatus.Reconciled => "Reconciled",
        _ => status.ToString()
    };

    private static string NormalizeMetadataValue(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string FormatWorkStatus(EntityWorkflowState status) => status switch
    {
        EntityWorkflowState.Ready => "Ready",
        EntityWorkflowState.Blocked => "Blocked",
        EntityWorkflowState.InProgress => "In progress",
        EntityWorkflowState.ReworkNeeded => "Rework needed",
        EntityWorkflowState.DevelopmentCompleted => "Dev. completed",
        EntityWorkflowState.Reconciled => "Reconciled",
        EntityWorkflowState.Archived => "Archived",
        _ => status.ToString()
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

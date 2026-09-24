using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using EntityTracker.Application.Projects;
using EntityTracker.Domain;
using EntityTracker.Reporting;
using EntityTracker.Wpf.Commands;

namespace EntityTracker.Wpf.ViewModels;

public sealed class ProjectDashboardViewModel : INotifyPropertyChanged
{
    private readonly PortfolioQueryService _queryService;
    private readonly ProjectEntityComparisonQueryService _comparisonService;
    private readonly AsyncCommand<ProjectComparisonCategory> _selectCategoryCommand;
    private readonly AsyncCommand _clearCategoryCommand;
    private readonly SemaphoreSlim _comparisonGate = new(1, 1);
    private ProjectDashboard? _dashboard;
    private ProjectEntityComparison? _comparison;
    private IReadOnlyList<ProjectComparisonDisplayRow> _comparisonRows = [];
    private bool _showAllEntities;
    private ProjectComparisonCategory _selectedCategory;
    private string? _errorMessage;
    private bool _isBusy;

    public ProjectDashboardViewModel(
        ProjectId projectId,
        PortfolioQueryService queryService,
        ProjectEntityComparisonQueryService comparisonService,
        AggregateProgressReportingService reportingService,
        ProgressChartPresentationBuilder presentationBuilder)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        ArgumentNullException.ThrowIfNull(queryService);
        ArgumentNullException.ThrowIfNull(comparisonService);
        ProjectId = projectId;
        _queryService = queryService;
        _comparisonService = comparisonService;
        _selectCategoryCommand = new AsyncCommand<ProjectComparisonCategory>(
            SelectCategoryAsync,
            category => GetCategoryCount(category) > 0);
        _clearCategoryCommand = new AsyncCommand(
            ClearCategoryAsync,
            () => HasCategoryFilter);
        Progress = new AggregateProgressDashboardViewModel(
            (range, cancellationToken) => reportingService.GetProjectReportAsync(
                projectId,
                range,
                cancellationToken),
            presentationBuilder);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ProjectId ProjectId { get; }

    public AggregateProgressDashboardViewModel Progress { get; }

    public ProjectDashboard? Dashboard
    {
        get => _dashboard;
        private set
        {
            if (SetField(ref _dashboard, value))
            {
                OnPropertyChanged(nameof(HasTrackers));
            }
        }
    }

    public ProjectEntityComparison? Comparison
    {
        get => _comparison;
        private set
        {
            if (SetField(ref _comparison, value))
            {
                ComparisonRows = CreateDisplayRows(value);
                OnPropertyChanged(nameof(HasComparisonRows));
                OnPropertyChanged(nameof(ShowNoActionableRows));
                OnPropertyChanged(nameof(ShowNoComparisonEntities));
                OnPropertyChanged(nameof(ComparisonSummary));
                NotifyCategoryPropertiesChanged();
                _selectCategoryCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool ShowAllEntities
    {
        get => _showAllEntities;
        set
        {
            if (SetField(ref _showAllEntities, value))
            {
                if (value && _selectedCategory != ProjectComparisonCategory.None)
                {
                    _selectedCategory = ProjectComparisonCategory.None;
                    NotifyCategoryPropertiesChanged();
                }

                OnPropertyChanged(nameof(ShowNoActionableRows));
                OnPropertyChanged(nameof(ComparisonSummary));
                _ = LoadComparisonSafelyAsync();
            }
        }
    }

    public IReadOnlyList<ProjectComparisonDisplayRow> ComparisonRows
    {
        get => _comparisonRows;
        private set
        {
            if (SetField(ref _comparisonRows, value))
            {
                OnPropertyChanged(nameof(HasComparisonRows));
            }
        }
    }

    public bool HasTrackers => Dashboard?.Trackers.Count > 0;

    public bool HasComparisonRows => Comparison?.Rows.Count > 0;

    public bool ShowNoActionableRows =>
        Comparison is { Rows.Count: 0, TotalEntityCount: > 0 } && !ShowAllEntities;

    public bool ShowNoComparisonEntities => Comparison is { TotalEntityCount: 0 };

    public string ComparisonEmptyTitle => HasCategoryFilter
        ? $"No {FormatCategory(SelectedCategory).ToLowerInvariant()} entities"
        : "No actionable differences";

    public string ComparisonEmptyHint => HasCategoryFilter
        ? "Choose the selected category again or clear the filter."
        : "Choose Show all to inspect matching entities.";

    public ProjectComparisonCategory SelectedCategory => _selectedCategory;

    public bool HasCategoryFilter => SelectedCategory != ProjectComparisonCategory.None;

    public bool IsMissingSelected => SelectedCategory == ProjectComparisonCategory.Missing;

    public bool IsDivergentSelected => SelectedCategory == ProjectComparisonCategory.Divergent;

    public bool IsBlockedSelected => SelectedCategory == ProjectComparisonCategory.Blocked;

    public bool IsReworkSelected => SelectedCategory == ProjectComparisonCategory.ReworkNeeded;

    public bool IsUnresolvedSelected => SelectedCategory == ProjectComparisonCategory.Unresolved;

    public int MissingCount => Comparison?.CategoryCounts.Missing ?? 0;

    public int DivergentCount => Comparison?.CategoryCounts.Divergent ?? 0;

    public int BlockedCount => Comparison?.CategoryCounts.Blocked ?? 0;

    public int ReworkCount => Comparison?.CategoryCounts.ReworkNeeded ?? 0;

    public int UnresolvedCount => Comparison?.CategoryCounts.Unresolved ?? 0;

    public ICommand SelectCategoryCommand => _selectCategoryCommand;

    public ICommand ClearCategoryCommand => _clearCategoryCommand;

    public string ComparisonSummary => Comparison is null
        ? string.Empty
        : ShowAllEntities
            ? $"Showing all {Comparison.TotalEntityCount} entities"
            : HasCategoryFilter
                ? $"Showing {Comparison.Rows.Count} {FormatCategory(SelectedCategory).ToLowerInvariant()} of {Comparison.TotalEntityCount} entities"
                : $"Showing {Comparison.ActionableEntityCount} actionable of {Comparison.TotalEntityCount} entities";

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            Task<ProjectDashboard?> dashboardTask =
                _queryService.GetProjectAsync(ProjectId, cancellationToken);
            Task progressTask = Progress.LoadAsync(cancellationToken);
            Task comparisonTask = LoadComparisonAsync(cancellationToken);
            await Task.WhenAll(dashboardTask, progressTask, comparisonTask);
            Dashboard = await dashboardTask;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ErrorMessage = "Loading the project dashboard was cancelled.";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"The project dashboard could not be loaded: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadComparisonAsync(CancellationToken cancellationToken = default)
    {
        await _comparisonGate.WaitAsync(cancellationToken);
        try
        {
            Comparison = await _comparisonService.GetAsync(
                ProjectId,
                ShowAllEntities
                    ? ProjectComparisonFilter.All
                    : ProjectComparisonFilter.ActionableDifferences,
                ShowAllEntities ? ProjectComparisonCategory.None : SelectedCategory,
                cancellationToken);
        }
        finally
        {
            _comparisonGate.Release();
        }
    }

    private async Task SelectCategoryAsync(ProjectComparisonCategory category)
    {
        if (GetCategoryCount(category) == 0)
        {
            return;
        }

        if (_showAllEntities)
        {
            _showAllEntities = false;
            OnPropertyChanged(nameof(ShowAllEntities));
        }

        _selectedCategory = _selectedCategory == category
            ? ProjectComparisonCategory.None
            : category;
        NotifyCategoryPropertiesChanged();
        await LoadComparisonSafelyAsync();
    }

    private async Task ClearCategoryAsync()
    {
        if (!HasCategoryFilter)
        {
            return;
        }

        _selectedCategory = ProjectComparisonCategory.None;
        NotifyCategoryPropertiesChanged();
        await LoadComparisonSafelyAsync();
    }

    private int GetCategoryCount(ProjectComparisonCategory category) => category switch
    {
        ProjectComparisonCategory.Missing => MissingCount,
        ProjectComparisonCategory.Divergent => DivergentCount,
        ProjectComparisonCategory.Blocked => BlockedCount,
        ProjectComparisonCategory.ReworkNeeded => ReworkCount,
        ProjectComparisonCategory.Unresolved => UnresolvedCount,
        _ => 0
    };

    private void NotifyCategoryPropertiesChanged()
    {
        OnPropertyChanged(nameof(SelectedCategory));
        OnPropertyChanged(nameof(HasCategoryFilter));
        OnPropertyChanged(nameof(IsMissingSelected));
        OnPropertyChanged(nameof(IsDivergentSelected));
        OnPropertyChanged(nameof(IsBlockedSelected));
        OnPropertyChanged(nameof(IsReworkSelected));
        OnPropertyChanged(nameof(IsUnresolvedSelected));
        OnPropertyChanged(nameof(MissingCount));
        OnPropertyChanged(nameof(DivergentCount));
        OnPropertyChanged(nameof(BlockedCount));
        OnPropertyChanged(nameof(ReworkCount));
        OnPropertyChanged(nameof(UnresolvedCount));
        OnPropertyChanged(nameof(ComparisonSummary));
        OnPropertyChanged(nameof(ShowNoActionableRows));
        OnPropertyChanged(nameof(ComparisonEmptyTitle));
        OnPropertyChanged(nameof(ComparisonEmptyHint));
        _clearCategoryCommand.NotifyCanExecuteChanged();
    }

    private static string FormatCategory(ProjectComparisonCategory category) => category switch
    {
        ProjectComparisonCategory.Missing => "Missing",
        ProjectComparisonCategory.Divergent => "Divergent",
        ProjectComparisonCategory.Blocked => "Blocked",
        ProjectComparisonCategory.ReworkNeeded => "Rework",
        ProjectComparisonCategory.Unresolved => "Unresolved",
        _ => "Actionable"
    };

    private async Task LoadComparisonSafelyAsync()
    {
        try
        {
            ErrorMessage = null;
            await LoadComparisonAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = $"The project comparison could not be loaded: {exception.Message}";
        }
    }

    private static IReadOnlyList<ProjectComparisonDisplayRow> CreateDisplayRows(
        ProjectEntityComparison? comparison)
    {
        if (comparison is null)
        {
            return [];
        }

        return comparison.Rows.Select(row => new ProjectComparisonDisplayRow(
            row.NormalizedSourceKey,
            row.DisplayName,
            row.Cells.Select((cell, index) => ProjectComparisonDisplayCell.Create(
                row.DisplayName,
                comparison.Trackers[index].Name,
                cell)).ToArray(),
            row.IsActionable)).ToArray();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

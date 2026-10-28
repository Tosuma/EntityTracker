using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using EntityTracker.Application.Projects;
using EntityTracker.Domain;
using EntityTracker.Reporting;
using EntityTracker.Wpf.Commands;
using EntityTracker.Wpf.Services;
using EntityTracker.Application.Diagnostics;
using Microsoft.Extensions.Logging;

namespace EntityTracker.Wpf.ViewModels;

public sealed class ProjectDashboardViewModel : INotifyPropertyChanged
{
    private static readonly ILogger Logger = AppLogging.CreateLogger<ProjectDashboardViewModel>();

    private readonly PortfolioQueryService _queryService;
    private readonly ProjectEntityComparisonQueryService _comparisonService;
    private readonly AsyncCommand<ProjectComparisonCategory> _selectCategoryCommand;
    private readonly AsyncCommand _clearCategoryCommand;
    private readonly SemaphoreSlim _comparisonGate = new(1, 1);
    private ProjectDashboard? _dashboard;
    private ProjectEntityComparison? _comparison;
    private IReadOnlyList<ProjectComparisonDisplayRow> _comparisonRows = [];
    private ProjectComparisonCategory _selectedCategories;
    private readonly NotificationCenter? _notifications;
    private bool _hasError;
    private bool _isBusy;

    public ProjectDashboardViewModel(
        ProjectId projectId,
        PortfolioQueryService queryService,
        ProjectEntityComparisonQueryService comparisonService,
        AggregateProgressReportingService reportingService,
        ProgressChartPresentationBuilder presentationBuilder,
        ProjectRepositoryCardViewModel? repositoryCard = null,
        NotificationCenter? notifications = null)
    {
        _notifications = notifications;
        ArgumentNullException.ThrowIfNull(projectId);
        ArgumentNullException.ThrowIfNull(queryService);
        ArgumentNullException.ThrowIfNull(comparisonService);
        ProjectId = projectId;
        _queryService = queryService;
        _comparisonService = comparisonService;
        RepositoryCard = repositoryCard;
        _selectCategoryCommand = new AsyncCommand<ProjectComparisonCategory>(
            SelectCategoryAsync,
            category => GetCategoryCount(category) > 0 || SelectedCategories.HasFlag(category));
        _clearCategoryCommand = new AsyncCommand(ClearCategoryAsync);
        Progress = new AggregateProgressDashboardViewModel(
            (range, cancellationToken) => reportingService.GetProjectReportAsync(
                projectId,
                range,
                cancellationToken),
            presentationBuilder,
            notifications);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ProjectId ProjectId { get; }

    public ProjectRepositoryCardViewModel? RepositoryCard { get; }

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
                OnPropertyChanged(nameof(ShowNoMatchingRows));
                OnPropertyChanged(nameof(ShowNoComparisonEntities));
                OnPropertyChanged(nameof(ComparisonSummary));
                NotifyCategoryPropertiesChanged();
                _selectCategoryCommand.NotifyCanExecuteChanged();
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

    public bool ShowNoMatchingRows => Comparison is { Rows.Count: 0, TotalEntityCount: > 0 };

    public bool ShowNoComparisonEntities => Comparison is { TotalEntityCount: 0 };

    // The chosen category cards; they add up, and none chosen (Total) shows every entity.
    public ProjectComparisonCategory SelectedCategories => _selectedCategories;

    public bool HasCategoryFilter => SelectedCategories != ProjectComparisonCategory.None;

    public bool IsTotalSelected => !HasCategoryFilter;

    public bool IsMissingSelected => SelectedCategories.HasFlag(ProjectComparisonCategory.Missing);

    public bool IsDivergentSelected => SelectedCategories.HasFlag(ProjectComparisonCategory.Divergent);

    public bool IsBlockedSelected => SelectedCategories.HasFlag(ProjectComparisonCategory.Blocked);

    public bool IsWaitingOnDependenciesSelected =>
        SelectedCategories.HasFlag(ProjectComparisonCategory.WaitingOnDependencies);

    public bool IsReworkSelected => SelectedCategories.HasFlag(ProjectComparisonCategory.ReworkNeeded);

    public bool IsUnresolvedSelected => SelectedCategories.HasFlag(ProjectComparisonCategory.Unresolved);

    public int TotalEntityCount => Comparison?.TotalEntityCount ?? 0;

    public int MissingCount => Comparison?.CategoryCounts.Missing ?? 0;

    public int DivergentCount => Comparison?.CategoryCounts.Divergent ?? 0;

    public int BlockedCount => Comparison?.CategoryCounts.Blocked ?? 0;

    public int WaitingOnDependenciesCount =>
        Comparison?.CategoryCounts.WaitingOnDependencies ?? 0;

    public int ReworkCount => Comparison?.CategoryCounts.ReworkNeeded ?? 0;

    public int UnresolvedCount => Comparison?.CategoryCounts.Unresolved ?? 0;

    public ICommand SelectCategoryCommand => _selectCategoryCommand;

    public ICommand ClearCategoryCommand => _clearCategoryCommand;

    public string ComparisonSummary => Comparison is null
        ? string.Empty
        : HasCategoryFilter
            ? $"Showing {Comparison.Rows.Count} of {Comparison.TotalEntityCount} entities"
            : $"Showing all {Comparison.TotalEntityCount} entities";

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    /// <summary>Gets whether the last load or save failed; the reason is in the notification center.</summary>
    public bool HasError
    {
        get => _hasError;
        private set => SetField(ref _hasError, value);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        HasError = false;
        try
        {
            Task<ProjectDashboard?> dashboardTask =
                _queryService.GetProjectAsync(ProjectId, cancellationToken);
            Task progressTask = Progress.LoadAsync(cancellationToken);
            Task comparisonTask = LoadComparisonAsync(cancellationToken);
            Task repositoryTask = RepositoryCard?.RefreshAsync(cancellationToken) ?? Task.CompletedTask;
            await Task.WhenAll(dashboardTask, progressTask, comparisonTask, repositoryTask);
            Dashboard = await dashboardTask;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Logger.LogInformation("Loading the project dashboard was cancelled.");
            ReportError("Loading the project dashboard was cancelled.", NotificationKind.Information);
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "The project dashboard could not be loaded.");
            ReportError($"The project dashboard could not be loaded: {exception.Message}");
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
                SelectedCategories,
                cancellationToken);
        }
        finally
        {
            _comparisonGate.Release();
        }
    }

    private async Task SelectCategoryAsync(ProjectComparisonCategory category)
    {
        if (!_selectCategoryCommand.CanExecute(category))
        {
            return;
        }

        // A card adds its entities to the chosen ones; choosing it again takes them away.
        _selectedCategories ^= category;
        NotifyCategoryPropertiesChanged();
        await LoadComparisonSafelyAsync();
    }

    private async Task ClearCategoryAsync()
    {
        if (!HasCategoryFilter)
        {
            return;
        }

        _selectedCategories = ProjectComparisonCategory.None;
        NotifyCategoryPropertiesChanged();
        await LoadComparisonSafelyAsync();
    }

    private int GetCategoryCount(ProjectComparisonCategory category) => category switch
    {
        ProjectComparisonCategory.Missing => MissingCount,
        ProjectComparisonCategory.Divergent => DivergentCount,
        ProjectComparisonCategory.Blocked => BlockedCount,
        ProjectComparisonCategory.WaitingOnDependencies => WaitingOnDependenciesCount,
        ProjectComparisonCategory.ReworkNeeded => ReworkCount,
        ProjectComparisonCategory.Unresolved => UnresolvedCount,
        _ => 0
    };

    private void NotifyCategoryPropertiesChanged()
    {
        OnPropertyChanged(nameof(SelectedCategories));
        OnPropertyChanged(nameof(HasCategoryFilter));
        OnPropertyChanged(nameof(IsTotalSelected));
        OnPropertyChanged(nameof(IsMissingSelected));
        OnPropertyChanged(nameof(IsDivergentSelected));
        OnPropertyChanged(nameof(IsBlockedSelected));
        OnPropertyChanged(nameof(IsWaitingOnDependenciesSelected));
        OnPropertyChanged(nameof(IsReworkSelected));
        OnPropertyChanged(nameof(IsUnresolvedSelected));
        OnPropertyChanged(nameof(TotalEntityCount));
        OnPropertyChanged(nameof(MissingCount));
        OnPropertyChanged(nameof(DivergentCount));
        OnPropertyChanged(nameof(BlockedCount));
        OnPropertyChanged(nameof(WaitingOnDependenciesCount));
        OnPropertyChanged(nameof(ReworkCount));
        OnPropertyChanged(nameof(UnresolvedCount));
        OnPropertyChanged(nameof(ComparisonSummary));
        OnPropertyChanged(nameof(ShowNoMatchingRows));
        _selectCategoryCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadComparisonSafelyAsync()
    {
        try
        {
            HasError = false;
            await LoadComparisonAsync();
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "The project comparison could not be loaded.");
            ReportError($"The project comparison could not be loaded: {exception.Message}");
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
                cell)).ToArray())).ToArray();
    }

    private void ReportError(string message, NotificationKind kind = NotificationKind.Failure)
    {
        HasError = true;
        _notifications?.Show("Project dashboard", message, kind);
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

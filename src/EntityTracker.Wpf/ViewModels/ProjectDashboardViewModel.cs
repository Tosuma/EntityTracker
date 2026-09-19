using System.ComponentModel;
using System.Runtime.CompilerServices;

using EntityTracker.Application.Projects;
using EntityTracker.Domain;
using EntityTracker.Reporting;

namespace EntityTracker.Wpf.ViewModels;

public sealed class ProjectDashboardViewModel : INotifyPropertyChanged
{
    private readonly PortfolioQueryService _queryService;
    private readonly ProjectEntityComparisonQueryService _comparisonService;
    private readonly SemaphoreSlim _comparisonGate = new(1, 1);
    private ProjectDashboard? _dashboard;
    private ProjectEntityComparison? _comparison;
    private IReadOnlyList<ProjectComparisonDisplayRow> _comparisonRows = [];
    private bool _showAllEntities;
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

    public string ComparisonSummary => Comparison is null
        ? string.Empty
        : ShowAllEntities
            ? $"Showing all {Comparison.TotalEntityCount} entities"
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
                cancellationToken);
        }
        finally
        {
            _comparisonGate.Release();
        }
    }

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

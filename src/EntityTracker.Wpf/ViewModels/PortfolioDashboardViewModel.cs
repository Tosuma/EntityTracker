using System.ComponentModel;
using System.Runtime.CompilerServices;

using EntityTracker.Application.Projects;
using EntityTracker.Reporting;

namespace EntityTracker.Wpf.ViewModels;

public sealed class PortfolioDashboardViewModel : INotifyPropertyChanged
{
    private readonly PortfolioQueryService _queryService;
    private PortfolioDashboard? _dashboard;
    private string? _errorMessage;
    private bool _isBusy;

    public PortfolioDashboardViewModel(
        PortfolioQueryService queryService,
        AggregateProgressReportingService reportingService,
        ProgressChartPresentationBuilder presentationBuilder)
    {
        ArgumentNullException.ThrowIfNull(queryService);
        ArgumentNullException.ThrowIfNull(reportingService);
        _queryService = queryService;
        Progress = new AggregateProgressDashboardViewModel(
            async (range, cancellationToken) => await reportingService.GetPortfolioReportAsync(
                range,
                cancellationToken),
            presentationBuilder);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AggregateProgressDashboardViewModel Progress { get; }

    public PortfolioDashboard? Dashboard
    {
        get => _dashboard;
        private set
        {
            if (SetField(ref _dashboard, value))
            {
                OnPropertyChanged(nameof(HasProjects));
            }
        }
    }

    public bool HasProjects => Dashboard?.Projects.Count > 0;

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
            Dashboard = await _queryService.GetPortfolioAsync(cancellationToken);
            await Progress.LoadAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ErrorMessage = "Loading the portfolio was cancelled.";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"The portfolio could not be loaded: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
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

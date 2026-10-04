using System.ComponentModel;
using System.Runtime.CompilerServices;

using EntityTracker.Application.Projects;
using EntityTracker.Reporting;
using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf.ViewModels;

public sealed class PortfolioDashboardViewModel : INotifyPropertyChanged
{
    private readonly PortfolioQueryService _queryService;
    private PortfolioDashboard? _dashboard;
    private readonly NotificationCenter? _notifications;
    private bool _hasError;
    private bool _isBusy;

    public PortfolioDashboardViewModel(
        PortfolioQueryService queryService,
        AggregateProgressReportingService reportingService,
        ProgressChartPresentationBuilder presentationBuilder,
        NotificationCenter? notifications = null)
    {
        _notifications = notifications;
        ArgumentNullException.ThrowIfNull(queryService);
        ArgumentNullException.ThrowIfNull(reportingService);
        _queryService = queryService;
        Progress = new AggregateProgressDashboardViewModel(
            async (range, cancellationToken) => await reportingService.GetPortfolioReportAsync(
                range,
                cancellationToken),
            presentationBuilder,
            notifications);
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
            Dashboard = await _queryService.GetPortfolioAsync(cancellationToken);
            await Progress.LoadAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ReportError("Loading the portfolio was cancelled.", NotificationKind.Information);
        }
        catch (Exception exception)
        {
            ReportError($"The portfolio could not be loaded: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ReportError(string message, NotificationKind kind = NotificationKind.Failure)
    {
        HasError = true;
        _notifications?.Show("Portfolio", message, kind);
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

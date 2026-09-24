using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using EntityTracker.Reporting;
using EntityTracker.Wpf.Commands;

using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace EntityTracker.Wpf.ViewModels;

public sealed class AggregateProgressDashboardViewModel : INotifyPropertyChanged
{
    private readonly Func<ProgressDateRange, CancellationToken, Task<ProgressDashboardReport?>> _loader;
    private readonly ProgressChartPresentationBuilder _presentationBuilder;
    private readonly AsyncCommand _applyRangeCommand;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private ProgressRangePreset _selectedRange = ProgressRangePreset.AllHistory;
    private DateTime? _customFrom = DateTime.Today.AddDays(-29);
    private DateTime? _customTo = DateTime.Today;
    private ProgressDashboardReport? _report;
    private ISeries[] _currentStatusSeries = [];
    private ISeries[] _implementedSeries = [];
    private Axis[] _implementedXAxes = [];
    private Axis[] _countYAxes = [];
    private string? _errorMessage;
    private bool _isBusy;
    private bool _hasLoaded;

    public AggregateProgressDashboardViewModel(
        Func<ProgressDateRange, CancellationToken, Task<ProgressDashboardReport?>> loader,
        ProgressChartPresentationBuilder presentationBuilder)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(presentationBuilder);
        _loader = loader;
        _presentationBuilder = presentationBuilder;
        _applyRangeCommand = new AsyncCommand(
            () => LoadAsync(),
            () => !IsBusy && IsCustomRangeValid);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<ProgressRangeOption> RangeOptions { get; } =
    [
        new(ProgressRangePreset.AllHistory, "All history"),
        new(ProgressRangePreset.Last30Days, "Last 30 days"),
        new(ProgressRangePreset.Last60Days, "Last 60 days"),
        new(ProgressRangePreset.Last90Days, "Last 90 days"),
        new(ProgressRangePreset.Custom, "Custom range")
    ];

    public ProgressRangePreset SelectedRange
    {
        get => _selectedRange;
        set
        {
            if (!SetField(ref _selectedRange, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsCustomRange));
            NotifyRangeValidationChanged();
            if (_hasLoaded && value != ProgressRangePreset.Custom)
            {
                _ = LoadAsync();
            }
        }
    }

    public DateTime? CustomFrom
    {
        get => _customFrom;
        set
        {
            if (SetField(ref _customFrom, value))
            {
                NotifyRangeValidationChanged();
            }
        }
    }

    public DateTime? CustomTo
    {
        get => _customTo;
        set
        {
            if (SetField(ref _customTo, value))
            {
                NotifyRangeValidationChanged();
            }
        }
    }

    public bool IsCustomRange => SelectedRange == ProgressRangePreset.Custom;

    public bool IsCustomRangeValid => !IsCustomRange ||
        CustomFrom is not null && CustomTo is not null &&
        CustomFrom.Value.Date <= CustomTo.Value.Date &&
        CustomTo.Value.Date <= DateTime.Today;

    public bool HasRangeValidationError => IsCustomRange && !IsCustomRangeValid;

    public string RangeValidationMessage => IsCustomRangeValid
        ? string.Empty
        : "Choose dates where From is not after To and To is not in the future.";

    public ProgressDashboardReport? Report
    {
        get => _report;
        private set
        {
            if (SetField(ref _report, value))
            {
                OnPropertyChanged(nameof(HasReport));
                OnPropertyChanged(nameof(HasHistoricalData));
                OnPropertyChanged(nameof(ShowNoHistoricalData));
            }
        }
    }

    public bool HasReport => Report is not null;

    public bool HasHistoricalData => Report?.HasHistoricalData == true;

    public bool ShowNoHistoricalData => _hasLoaded && !IsBusy && !HasHistoricalData && !HasError;

    public ISeries[] CurrentStatusSeries
    {
        get => _currentStatusSeries;
        private set => SetField(ref _currentStatusSeries, value);
    }

    public ISeries[] ImplementedSeries
    {
        get => _implementedSeries;
        private set => SetField(ref _implementedSeries, value);
    }

    public Axis[] ImplementedXAxes
    {
        get => _implementedXAxes;
        private set => SetField(ref _implementedXAxes, value);
    }

    public Axis[] CountYAxes
    {
        get => _countYAxes;
        private set => SetField(ref _countYAxes, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(ShowNoHistoricalData));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                _applyRangeCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(ShowNoHistoricalData));
            }
        }
    }

    public ICommand ApplyRangeCommand => _applyRangeCommand;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!IsCustomRangeValid)
        {
            return;
        }

        await _loadGate.WaitAsync(cancellationToken);
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            ProgressDashboardReport? report = await _loader(CreateRange(), cancellationToken);
            Report = report;
            if (report is null)
            {
                CurrentStatusSeries = [];
                ImplementedSeries = [];
                ImplementedXAxes = [];
                CountYAxes = [];
            }
            else
            {
                ProgressChartPresentation presentation = _presentationBuilder.Build(report);
                CurrentStatusSeries = presentation.CurrentStatusSeries;
                ImplementedSeries = presentation.ImplementedSeries;
                ImplementedXAxes = presentation.ImplementedXAxes;
                CountYAxes = presentation.CountYAxes;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ErrorMessage = "Loading aggregate progress was cancelled.";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Aggregate progress could not be loaded: {exception.Message}";
        }
        finally
        {
            _hasLoaded = true;
            IsBusy = false;
            OnPropertyChanged(nameof(ShowNoHistoricalData));
            _loadGate.Release();
        }
    }

    private ProgressDateRange CreateRange()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        return SelectedRange switch
        {
            ProgressRangePreset.AllHistory => ProgressDateRange.AllHistory,
            ProgressRangePreset.Last30Days => ProgressDateRange.LastDays(30, today),
            ProgressRangePreset.Last60Days => ProgressDateRange.LastDays(60, today),
            ProgressRangePreset.Last90Days => ProgressDateRange.LastDays(90, today),
            ProgressRangePreset.Custom => ProgressDateRange.Inclusive(
                DateOnly.FromDateTime(CustomFrom!.Value),
                DateOnly.FromDateTime(CustomTo!.Value)),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private void NotifyRangeValidationChanged()
    {
        OnPropertyChanged(nameof(IsCustomRangeValid));
        OnPropertyChanged(nameof(HasRangeValidationError));
        OnPropertyChanged(nameof(RangeValidationMessage));
        _applyRangeCommand.NotifyCanExecuteChanged();
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

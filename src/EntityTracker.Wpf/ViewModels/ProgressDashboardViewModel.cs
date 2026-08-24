using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using EntityTracker.Reporting;
using EntityTracker.Wpf.Commands;

using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;

using SkiaSharp;

namespace EntityTracker.Wpf.ViewModels;

public sealed class ProgressDashboardViewModel : INotifyPropertyChanged
{
    private static readonly SolidColorPaint TextPaint = new(new SKColor(82, 97, 106));
    private readonly ProgressReportingService _reportingService;
    private readonly AsyncCommand _applyRangeCommand;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private ProgressRangePreset _selectedRange = ProgressRangePreset.AllHistory;
    private DateTime? _customFrom = DateTime.Today.AddDays(-29);
    private DateTime? _customTo = DateTime.Today;
    private ISeries[] _currentStatusSeries = [];
    private ISeries[] _implementedSeries = [];
    private ISeries[] _readinessSeries = [];
    private ISeries[] _weeklySeries = [];
    private Axis[] _implementedXAxes = [];
    private Axis[] _readinessXAxes = [];
    private Axis[] _weeklyXAxes = [];
    private string? _errorMessage;
    private bool _isBusy;
    private bool _hasHistoricalData;

    public ProgressDashboardViewModel(ProgressReportingService reportingService)
    {
        ArgumentNullException.ThrowIfNull(reportingService);
        _reportingService = reportingService;
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
            if (SetField(ref _selectedRange, value))
            {
                OnPropertyChanged(nameof(IsCustomRange));
                OnPropertyChanged(nameof(IsCustomRangeValid));
                _applyRangeCommand.NotifyCanExecuteChanged();
                if (value != ProgressRangePreset.Custom)
                {
                    _ = LoadAsync();
                }
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

    public string RangeValidationMessage => IsCustomRangeValid
        ? string.Empty
        : "Choose dates where From is not after To and To is not in the future.";

    public bool HasRangeValidationError => IsCustomRange && !IsCustomRangeValid;

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

    public ISeries[] ReadinessSeries
    {
        get => _readinessSeries;
        private set => SetField(ref _readinessSeries, value);
    }

    public ISeries[] WeeklySeries
    {
        get => _weeklySeries;
        private set => SetField(ref _weeklySeries, value);
    }

    public Axis[] ImplementedXAxes
    {
        get => _implementedXAxes;
        private set => SetField(ref _implementedXAxes, value);
    }

    public Axis[] ReadinessXAxes
    {
        get => _readinessXAxes;
        private set => SetField(ref _readinessXAxes, value);
    }

    public Axis[] WeeklyXAxes
    {
        get => _weeklyXAxes;
        private set => SetField(ref _weeklyXAxes, value);
    }

    public Axis[] CountYAxes { get; } =
    [
        new Axis { MinLimit = 0, MinStep = 1, LabelsPaint = TextPaint }
    ];

    public Axis[] SignedYAxes { get; } =
    [
        new Axis { MinStep = 1, LabelsPaint = TextPaint }
    ];

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

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                _applyRangeCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasHistoricalData
    {
        get => _hasHistoricalData;
        private set
        {
            if (SetField(ref _hasHistoricalData, value))
            {
                OnPropertyChanged(nameof(ShowNoHistoricalData));
            }
        }
    }

    public bool ShowNoHistoricalData => !IsBusy && !HasHistoricalData && !HasError;

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
            ProgressDashboardReport report = await _reportingService.GetReportAsync(
                CreateRange(),
                cancellationToken);
            ApplyReport(report);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ErrorMessage = "Loading progress history was cancelled.";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Progress history could not be loaded: {exception.Message}";
        }
        finally
        {
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
            _ => throw new ArgumentOutOfRangeException(nameof(SelectedRange))
        };
    }

    private void ApplyReport(ProgressDashboardReport report)
    {
        Dictionary<ProgressStatusCategory, SKColor> statusColors = new()
        {
            [ProgressStatusCategory.NotStarted] = new SKColor(148, 163, 184),
            [ProgressStatusCategory.InProgress] = new SKColor(23, 107, 135),
            [ProgressStatusCategory.ReworkNeeded] = new SKColor(217, 119, 6),
            [ProgressStatusCategory.DevelopmentCompleted] = new SKColor(37, 99, 235),
            [ProgressStatusCategory.Reconciled] = new SKColor(46, 139, 87)
        };
        CurrentStatusSeries = report.CurrentStatusCounts
            .Select(item => (ISeries)new PieSeries<int>
            {
                Name = FormatStatus(item.Status),
                Values = [item.Count],
                Fill = new SolidColorPaint(statusColors[item.Status])
            })
            .ToArray();

        DateOnly[] implementedDates = report.ImplementedOverTime
            .Select(static point => point.Date)
            .ToArray();
        ImplementedXAxes = CreateDateAxes(implementedDates);
        ReadinessXAxes = CreateDateAxes(report.ReadyAndBlockedOverTime
            .Select(static point => point.Date)
            .ToArray());
        ImplementedSeries =
        [
            new LineSeries<int>
            {
                Name = "Implemented",
                Values = report.ImplementedOverTime.Select(static point => point.ImplementedCount).ToArray(),
                Stroke = new SolidColorPaint(new SKColor(23, 107, 135), 3),
                Fill = new SolidColorPaint(new SKColor(23, 107, 135, 35)),
                GeometrySize = 5
            }
        ];
        ReadinessSeries =
        [
            new LineSeries<int>
            {
                Name = "Ready",
                Values = report.ReadyAndBlockedOverTime.Select(static point => point.ReadyCount).ToArray(),
                Stroke = new SolidColorPaint(new SKColor(46, 139, 87), 3),
                Fill = null,
                GeometrySize = 5
            },
            new LineSeries<int>
            {
                Name = "Blocked",
                Values = report.ReadyAndBlockedOverTime.Select(static point => point.BlockedCount).ToArray(),
                Stroke = new SolidColorPaint(new SKColor(198, 40, 40), 3),
                Fill = null,
                GeometrySize = 5
            }
        ];

        WeeklyXAxes = CreateDateAxes(report.WeeklyNetImplementedChange
            .Select(static point => point.WeekStartingMonday)
            .ToArray());
        int?[] positive = report.WeeklyNetImplementedChange
            .Select(static point => point.NetChange >= 0 ? point.NetChange : (int?)null)
            .ToArray();
        int?[] negative = report.WeeklyNetImplementedChange
            .Select(static point => point.NetChange < 0 ? point.NetChange : (int?)null)
            .ToArray();
        WeeklySeries =
        [
            new ColumnSeries<int?>
            {
                Name = "Increase",
                Values = positive,
                Fill = new SolidColorPaint(new SKColor(46, 139, 87))
            },
            new ColumnSeries<int?>
            {
                Name = "Decrease",
                Values = negative,
                Fill = new SolidColorPaint(new SKColor(198, 40, 40))
            }
        ];
        HasHistoricalData = report.HasHistoricalData;
    }

    private static Axis[] CreateDateAxes(DateOnly[] dates)
    {
        string format = dates.Select(static date => date.Year).Distinct().Take(2).Count() > 1
            ? "dd MMM yy"
            : "dd MMM";
        return
        [
            new Axis
            {
                Labeler = value => FormatDateLabel(value, dates, format),
                LabelsDensity = 1.25f,
                LabelsRotation = 0,
                LabelsPaint = TextPaint,
                MinStep = 1
            }
        ];
    }

    private static string FormatDateLabel(double value, IReadOnlyList<DateOnly> dates, string format)
    {
        int index = (int)Math.Round(value);
        return Math.Abs(value - index) < 0.001 && index >= 0 && index < dates.Count
            ? dates[index].ToString(format)
            : string.Empty;
    }

    private static string FormatStatus(ProgressStatusCategory status) => status switch
    {
        ProgressStatusCategory.NotStarted => "Not started",
        ProgressStatusCategory.InProgress => "In progress",
        ProgressStatusCategory.ReworkNeeded => "Rework needed",
        ProgressStatusCategory.DevelopmentCompleted => "Dev. completed",
        ProgressStatusCategory.Reconciled => "Reconciled",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private void NotifyRangeValidationChanged()
    {
        OnPropertyChanged(nameof(IsCustomRangeValid));
        OnPropertyChanged(nameof(RangeValidationMessage));
        OnPropertyChanged(nameof(HasRangeValidationError));
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

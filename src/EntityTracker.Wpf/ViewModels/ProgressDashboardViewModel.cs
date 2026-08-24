using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using EntityTracker.Reporting;
using EntityTracker.Wpf.Commands;
using EntityTracker.Wpf.Services;

using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace EntityTracker.Wpf.ViewModels;

public sealed class ProgressDashboardViewModel : INotifyPropertyChanged
{
    private readonly ProgressReportingService _reportingService;
    private readonly ProgressChartPresentationBuilder _presentationBuilder;
    private readonly ProgressChartPngExporter _pngExporter;
    private readonly IProgressChartFilePicker _filePicker;
    private readonly IImageClipboard _clipboard;
    private readonly AsyncCommand _applyRangeCommand;
    private readonly AsyncCommand<ProgressChartKind> _saveChartCommand;
    private readonly AsyncCommand<ProgressChartKind> _copyChartCommand;
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
    private Axis[] _countYAxes = [];
    private Axis[] _signedYAxes = [];
    private ProgressDashboardReport? _currentReport;
    private ProgressManagerSummary _managerSummary = ProgressManagerSummary.Empty;
    private string _managerDateSummary = "No progress data is available yet.";
    private string? _exportMessage;
    private string? _errorMessage;
    private bool _isBusy;
    private bool _hasHistoricalData;

    public ProgressDashboardViewModel(
        ProgressReportingService reportingService,
        ProgressChartPresentationBuilder presentationBuilder,
        ProgressChartPngExporter pngExporter,
        IProgressChartFilePicker filePicker,
        IImageClipboard clipboard)
    {
        ArgumentNullException.ThrowIfNull(reportingService);
        ArgumentNullException.ThrowIfNull(presentationBuilder);
        ArgumentNullException.ThrowIfNull(pngExporter);
        ArgumentNullException.ThrowIfNull(filePicker);
        ArgumentNullException.ThrowIfNull(clipboard);
        _reportingService = reportingService;
        _presentationBuilder = presentationBuilder;
        _pngExporter = pngExporter;
        _filePicker = filePicker;
        _clipboard = clipboard;
        _applyRangeCommand = new AsyncCommand(
            () => LoadAsync(),
            () => !IsBusy && IsCustomRangeValid);
        _saveChartCommand = new AsyncCommand<ProgressChartKind>(SaveChartAsync, _ => CanExportCharts);
        _copyChartCommand = new AsyncCommand<ProgressChartKind>(CopyChartAsync, _ => CanExportCharts);
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

    public Axis[] CountYAxes
    {
        get => _countYAxes;
        private set => SetField(ref _countYAxes, value);
    }

    public Axis[] SignedYAxes
    {
        get => _signedYAxes;
        private set => SetField(ref _signedYAxes, value);
    }

    public ProgressManagerSummary ManagerSummary
    {
        get => _managerSummary;
        private set => SetField(ref _managerSummary, value);
    }

    public string ManagerDateSummary
    {
        get => _managerDateSummary;
        private set => SetField(ref _managerDateSummary, value);
    }

    public string? ExportMessage
    {
        get => _exportMessage;
        private set
        {
            if (SetField(ref _exportMessage, value))
            {
                OnPropertyChanged(nameof(HasExportMessage));
            }
        }
    }

    public bool HasExportMessage => !string.IsNullOrWhiteSpace(ExportMessage);

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
                _saveChartCommand.NotifyCanExecuteChanged();
                _copyChartCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanExportCharts));
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

    public bool HasReport => _currentReport is not null;

    public bool CanExportCharts => !IsBusy && _currentReport?.HasHistoricalData == true;

    public ICommand ApplyRangeCommand => _applyRangeCommand;

    public ICommand SaveChartCommand => _saveChartCommand;

    public ICommand CopyChartCommand => _copyChartCommand;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!IsCustomRangeValid)
        {
            return;
        }

        await _loadGate.WaitAsync(cancellationToken);
        IsBusy = true;
        ErrorMessage = null;
        ExportMessage = null;
        try
        {
            ProgressDashboardReport report = await _reportingService.GetReportAsync(CreateRange(), cancellationToken);
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
        ProgressChartPresentation presentation = _presentationBuilder.Build(report);
        CurrentStatusSeries = presentation.CurrentStatusSeries;
        ImplementedSeries = presentation.ImplementedSeries;
        ReadinessSeries = presentation.ReadinessSeries;
        WeeklySeries = presentation.WeeklySeries;
        ImplementedXAxes = presentation.ImplementedXAxes;
        ReadinessXAxes = presentation.ReadinessXAxes;
        WeeklyXAxes = presentation.WeeklyXAxes;
        CountYAxes = presentation.CountYAxes;
        SignedYAxes = presentation.SignedYAxes;
        _currentReport = report;
        ManagerSummary = report.ManagerSummary;
        ManagerDateSummary = CreateManagerDateSummary(report);
        HasHistoricalData = report.HasHistoricalData;
        OnPropertyChanged(nameof(HasReport));
        OnPropertyChanged(nameof(CanExportCharts));
        _saveChartCommand.NotifyCanExecuteChanged();
        _copyChartCommand.NotifyCanExecuteChanged();
    }

    private async Task SaveChartAsync(ProgressChartKind kind)
    {
        if (_currentReport?.HasHistoricalData != true)
        {
            return;
        }

        string? path = _filePicker.SelectPngPath(CreateSuggestedFileName(kind, _currentReport));
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        ExportMessage = null;
        try
        {
            await _pngExporter.SavePngAsync(_currentReport, kind, path);
            ExportMessage = $"Saved {Path.GetFileName(path)}.";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"The chart could not be saved: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CopyChartAsync(ProgressChartKind kind)
    {
        if (_currentReport?.HasHistoricalData != true)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        ExportMessage = null;
        try
        {
            byte[] png = await Task.Run(() => _pngExporter.RenderPng(_currentReport, kind));
            _clipboard.SetPng(png);
            ExportMessage = $"Copied {ProgressChartPresentationBuilder.GetTitle(kind)}.";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"The chart could not be copied: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string CreateSuggestedFileName(ProgressChartKind kind, ProgressDashboardReport report)
    {
        string date = report.ManagerSummary.DataAsOfDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
            ?? DateOnly.FromDateTime(DateTime.Today).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return $"entitytracker-{ProgressChartPresentationBuilder.GetFileNameSegment(kind)}-{date}.png";
    }

    private static string CreateManagerDateSummary(ProgressDashboardReport report)
    {
        string dataDate = report.ManagerSummary.DataAsOfDate?.ToString("dd MMM yyyy", CultureInfo.CurrentCulture)
            ?? "not available";
        if (report.EffectiveFrom is null || report.EffectiveTo is null)
        {
            return $"Data as of {dataDate} · No progress history in the selected range";
        }

        string historyFrom = report.EffectiveFrom.Value.ToString("dd MMM yyyy", CultureInfo.CurrentCulture);
        string historyTo = report.EffectiveTo.Value.ToString("dd MMM yyyy", CultureInfo.CurrentCulture);
        return $"Data as of {dataDate} · History shown {historyFrom}–{historyTo}";
    }

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

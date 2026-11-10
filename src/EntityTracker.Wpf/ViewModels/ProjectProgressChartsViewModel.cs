using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using EntityTracker.Domain;
using EntityTracker.Reporting;
using EntityTracker.Reporting.ProjectReports;
using EntityTracker.Wpf.Commands;
using EntityTracker.Wpf.Services;
using EntityTracker.Application.Diagnostics;
using Microsoft.Extensions.Logging;

namespace EntityTracker.Wpf.ViewModels;

/// <summary>What the charts show: all chosen Trackers together (<see cref="Tracker"/> is null), or one.</summary>
public sealed record ProgressChartScope(Tracker? Tracker, string Name);

/// <summary>
/// The Project report page's live progress charts: the report's four charts in the app, for all
/// chosen Trackers together or one of them, over the page's progress period. Each chart can be
/// saved or copied as an image.
/// </summary>
public sealed class ProjectProgressChartsViewModel : INotifyPropertyChanged, IChartThemeTarget
{
    private static readonly ILogger Logger = AppLogging.CreateLogger<ProjectProgressChartsViewModel>();

    private static readonly ProgressChartScope AllScope = new(null, "All chosen Trackers");

    private readonly ProjectId _projectId;
    private readonly string _projectName;
    private readonly ProjectReportBuilder _builder;
    private readonly IProjectReportFiles _files;
    private readonly NotificationCenter? _notifications;
    private readonly Func<IReadOnlyList<Tracker>> _chosenTrackers;
    private readonly Func<ProgressDateRange?> _range;
    private readonly Func<ReportAudience> _audience;
    private readonly ProgressChartPresentationBuilder _presentationBuilder = new();
    private readonly ProgressChartPngExporter _exporter;
    private readonly AsyncCommand<ProgressChartKind> _saveChartCommand;
    private readonly AsyncCommand<ProgressChartKind> _copyChartCommand;
    private ProgressChartScope _selectedScope = AllScope;
    private ProgressDashboardReport? _report;
    private ProgressChartTheme _chartTheme;
    private ProgressChartPresentation? _presentation;
    private CancellationTokenSource? _loading;
    private bool _isBusy;
    private bool _isExporting;

    public ProjectProgressChartsViewModel(
        ProjectId projectId,
        string projectName,
        ProjectReportBuilder builder,
        IProjectReportFiles files,
        Func<IReadOnlyList<Tracker>> chosenTrackers,
        Func<ProgressDateRange?> range,
        NotificationCenter? notifications = null,
        Func<ReportAudience>? audience = null)
    {
        _projectId = projectId ?? throw new ArgumentNullException(nameof(projectId));
        _projectName = projectName;
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _chosenTrackers = chosenTrackers ?? throw new ArgumentNullException(nameof(chosenTrackers));
        _range = range ?? throw new ArgumentNullException(nameof(range));
        _notifications = notifications;
        _audience = audience ?? (static () => ReportAudience.Internal);
        _exporter = new ProgressChartPngExporter(_presentationBuilder);
        _saveChartCommand = new AsyncCommand<ProgressChartKind>(SaveChartAsync, _ => CanExportCharts);
        _copyChartCommand = new AsyncCommand<ProgressChartKind>(CopyChartAsync, _ => CanExportCharts);
        RefreshScopes(reload: false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProgressChartScope> ScopeOptions { get; } = [];

    /// <summary>Gets or sets whose progress the charts show; changing it reloads them.</summary>
    public ProgressChartScope SelectedScope
    {
        get => _selectedScope;
        set
        {
            if (value is null || !SetField(ref _selectedScope, value)) return;
            _ = ReloadAsync();
        }
    }

    /// <summary>Gets the charts as LiveCharts series and axes, or null before anything is loaded.</summary>
    public ProgressChartPresentation? Presentation
    {
        get => _presentation;
        private set => SetField(ref _presentation, value);
    }

    /// <summary>The app theme the on-screen charts are drawn for; saved and copied images stay light.</summary>
    public ProgressChartTheme ChartTheme
    {
        get => _chartTheme;
        set
        {
            if (SetField(ref _chartTheme, value) && _report is { } report)
                Presentation = _presentationBuilder.Build(report, value);
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            NotifyState();
        }
    }

    public bool HasHistoricalData => _report?.HasHistoricalData == true;

    public bool ShowNoHistoricalData => !IsBusy && _report is not null && !HasHistoricalData;

    public bool CanExportCharts => !IsBusy && !_isExporting && HasHistoricalData;

    /// <summary>Gets a line such as "Data as of 06 Oct 2026 · History shown 01 Jul 2026–06 Oct 2026".</summary>
    public string DateSummary
    {
        get
        {
            if (_report is null) return string.Empty;
            string dataDate = _report.ManagerSummary.DataAsOfDate?.ToString("dd MMM yyyy", CultureInfo.CurrentCulture)
                ?? "not available";
            return _report.EffectiveFrom is null || _report.EffectiveTo is null
                ? $"Data as of {dataDate} · No progress history in the selected period"
                : $"Data as of {dataDate} · History shown " +
                  $"{_report.EffectiveFrom.Value.ToString("dd MMM yyyy", CultureInfo.CurrentCulture)}–" +
                  $"{_report.EffectiveTo.Value.ToString("dd MMM yyyy", CultureInfo.CurrentCulture)}";
        }
    }

    public ICommand SaveChartCommand => _saveChartCommand;

    public ICommand CopyChartCommand => _copyChartCommand;

    /// <summary>Asks which lines a saved or copied "Progress over time" image shows.</summary>
    public ChartLineChoiceViewModel LineChoice { get; } = new();

    /// <summary>
    /// Brings the Tracker choices in line with the Trackers ticked on the page; a Tracker that is no
    /// longer ticked falls back to all chosen Trackers.
    /// </summary>
    public void RefreshScopes(bool reload = true)
    {
        IReadOnlyList<Tracker> chosen = _chosenTrackers();
        ProgressChartScope[] options =
        [
            AllScope,
            .. chosen.Count > 1 ? chosen.Select(static tracker => new ProgressChartScope(tracker, tracker.Name)) : []
        ];
        ProgressChartScope selected = options.FirstOrDefault(option =>
            option.Tracker?.Id == _selectedScope.Tracker?.Id) ?? AllScope;
        ScopeOptions.Clear();
        foreach (ProgressChartScope option in options) ScopeOptions.Add(option);
        _selectedScope = selected;
        OnPropertyChanged(nameof(SelectedScope));
        if (reload) _ = ReloadAsync();
    }

    /// <summary>Loads the charts for the chosen scope and period, replacing any load still running.</summary>
    public async Task ReloadAsync()
    {
        _loading?.Cancel();
        CancellationTokenSource loading = new();
        _loading = loading;
        IReadOnlyList<Tracker> chosen = _chosenTrackers();
        ProgressDateRange? range = _range();
        TrackerId[] trackerIds = SelectedScope.Tracker is { } tracker
            ? [tracker.Id]
            : chosen.Select(static item => item.Id).ToArray();
        if (trackerIds.Length == 0 || range is null)
        {
            Show(null);
            return;
        }

        IsBusy = true;
        try
        {
            ProgressDashboardReport report = await _builder.BuildProgressAsync(
                new ProjectReportRequest(_projectId, trackerIds, ReportAudience.Internal, range), loading.Token);
            if (!loading.IsCancellationRequested) Show(report);
        }
        catch (OperationCanceledException) when (loading.IsCancellationRequested)
        {
            Logger.LogDebug("A newer load superseded this progress chart load.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            Logger.LogError(exception, "The progress charts could not be loaded.");
            Show(null);
            _notifications?.Show("Progress charts", $"The charts could not be loaded: {exception.Message}",
                NotificationKind.Failure);
        }
        finally
        {
            if (ReferenceEquals(_loading, loading)) IsBusy = false;
        }
    }

    private void Show(ProgressDashboardReport? report)
    {
        _report = report;
        Presentation = report is null ? null : _presentationBuilder.Build(report, ChartTheme);
        OnPropertyChanged(nameof(DateSummary));
        NotifyState();
    }

    /// <summary>
    /// Saves a chart; "Progress over time" first asks which lines to show, starting from the lines the
    /// report's audience gets: Implemented and Active entities for a client, all of them otherwise.
    /// </summary>
    private Task SaveChartAsync(ProgressChartKind kind) => ChooseLinesThen(kind, save: true, lines => SaveAsync(kind, lines));

    private Task CopyChartAsync(ProgressChartKind kind) => ChooseLinesThen(kind, save: false, lines => CopyAsync(kind, lines));

    private Task ChooseLinesThen(ProgressChartKind kind, bool save, Func<ProgressOverTimeLines, Task> export)
    {
        if (kind != ProgressChartKind.ImplementedOverTime) return export(ProgressOverTimeLines.All);
        ProgressOverTimeLines ticked = _audience() == ReportAudience.Client
            ? ProgressOverTimeLines.Client
            : ProgressOverTimeLines.All;
        LineChoice.Open(save, ticked, export);
        return Task.CompletedTask;
    }

    private async Task SaveAsync(ProgressChartKind kind, ProgressOverTimeLines lines) => await ExportAsync(kind, async (report, title) =>
    {
        string date = (report.ManagerSummary.DataAsOfDate ?? DateOnly.FromDateTime(DateTime.Today))
            .ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        string? path = _files.SelectChartPath(
            $"{SafeFileName(_projectName)}-{ProgressChartPresentationBuilder.GetFileNameSegment(kind)}-{date}.png");
        if (path is null) return;
        await _exporter.SavePngAsync(report, kind, path, lines: lines);
        _notifications?.Show("Chart image", $"Saved {title} as {Path.GetFileName(path)}.", NotificationKind.Success);
    });

    private async Task CopyAsync(ProgressChartKind kind, ProgressOverTimeLines lines) => await ExportAsync(kind, async (report, title) =>
    {
        byte[] png = await Task.Run(() => _exporter.RenderPng(report, kind, lines));
        _files.CopyChart(png);
        _notifications?.Show("Chart image", $"Copied {title}.", NotificationKind.Success);
    });

    /// <summary>Saves or copies a chart as shown: the loaded scope and period.</summary>
    private async Task ExportAsync(ProgressChartKind kind, Func<ProgressDashboardReport, string, Task> use)
    {
        if (_report is not { HasHistoricalData: true } report) return;
        _isExporting = true;
        NotifyState();
        try
        {
            await use(report, ProgressChartPresentationBuilder.GetTitle(kind));
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or
                                              UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            Logger.LogError(exception, "A chart image could not be created.");
            _notifications?.Show("Chart image", $"The chart image could not be created: {exception.Message}",
                NotificationKind.Failure);
        }
        finally
        {
            _isExporting = false;
            NotifyState();
        }
    }

    private static string SafeFileName(string name) => string.Concat(name.Select(static character =>
        Path.GetInvalidFileNameChars().Contains(character) || character == ' ' ? '-' : character)).ToLowerInvariant();

    private void NotifyState()
    {
        OnPropertyChanged(nameof(HasHistoricalData));
        OnPropertyChanged(nameof(ShowNoHistoricalData));
        OnPropertyChanged(nameof(CanExportCharts));
        _saveChartCommand.NotifyCanExecuteChanged();
        _copyChartCommand.NotifyCanExecuteChanged();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

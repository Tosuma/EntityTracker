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

namespace EntityTracker.Wpf.ViewModels;

/// <summary>A Tracker that can be ticked into the report.</summary>
public sealed class ReportTrackerChoice(Tracker tracker, Action changed) : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Tracker Tracker { get; } = tracker;

    public string Name => Tracker.Name;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            changed();
        }
    }
}

public sealed record ReportAudienceOption(ReportAudience Audience, string Label, string Description);

public sealed record ReportChartOption(ProgressChartKind Kind, string Label);

/// <summary>
/// The Project Report page: choose Trackers, who the report is for and the progress period, then
/// preview it in the browser or export it as one HTML file to hand to the client.
/// </summary>
public sealed class ProjectReportViewModel : INotifyPropertyChanged
{
    private readonly ProjectId _projectId;
    private readonly ProjectReportBuilder _builder;
    private readonly IProjectReportFiles _files;
    private readonly NotificationCenter? _notifications;
    private readonly AsyncCommand _exportCommand;
    private readonly AsyncCommand _previewCommand;
    private readonly AsyncCommand _saveChartCommand;
    private readonly AsyncCommand _copyChartCommand;
    private readonly ProgressChartPngExporter _chartExporter = new(new ProgressChartPresentationBuilder());
    private ProgressChartKind _chart = ProgressChartKind.CurrentStatus;
    private ReportAudience _audience = ReportAudience.Client;
    private ProgressRangePreset _range = ProgressRangePreset.AllHistory;
    private DateTime? _customFrom;
    private DateTime? _customTo;
    private bool _isBusy;

    public ProjectReportViewModel(ProjectId projectId, string projectName, IEnumerable<Tracker> trackers,
        ProjectReportBuilder builder, IProjectReportFiles files, NotificationCenter? notifications = null)
    {
        _projectId = projectId ?? throw new ArgumentNullException(nameof(projectId));
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _notifications = notifications;
        ProjectName = projectName;
        Trackers = new ObservableCollection<ReportTrackerChoice>(trackers
            .OrderBy(static tracker => tracker.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(tracker => new ReportTrackerChoice(tracker, OnSelectionChanged)));
        _exportCommand = new AsyncCommand(ExportAsync, CanRun);
        _previewCommand = new AsyncCommand(PreviewAsync, CanRun);
        _saveChartCommand = new AsyncCommand(SaveChartAsync, CanRun);
        _copyChartCommand = new AsyncCommand(CopyChartAsync, CanRun);
        SelectAllCommand = new RelayCommand(() => SetAll(true));
        SelectNoneCommand = new RelayCommand(() => SetAll(false));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ProjectName { get; }

    public ObservableCollection<ReportTrackerChoice> Trackers { get; }

    public static IReadOnlyList<ReportAudienceOption> AudienceOptions { get; } =
    [
        new(ReportAudience.Client, "Client report",
            "For the client: progress, statuses, Filter active, shared notes, dependencies and the dependency graph. Leaves out internal notes, " +
            "developer names and technical details such as origin and missing references."),
        new(ReportAudience.Internal, "Internal report",
            "For us: everything in the client report, plus internal notes, responsible developers, origin " +
            "and missing dependencies. Do not share it with the client.")
    ];

    public static IReadOnlyList<ProgressRangeOption> RangeOptions { get; } =
    [
        new(ProgressRangePreset.AllHistory, "All history"),
        new(ProgressRangePreset.Last30Days, "Last 30 days"),
        new(ProgressRangePreset.Last60Days, "Last 60 days"),
        new(ProgressRangePreset.Last90Days, "Last 90 days"),
        new(ProgressRangePreset.Custom, "Custom range")
    ];

    /// <summary>Gets the charts that can be saved or copied as images, as the report shows them.</summary>
    public static IReadOnlyList<ReportChartOption> ChartOptions { get; } =
        Enum.GetValues<ProgressChartKind>()
            .Select(kind => new ReportChartOption(kind, ProgressChartPresentationBuilder.GetTitle(kind)))
            .ToArray();

    /// <summary>Gets or sets the chart that Save image and Copy image use.</summary>
    public ProgressChartKind Chart
    {
        get => _chart;
        set => SetField(ref _chart, value);
    }

    public ReportAudience Audience
    {
        get => _audience;
        set
        {
            if (!SetField(ref _audience, value)) return;
            OnPropertyChanged(nameof(AudienceDescription));
            OnPropertyChanged(nameof(IncludedSummary));
        }
    }

    public string AudienceDescription => AudienceOptions.Single(option => option.Audience == Audience).Description;

    public ProgressRangePreset Range
    {
        get => _range;
        set
        {
            if (!SetField(ref _range, value)) return;
            if (value == ProgressRangePreset.Custom && _customFrom is null && _customTo is null)
            {
                // Start from the last 30 days, so a custom range only needs adjusting.
                _customTo = DateTime.Today;
                _customFrom = DateTime.Today.AddDays(-30);
                OnPropertyChanged(nameof(CustomFrom));
                OnPropertyChanged(nameof(CustomTo));
            }

            OnPropertyChanged(nameof(IsCustomRange));
            NotifyRangeValidationChanged();
        }
    }

    /// <summary>Gets or sets the first day of a custom progress period.</summary>
    public DateTime? CustomFrom
    {
        get => _customFrom;
        set
        {
            if (SetField(ref _customFrom, value)) NotifyRangeValidationChanged();
        }
    }

    /// <summary>Gets or sets the last day of a custom progress period.</summary>
    public DateTime? CustomTo
    {
        get => _customTo;
        set
        {
            if (SetField(ref _customTo, value)) NotifyRangeValidationChanged();
        }
    }

    public bool IsCustomRange => Range == ProgressRangePreset.Custom;

    public bool IsCustomRangeValid => !IsCustomRange ||
        CustomFrom is not null && CustomTo is not null &&
        CustomFrom.Value.Date <= CustomTo.Value.Date &&
        CustomTo.Value.Date <= DateTime.Today;

    public bool HasRangeValidationError => !IsCustomRangeValid;

    public string RangeValidationMessage => IsCustomRangeValid
        ? string.Empty
        : "Choose dates where From is not after To and To is not in the future.";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            NotifyCommands();
        }
    }

    public bool HasSelection => Trackers.Any(static choice => choice.IsSelected);

    /// <summary>Gets a short line describing what the export will contain.</summary>
    public string IncludedSummary
    {
        get
        {
            int count = Trackers.Count(static choice => choice.IsSelected);
            string trackers = count == 0 ? "No Tracker selected"
                : count == Trackers.Count && count > 1 ? $"All {count} Trackers"
                : count == 1 ? Trackers.First(static choice => choice.IsSelected).Name
                : $"{count} of {Trackers.Count} Trackers";
            return $"{trackers} · {ProjectReportHtmlWriter.AudienceLabel(Audience)}";
        }
    }

    public ICommand ExportCommand => _exportCommand;
    public ICommand PreviewCommand => _previewCommand;
    public ICommand SaveChartCommand => _saveChartCommand;
    public ICommand CopyChartCommand => _copyChartCommand;
    public ICommand SelectAllCommand { get; }
    public ICommand SelectNoneCommand { get; }

    /// <summary>Builds the report as chosen on the page.</summary>
    public Task<ProjectReport> BuildAsync(CancellationToken cancellationToken = default) =>
        _builder.BuildAsync(Request(), cancellationToken);

    private ProjectReportRequest Request() => new(
        _projectId,
        Trackers.Where(static choice => choice.IsSelected).Select(static choice => choice.Tracker.Id).ToArray(),
        Audience,
        CreateRange());

    private async Task ExportAsync() => await RunAsync(async report =>
    {
        string? path = _files.SelectExportPath(ProjectReportHtmlWriter.SuggestFileName(report));
        if (path is null) return;
        await File.WriteAllTextAsync(path, ProjectReportHtmlWriter.Write(report));
        _notifications?.Show("Project report",
            $"Saved {Path.GetFileName(path)}. It opens offline in any browser.", NotificationKind.Success);
    });

    private async Task PreviewAsync() => await RunAsync(report =>
    {
        _files.OpenPreview(ProjectReportHtmlWriter.Write(report), ProjectReportHtmlWriter.SuggestFileName(report));
        return Task.CompletedTask;
    });

    /// <summary>Saves the chosen chart for the chosen Trackers and period as a PNG image.</summary>
    private async Task SaveChartAsync() => await RunChartAsync(async (report, title) =>
    {
        string date = (report.ManagerSummary.DataAsOfDate ?? DateOnly.FromDateTime(DateTime.Today))
            .ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        string? path = _files.SelectChartPath(
            $"{SafeFileName(ProjectName)}-{ProgressChartPresentationBuilder.GetFileNameSegment(Chart)}-{date}.png");
        if (path is null) return;
        await _chartExporter.SavePngAsync(report, Chart, path);
        _notifications?.Show("Chart image", $"Saved {title} as {Path.GetFileName(path)}.", NotificationKind.Success);
    });

    /// <summary>Copies the chosen chart for the chosen Trackers and period to the clipboard.</summary>
    private async Task CopyChartAsync() => await RunChartAsync(async (report, title) =>
    {
        byte[] png = await Task.Run(() => _chartExporter.RenderPng(report, Chart));
        _files.CopyChart(png);
        _notifications?.Show("Chart image", $"Copied {title}.", NotificationKind.Success);
    });

    private async Task RunChartAsync(Func<ProgressDashboardReport, string, Task> use)
    {
        if (!CanRun()) return;
        IsBusy = true;
        string title = ProgressChartPresentationBuilder.GetTitle(Chart);
        try
        {
            ProgressDashboardReport report = await _builder.BuildProgressAsync(Request());
            if (!report.HasHistoricalData)
            {
                _notifications?.Show("Chart image",
                    "There is no progress history for these Trackers in this period yet.", NotificationKind.Failure);
                return;
            }

            await use(report, title);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or
                                              UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            _notifications?.Show("Chart image", $"The chart image could not be created: {exception.Message}",
                NotificationKind.Failure);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string SafeFileName(string name) => string.Concat(name.Select(static character =>
        Path.GetInvalidFileNameChars().Contains(character) || character == ' ' ? '-' : character)).ToLowerInvariant();

    private async Task RunAsync(Func<ProjectReport, Task> use)
    {
        if (!CanRun()) return;
        IsBusy = true;
        try
        {
            await use(await BuildAsync());
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _notifications?.Show("Project report", $"The report could not be created: {exception.Message}",
                NotificationKind.Failure);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private ProgressDateRange CreateRange()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        return Range switch
        {
            ProgressRangePreset.Last30Days => ProgressDateRange.LastDays(30, today),
            ProgressRangePreset.Last60Days => ProgressDateRange.LastDays(60, today),
            ProgressRangePreset.Last90Days => ProgressDateRange.LastDays(90, today),
            ProgressRangePreset.Custom when IsCustomRangeValid => ProgressDateRange.Inclusive(
                DateOnly.FromDateTime(CustomFrom!.Value), DateOnly.FromDateTime(CustomTo!.Value)),
            _ => ProgressDateRange.AllHistory
        };
    }

    private bool CanRun() => !IsBusy && HasSelection && IsCustomRangeValid;

    private void NotifyRangeValidationChanged()
    {
        OnPropertyChanged(nameof(IsCustomRangeValid));
        OnPropertyChanged(nameof(HasRangeValidationError));
        OnPropertyChanged(nameof(RangeValidationMessage));
        NotifyCommands();
    }

    private void SetAll(bool selected)
    {
        foreach (ReportTrackerChoice choice in Trackers) choice.IsSelected = selected;
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IncludedSummary));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        _exportCommand.NotifyCanExecuteChanged();
        _previewCommand.NotifyCanExecuteChanged();
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

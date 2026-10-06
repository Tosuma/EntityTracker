using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private ReportAudience _audience = ReportAudience.Client;
    private ProgressRangePreset _range = ProgressRangePreset.AllHistory;
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
        SelectAllCommand = new RelayCommand(() => SetAll(true));
        SelectNoneCommand = new RelayCommand(() => SetAll(false));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ProjectName { get; }

    public ObservableCollection<ReportTrackerChoice> Trackers { get; }

    public static IReadOnlyList<ReportAudienceOption> AudienceOptions { get; } =
    [
        new(ReportAudience.Client, "Client report",
            "For the client: progress, statuses, Filter active and dependencies. Leaves out internal notes, " +
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
        new(ProgressRangePreset.Last90Days, "Last 90 days")
    ];

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
        set => SetField(ref _range, value);
    }

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
    public ICommand SelectAllCommand { get; }
    public ICommand SelectNoneCommand { get; }

    /// <summary>Builds the report as chosen on the page.</summary>
    public Task<ProjectReport> BuildAsync(CancellationToken cancellationToken = default) =>
        _builder.BuildAsync(new ProjectReportRequest(
            _projectId,
            Trackers.Where(static choice => choice.IsSelected).Select(static choice => choice.Tracker.Id).ToArray(),
            Audience,
            CreateRange()), cancellationToken);

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
            _ => ProgressDateRange.AllHistory
        };
    }

    private bool CanRun() => !IsBusy && HasSelection;

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

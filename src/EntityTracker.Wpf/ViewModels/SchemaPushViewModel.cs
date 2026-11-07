using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using EntityTracker.Application.Schemas;
using EntityTracker.Domain;
using EntityTracker.Wpf.Commands;
using EntityTracker.Wpf.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntityTracker.Wpf.ViewModels;

/// <summary>One change in the push review, with its two choices as radio buttons; neither is preselected.</summary>
public sealed class SchemaPushChangeItem : INotifyPropertyChanged
{
    public SchemaPushChangeItem(SchemaPushChange change)
    {
        Change = change ?? throw new ArgumentNullException(nameof(change));
        change.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(SchemaPushChange.Choice)) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UseSchema)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(KeepTracker)));
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SchemaPushChange Change { get; }

    /// <summary>A unique group for this change's two radio buttons.</summary>
    public string ChoiceGroup { get; } = Guid.NewGuid().ToString("N");

    public string Description => Change.Description;
    public bool IsConflict => Change.IsConflict;

    /// <summary>Marks a change the Tracker made too, so its own value is about to be replaced.</summary>
    public string ConflictNote => Change.IsConflict ? " This tracker changed it too." : string.Empty;
    public string UseSchemaLabel => Change.UseSchemaLabel;
    public string KeepTrackerLabel => Change.KeepTrackerLabel;

    public bool UseSchema
    {
        get => Change.Choice == SchemaPushChoice.Schema;
        set { if (value) Change.Choice = SchemaPushChoice.Schema; }
    }

    public bool KeepTracker
    {
        get => Change.Choice == SchemaPushChoice.Tracker;
        set { if (value) Change.Choice = SchemaPushChoice.Tracker; }
    }
}

/// <summary>What a push offers one Tracker, with choices for all its changes at once.</summary>
public sealed class SchemaPushTrackerItem : INotifyPropertyChanged
{
    public SchemaPushTrackerItem(string trackerName, IReadOnlyList<SchemaPushChangeItem> changes)
    {
        TrackerName = trackerName;
        Changes = changes;
        UseSchemaForAllCommand = new RelayCommand(() => Choose(SchemaPushChoice.Schema), () => HasChanges);
        KeepTrackerForAllCommand = new RelayCommand(() => Choose(SchemaPushChoice.Tracker), () => HasChanges);
        DecideLaterCommand = new RelayCommand(() => Choose(null), () => HasChanges);
        foreach (SchemaPushChangeItem change in changes)
            change.Change.PropertyChanged += (_, _) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DecidedText)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string TrackerName { get; }
    public IReadOnlyList<SchemaPushChangeItem> Changes { get; }
    public bool HasChanges => Changes.Count > 0;
    public bool IsUpToDate => !HasChanges;

    /// <summary>How far this Tracker's changes are decided: "2 of 3 decided", or that it is skipped.</summary>
    public string DecidedText
    {
        get
        {
            int decided = Changes.Count(static change => change.Change.Choice is not null);
            return decided == 0
                ? "Nothing decided: this tracker is left for a later push."
                : $"{decided} of {Changes.Count} decided";
        }
    }

    public ICommand UseSchemaForAllCommand { get; }
    public ICommand KeepTrackerForAllCommand { get; }
    public ICommand DecideLaterCommand { get; }

    private void Choose(SchemaPushChoice? choice)
    {
        foreach (SchemaPushChangeItem change in Changes) change.Change.Choice = choice;
    }
}

/// <summary>
/// The schema's changes since its last push, and the push review: one review for every Tracker made from
/// the schema, where each change is decided per Tracker. Use schema applies it, Keep tracker declines it, and
/// a change left undecided is offered again at a later push.
/// </summary>
public sealed class SchemaPushViewModel : INotifyPropertyChanged
{
    private readonly SchemaId _schemaId;
    private readonly SchemaTrackerService _service;
    private readonly NotificationCenter? _notifications;
    private readonly ILogger _logger;
    private readonly AsyncCommand _openReviewCommand;
    private readonly AsyncCommand _applyCommand;
    private readonly RelayCommand _useSchemaForAllCommand;
    private readonly RelayCommand _keepTrackersForAllCommand;
    private SchemaPendingChanges? _pending;
    private SchemaPushReview? _review;
    private string? _errorMessage;
    private bool _isEditable;
    private bool _isBusy;

    public SchemaPushViewModel(SchemaId schemaId, SchemaTrackerService service, bool isEditable,
        NotificationCenter? notifications = null, ILogger? logger = null)
    {
        _schemaId = schemaId;
        _service = service;
        _isEditable = isEditable;
        _notifications = notifications;
        _logger = logger ?? NullLogger.Instance;
        PendingChanges = [];
        ReviewTrackers = [];
        _openReviewCommand = new AsyncCommand(OpenReviewAsync, () => !_isBusy && CanPush);
        _applyCommand = new AsyncCommand(ApplyAsync, () => !_isBusy && _review?.CanApply == true);
        _useSchemaForAllCommand = new RelayCommand(() => ChooseAll(SchemaPushChoice.Schema), () => HasReviewChanges);
        _keepTrackersForAllCommand = new RelayCommand(() => ChooseAll(SchemaPushChoice.Tracker), () => HasReviewChanges);
        CancelReviewCommand = new RelayCommand(CloseReview, () => !_isBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after a push was applied, so pages that show the Trackers can reload.</summary>
    public event EventHandler? Pushed;

    public ObservableCollection<string> PendingChanges { get; }
    public bool HasPendingChanges => _pending?.HasChanges == true;
    public bool ShowSummaryOnly => !HasPendingChanges;
    public bool CanPush => _isEditable && HasPendingChanges;

    /// <summary>One line on where the schema's Trackers stand.</summary>
    public string Summary => _pending switch
    {
        null => "",
        { LinkedTrackerCount: 0 } =>
            "No trackers are made from this schema yet. Create one from the Project dashboard: Create tracker, From schema.",
        { HasChanges: false, LinkedTrackerCount: 1 } => "The tracker made from this schema is up to date.",
        { HasChanges: false } p => $"All {p.LinkedTrackerCount} trackers made from this schema are up to date.",
        var p => $"{Count(p.Changes.Count, "change")} not yet pushed to {Count(p.TrackersBehindCount, "tracker")}",
    };

    public ObservableCollection<SchemaPushTrackerItem> ReviewTrackers { get; }
    public bool IsReviewOpen => _review is not null;
    public bool HasReviewChanges => _review?.HasChanges == true;
    public int PendingChoiceCount => _review?.Changes.Count(static c => c.Choice is null) ?? 0;

    /// <summary>How many changes are decided; the undecided ones wait for a later push.</summary>
    public string ReviewSummary => _review is null ? "" : !_review.HasChanges
        ? "Nothing to decide: the trackers already have these changes, so the push only records that."
        : PendingChoiceCount == 0
            ? $"All {Count(_review.Changes.Count, "change")} decided."
            : $"{_review.DecidedCount} of {Count(_review.Changes.Count, "change")} decided. " +
              "Undecided changes aren't pushed now and are offered again next time.";

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public ICommand OpenReviewCommand => _openReviewCommand;
    public ICommand ApplyCommand => _applyCommand;
    public ICommand UseSchemaForAllCommand => _useSchemaForAllCommand;
    public ICommand KeepTrackersForAllCommand => _keepTrackersForAllCommand;
    public ICommand CancelReviewCommand { get; }

    /// <summary>Reads the changes since the last push again.</summary>
    public async Task RefreshAsync(bool isEditable, CancellationToken cancellationToken = default)
    {
        _isEditable = isEditable;
        try
        {
            _pending = await _service.GetPendingChangesAsync(_schemaId, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "The schema's unpushed changes could not be read.");
            _pending = null;
        }
        PendingChanges.Clear();
        foreach (string change in _pending?.Changes ?? []) PendingChanges.Add(change);
        OnPropertyChanged(nameof(HasPendingChanges));
        OnPropertyChanged(nameof(ShowSummaryOnly));
        OnPropertyChanged(nameof(CanPush));
        OnPropertyChanged(nameof(Summary));
        _openReviewCommand.NotifyCanExecuteChanged();
    }

    public async Task OpenReviewAsync()
    {
        await RunAsync(async () =>
        {
            SchemaPushReview review = await _service.ReviewPushAsync(_schemaId);
            SetReview(review);
        });
    }

    private async Task ApplyAsync()
    {
        if (_review is not { } review) return;
        await RunAsync(async () =>
        {
            await _service.ApplyPushAsync(review);
            int trackers = review.Trackers.Count(static tracker =>
                tracker.HasBaselineUpdate || tracker.Changes.Any(static change => change.Choice is not null));
            SetReview(null);
            _notifications?.Show("Schema pushed",
                $"{review.Schema.Schema.Name} was pushed to {Count(trackers, "tracker")}.", NotificationKind.Success);
            await RefreshAsync(_isEditable);
            Pushed?.Invoke(this, EventArgs.Empty);
        });
    }

    private void ChooseAll(SchemaPushChoice choice)
    {
        foreach (SchemaPushChange change in _review?.Changes ?? []) change.Choice = choice;
    }

    private void CloseReview()
    {
        SetReview(null);
        ErrorMessage = null;
    }

    private void SetReview(SchemaPushReview? review)
    {
        if (_review is not null)
            foreach (SchemaPushChange change in _review.Changes) change.PropertyChanged -= OnChoiceChanged;
        _review = review;
        ReviewTrackers.Clear();
        if (review is not null)
        {
            foreach (SchemaPushChange change in review.Changes) change.PropertyChanged += OnChoiceChanged;
            foreach (TrackerPushPlan plan in review.Trackers)
                ReviewTrackers.Add(new SchemaPushTrackerItem(plan.Tracker.Name,
                    plan.Changes.Select(static change => new SchemaPushChangeItem(change)).ToArray()));
        }
        OnPropertyChanged(nameof(IsReviewOpen));
        OnPropertyChanged(nameof(HasReviewChanges));
        OnChoiceChanged(null, EventArgs.Empty);
    }

    private void OnChoiceChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(PendingChoiceCount));
        OnPropertyChanged(nameof(ReviewSummary));
        _applyCommand.NotifyCanExecuteChanged();
        _useSchemaForAllCommand.NotifyCanExecuteChanged();
        _keepTrackersForAllCommand.NotifyCanExecuteChanged();
    }

    private async Task RunAsync(Func<Task> action)
    {
        _isBusy = true;
        ErrorMessage = null;
        NotifyCommands();
        try
        {
            await action();
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "A schema push failed.");
            ErrorMessage = exception.Message;
        }
        finally
        {
            _isBusy = false;
            NotifyCommands();
        }
    }

    private void NotifyCommands()
    {
        _openReviewCommand.NotifyCanExecuteChanged();
        _applyCommand.NotifyCanExecuteChanged();
        ((RelayCommand)CancelReviewCommand).NotifyCanExecuteChanged();
    }

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

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

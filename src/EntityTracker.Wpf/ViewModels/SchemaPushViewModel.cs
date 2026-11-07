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

/// <summary>One conflict in the push review, with its two choices as radio buttons.</summary>
public sealed class SchemaPushConflictItem : INotifyPropertyChanged
{
    public SchemaPushConflictItem(SchemaPushConflict conflict)
    {
        Conflict = conflict ?? throw new ArgumentNullException(nameof(conflict));
        conflict.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(SchemaPushConflict.Choice)) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UseSchema)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(KeepTracker)));
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SchemaPushConflict Conflict { get; }

    /// <summary>A unique group for this conflict's two radio buttons.</summary>
    public string ChoiceGroup { get; } = Guid.NewGuid().ToString("N");

    public string Description => Conflict.Description;
    public string UseSchemaLabel => Conflict.UseSchemaLabel;
    public string KeepTrackerLabel => Conflict.KeepTrackerLabel;

    public bool UseSchema
    {
        get => Conflict.Choice == SchemaPushChoice.Schema;
        set { if (value) Conflict.Choice = SchemaPushChoice.Schema; }
    }

    public bool KeepTracker
    {
        get => Conflict.Choice == SchemaPushChoice.Tracker;
        set { if (value) Conflict.Choice = SchemaPushChoice.Tracker; }
    }
}

/// <summary>What a push does to one Tracker, as the review shows it.</summary>
public sealed record SchemaPushTrackerItem(
    string TrackerName,
    IReadOnlyList<string> AutomaticChanges,
    IReadOnlyList<SchemaPushConflictItem> Conflicts)
{
    public bool HasAutomaticChanges => AutomaticChanges.Count > 0;
    public bool HasConflicts => Conflicts.Count > 0;
    public bool IsUpToDate => !HasAutomaticChanges && !HasConflicts;

    public string AutomaticHeading => AutomaticChanges.Count == 1
        ? "1 change applies without asking:"
        : $"{AutomaticChanges.Count} changes apply without asking:";
}

/// <summary>
/// The schema's changes since its last push, and the push review: one review for every Tracker made from
/// the schema. Changes only the schema made apply without asking; where a Tracker changed the same thing,
/// the review asks whether to use the schema's value or keep the Tracker's.
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
        _useSchemaForAllCommand = new RelayCommand(() => ChooseAll(SchemaPushChoice.Schema), () => HasConflicts);
        _keepTrackersForAllCommand = new RelayCommand(() => ChooseAll(SchemaPushChoice.Tracker), () => HasConflicts);
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
    public bool HasConflicts => _review?.Conflicts.Count > 0;
    public int PendingChoiceCount => _review?.Conflicts.Count(static c => c.Choice is null) ?? 0;

    public string ReviewSummary => _review is null ? "" : HasConflicts
        ? $"{Count(_review.Conflicts.Count, "difference")} to decide, {PendingChoiceCount} left."
        : "Nothing to decide: every change applies without asking.";

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
            int trackers = review.Trackers.Count(static tracker => tracker.HasChanges);
            SetReview(null);
            _notifications?.Show("Schema pushed",
                $"{review.Schema.Schema.Name} was pushed to {Count(trackers, "tracker")}.", NotificationKind.Success);
            await RefreshAsync(_isEditable);
            Pushed?.Invoke(this, EventArgs.Empty);
        });
    }

    private void ChooseAll(SchemaPushChoice choice)
    {
        foreach (SchemaPushConflict conflict in _review?.Conflicts ?? []) conflict.Choice = choice;
    }

    private void CloseReview()
    {
        SetReview(null);
        ErrorMessage = null;
    }

    private void SetReview(SchemaPushReview? review)
    {
        if (_review is not null)
            foreach (SchemaPushConflict conflict in _review.Conflicts) conflict.PropertyChanged -= OnChoiceChanged;
        _review = review;
        ReviewTrackers.Clear();
        if (review is not null)
        {
            foreach (SchemaPushConflict conflict in review.Conflicts) conflict.PropertyChanged += OnChoiceChanged;
            foreach (TrackerPushPlan plan in review.Trackers)
                ReviewTrackers.Add(new SchemaPushTrackerItem(plan.Tracker.Name, plan.AutomaticChanges,
                    plan.Conflicts.Select(static conflict => new SchemaPushConflictItem(conflict)).ToArray()));
        }
        OnPropertyChanged(nameof(IsReviewOpen));
        OnPropertyChanged(nameof(HasConflicts));
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

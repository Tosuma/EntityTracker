using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using EntityTracker.Application.GitSync;
using EntityTracker.Domain;
using EntityTracker.Wpf.Commands;
using EntityTracker.Wpf.Services;
using Microsoft.Extensions.Logging;

namespace EntityTracker.Wpf.ViewModels;

public sealed class ProjectRepositoryCardViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ProjectId _projectId;
    private readonly ProjectGitSyncService _service;
    private readonly IProjectRepositoryFolderPicker _picker;
    private readonly WpfProjectUnsavedEditsGate? _editGate;
    private readonly NotificationCenter? _notifications;
    private readonly ILogger<ProjectRepositoryCardViewModel>? _logger;
    private readonly ProjectAutoSyncService? _autoSync;
    private readonly Action<ProjectSyncTiming?>? _saveTiming;
    private CancellationTokenSource? _syncCancellation;
    private ProjectSyncLink? _link;
    private string? _message;
    private bool _busy;
    private ProjectSyncTiming? _lastTiming;
    private readonly AsyncCommand _linkCommand;
    private readonly AsyncCommand _syncCommand;
    private readonly AsyncCommand _unlinkCommand;
    private readonly RelayCommand _cancelSyncCommand;

    public ProjectRepositoryCardViewModel(ProjectId projectId, ProjectGitSyncService service,
        IProjectRepositoryFolderPicker picker, WpfProjectUnsavedEditsGate? editGate = null,
        NotificationCenter? notifications = null,
        ILogger<ProjectRepositoryCardViewModel>? logger = null,
        ProjectSyncTiming? initialTiming = null,
        Action<ProjectSyncTiming?>? saveTiming = null,
        ProjectAutoSyncService? autoSync = null)
    {
        _projectId = projectId;
        _service = service;
        _picker = picker;
        _editGate = editGate;
        _notifications = notifications;
        _logger = logger;
        _autoSync = autoSync;
        if (_autoSync is not null) _autoSync.StateChanged += OnAutoSyncStateChanged;
        _lastTiming = initialTiming;
        _saveTiming = saveTiming;
        if (_editGate is not null) _editGate.WaitingChanged += (_, _) => Notify();
        _linkCommand = new AsyncCommand(LinkAsync, () => !_busy && !IsLinked);
        _syncCommand = new AsyncCommand(() => SyncAsync(), () => !_busy && IsLinked);
        _unlinkCommand = new AsyncCommand(UnlinkAsync, () => !_busy && _link is not null);
        _cancelSyncCommand = new RelayCommand(() => _syncCancellation?.Cancel(),
            () => _syncCancellation is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand LinkCommand => _linkCommand;
    public ICommand SyncCommand => _syncCommand;
    public ICommand UnlinkCommand => _unlinkCommand;
    public ICommand CancelSyncCommand => _cancelSyncCommand;
    public bool CanCancelSync => _syncCancellation is not null;
    public bool IsLinked => _link is not null && _link.SyncStatus != "RemoteDeletedLocalKept";
    public bool IsBusy => _busy;
    public string RepositoryPath => IsLinked ? _link!.RepositoryPath : "No repository linked";
    public string Branch => !IsLinked ? string.Empty : "Branch: " + _link!.Branch;
    public string Upstream => !IsLinked ? string.Empty : _link!.UpstreamIdentity is null
        ? "Local only (no upstream)" : "Upstream: " + _link.UpstreamIdentity.Split(':', 2)[0];
    public string SyncStateLabel => CurrentState.Kind switch
    {
        ProjectSyncStateKind.Unlinked => "Unlinked",
        ProjectSyncStateKind.Idle => "Idle",
        ProjectSyncStateKind.Syncing => "Syncing",
        ProjectSyncStateKind.UpToDate => "Up to date",
        ProjectSyncStateKind.LocalPending => "Local pending",
        ProjectSyncStateKind.DeletionApproval => "Deletion approval needed",
        ProjectSyncStateKind.Conflict => "Conflict",
        ProjectSyncStateKind.AuthenticationRequired => "Authentication required",
        ProjectSyncStateKind.ConfigurationInvalid => "Configuration invalid",
        ProjectSyncStateKind.UpdatePaused => "Sync paused",
        _ => "Failed"
    };
    private ProjectSyncState CurrentState
    {
        get
        {
            ProjectSyncState? state = _autoSync?.GetState(_projectId);
            return state is { Kind: not ProjectSyncStateKind.Unlinked } ? state :
                _link is null ? new(ProjectSyncStateKind.Unlinked, "No repository linked.") :
                ProjectAutoSyncService.FromLink(_link);
        }
    }
    public string LastResult => IsLinked ? CurrentState.LastResult :
        _link?.LastResult ?? "Select an existing clean repository to link this Project.";
    public bool HasLastSyncTiming => _lastTiming is not null;
    public string LastSyncTiming
    {
        get
        {
            if (_lastTiming is null) return string.Empty;
            return $"Last sync: {_lastTiming.Total.TotalSeconds:0.0}s.";
        }
    }
    public string PendingAction
    {
        get
        {
            if (_editGate?.WaitingProjectId == _projectId.Value)
                return "Sync is waiting for the unfinished edit to be saved or closed.";
            string action = CurrentState.Kind switch
            {
                ProjectSyncStateKind.DeletionApproval or ProjectSyncStateKind.Conflict =>
                    "Open this Project and use Sync now to review the change.",
                ProjectSyncStateKind.AuthenticationRequired =>
                    "Repair Git access outside EntityTracker, then use Sync now.",
                ProjectSyncStateKind.ConfigurationInvalid =>
                    "Repair or relink the checkout outside EntityTracker, then use Sync now.",
                ProjectSyncStateKind.UpdatePaused =>
                    "Update EntityTracker or restore access to the app release check.",
                ProjectSyncStateKind.Failed => "Check the repository and use Sync now to retry.",
                _ => string.Empty
            };
            if (action.Length > 0) return action;
            return _link?.SyncStatus switch
            {
                "Pending" => "Sync now to create the initial snapshot commit.",
                "PendingPush" => "A local commit is waiting for push. Retry sync after checking Git access.",
                "PendingRemote" => "The upstream advanced during sync. Retry to validate its changes.",
                "RemoteDeletedLocalKept" => "The shared repository Project was deleted. Local changes were kept; this Project is unlinked.",
                _ => string.Empty
            };
        }
    }
    public string? Message => _message;
    public bool HasPendingAction => !string.IsNullOrEmpty(PendingAction);
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    public async Task RefreshAsync(CancellationToken token = default)
    {
        try
        {
            _link = await _service.GetLinkAsync(_projectId, token);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _message = "Repository link status could not be loaded: " + error.Message;
        }
        Notify();
    }

    private async Task LinkAsync()
    {
        string? path = _picker.Pick();
        if (path is null) return;
        await ExecuteAsync(async () => _link = await _service.LinkAsync(_projectId, path));
    }

    private async Task SyncAsync(NotificationItem? existingNotice = null)
    {
        if (_busy) return;
        _busy = true;
        _message = null;
        _autoSync?.BeginManual(_projectId);
        _autoSync?.ReportManual(_projectId, new(ProjectSyncStateKind.Syncing,
            "Manual sync is running…", DateTimeOffset.UtcNow));
        NotificationItem? notice = existingNotice ?? _notifications?.FindActionForProject(_projectId);
        if (notice is null) notice = _notifications?.BeginProgress("Project sync",
            "Checking for unfinished edits…", _projectId);
        else _notifications?.Restart(notice, "Checking for unfinished edits…");
        void OnWaitingChanged(object? sender, EventArgs args)
        {
            if (_editGate?.WaitingProjectId == _projectId.Value && notice is not null)
                _notifications?.Progress(notice, "Waiting for the unfinished edit to be saved or closed…");
        }
        if (_editGate is not null) _editGate.WaitingChanged += OnWaitingChanged;
        Notify();
        using CancellationTokenSource cancellation = new();
        _syncCancellation = cancellation;
        Notify();
        try
        {
            IProgress<ProjectSyncPhase>? progress = notice is null ? null :
                new ProjectSyncProgressReporter(phase =>
                    _notifications?.Progress(notice, NotificationCenter.DescribeProjectSyncPhase(phase)));
            IProgress<ProjectSyncTiming> timing = new ProjectSyncTimingReporter(RecordTiming);
            try { _link = await _service.SyncNowAsync(_projectId, cancellation.Token, progress, timing); }
            catch (ProjectNameCollisionException collision)
            {
                string? name = ProjectLocalNameDialog.Prompt(System.Windows.Application.Current.MainWindow,
                    collision.ConflictingName);
                if (name is null)
                {
                    if (notice is not null) _notifications?.Complete(notice,
                        "Sync stopped. Choose a local Project name when you retry.",
                        NotificationKind.Information);
                    return;
                }
                _link = await _service.SyncNowAsync(_projectId, name, cancellation.Token, progress, timing);
            }
            if (_link is not null) _autoSync?.ReportManual(_projectId,
                ProjectAutoSyncService.FromLink(_link, DateTimeOffset.UtcNow));
            if (notice is not null)
            {
                if (_link?.SyncStatus is "PendingPush" or "PendingRemote")
                    _notifications?.NeedAction(notice, _link.LastResult, "Retry",
                        () => SyncAsync(notice));
                else
                {
                    _notifications?.DismissProjectActions(_projectId, notice);
                    _notifications?.Complete(notice, _link?.LastResult ?? "Sync complete.",
                        _link?.SyncStatus == "RemoteDeletedLocalKept"
                            ? NotificationKind.Information : NotificationKind.Success);
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (_link is not null) _autoSync?.ReportManual(_projectId,
                ProjectAutoSyncService.FromLink(_link));
            if (notice is not null) _notifications?.Complete(notice, "Sync cancelled.",
                NotificationKind.Information);
        }
        catch (ProjectSyncReviewCancelledException)
        {
            if (_link is not null) _autoSync?.ReportManual(_projectId,
                ProjectAutoSyncService.FromLink(_link));
            if (notice is not null) _notifications?.Complete(notice,
                "Sync review was cancelled.", NotificationKind.Information);
        }
        catch (Exception error)
        {
            _message = error.Message;
            _autoSync?.ReportManual(_projectId,
                ProjectAutoSyncService.FromError(error, DateTimeOffset.UtcNow));
            try { _link = await _service.GetLinkAsync(_projectId); }
            catch { /* Keep the original sync error visible. */ }
            if (notice is not null)
            {
                bool review = error is ProjectSyncReviewRequiredException;
                bool retry = _link?.SyncStatus is "PendingPush" or "PendingRemote";
                _notifications?.NeedAction(notice, error.Message,
                    review ? "Review" : retry ? "Retry" : "Open Project",
                    review || retry ? () => SyncAsync(notice) : () =>
                        _notifications?.NavigateToProjectAsync?.Invoke(_projectId) ?? Task.CompletedTask);
            }
        }
        finally
        {
            if (_editGate is not null) _editGate.WaitingChanged -= OnWaitingChanged;
            _syncCancellation = null;
            _busy = false;
            _autoSync?.EndManual(_projectId);
            Notify();
        }
    }

    private void RecordTiming(ProjectSyncTiming timing)
    {
        _logger?.LogInformation(
            "Project sync timing: outcome={Outcome} totalMs={TotalMs} stages={Stages} networkFetchMs={NetworkMs} gitSnapshotReadMs={SnapshotReadMs} snapshotFiles={SnapshotFiles} reusedSnapshots={ReusedSnapshots} blobReadProcesses={BlobReadProcesses}",
            timing.Outcome, timing.Total.TotalMilliseconds,
            string.Join(", ", timing.Stages.OrderBy(stage => stage.Key)
                .Select(stage => $"{stage.Key}={stage.Value.TotalMilliseconds:0}")),
            timing.NetworkFetch.TotalMilliseconds, timing.GitSnapshotRead.TotalMilliseconds,
            timing.GitSnapshotFileCount, timing.ReusedSnapshots, timing.BlobReadProcesses);
        ShowTiming(timing);
    }

    internal void ShowTiming(ProjectSyncTiming timing)
    {
        _lastTiming = timing;
        _saveTiming?.Invoke(timing);
        Notify();
    }

    private Task UnlinkAsync() => ExecuteAsync(async () =>
    {
        await _service.UnlinkAsync(_projectId);
        _link = null;
        _lastTiming = null;
        _saveTiming?.Invoke(null);
        _notifications?.DismissProjectActions(_projectId);
        _autoSync?.ReportUnlinked(_projectId);
    });

    private async Task ExecuteAsync(Func<Task> action)
    {
        _busy = true;
        _message = null;
        Notify();
        try { await action(); }
        catch (Exception error) { _message = error.Message; }
        finally { _busy = false; Notify(); }
    }

    private void Notify()
    {
        foreach (string name in new[] { nameof(IsLinked), nameof(IsBusy), nameof(RepositoryPath),
                     nameof(Branch), nameof(Upstream), nameof(LastResult), nameof(LastSyncTiming),
                     nameof(HasLastSyncTiming), nameof(PendingAction), nameof(Message),
                     nameof(HasPendingAction), nameof(HasMessage), nameof(CanCancelSync),
                     nameof(SyncStateLabel) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        _linkCommand.NotifyCanExecuteChanged();
        _syncCommand.NotifyCanExecuteChanged();
        _unlinkCommand.NotifyCanExecuteChanged();
        _cancelSyncCommand.NotifyCanExecuteChanged();
    }

    private void OnAutoSyncStateChanged(object? sender, ProjectId projectId)
    {
        if (projectId != _projectId) return;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) Notify();
        else if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(Notify);
    }

    public void Dispose()
    {
        if (_autoSync is not null) _autoSync.StateChanged -= OnAutoSyncStateChanged;
    }
}

public sealed class ProjectRepositoryCardViewModelFactory(
    ProjectGitSyncService service, IProjectRepositoryFolderPicker picker,
    WpfProjectUnsavedEditsGate? editGate = null, NotificationCenter? notifications = null,
    ILogger<ProjectRepositoryCardViewModel>? logger = null,
    ProjectAutoSyncService? autoSync = null)
{
    private readonly Dictionary<Guid, ProjectSyncTiming> _timings = [];

    public ProjectRepositoryCardViewModel Create(ProjectId projectId) =>
        new(projectId, service, picker, editGate, notifications, logger,
            _timings.GetValueOrDefault(projectId.Value), timing =>
            {
                if (timing is null) _timings.Remove(projectId.Value);
                else _timings[projectId.Value] = timing;
            }, autoSync);
}

internal sealed class ProjectSyncTimingReporter(Action<ProjectSyncTiming> report)
    : IProgress<ProjectSyncTiming>
{
    public void Report(ProjectSyncTiming value)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) report(value);
        else dispatcher.BeginInvoke(() => report(value));
    }
}

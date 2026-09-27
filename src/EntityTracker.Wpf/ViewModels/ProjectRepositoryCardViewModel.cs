using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using EntityTracker.Application.GitSync;
using EntityTracker.Domain;
using EntityTracker.Wpf.Commands;
using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf.ViewModels;

public sealed class ProjectRepositoryCardViewModel : INotifyPropertyChanged
{
    private readonly ProjectId _projectId;
    private readonly ProjectGitSyncService _service;
    private readonly IProjectRepositoryFolderPicker _picker;
    private readonly WpfProjectUnsavedEditsGate? _editGate;
    private CancellationTokenSource? _syncCancellation;
    private ProjectSyncLink? _link;
    private string? _message;
    private bool _busy;
    private readonly AsyncCommand _linkCommand;
    private readonly AsyncCommand _syncCommand;
    private readonly AsyncCommand _unlinkCommand;
    private readonly RelayCommand _cancelSyncCommand;

    public ProjectRepositoryCardViewModel(ProjectId projectId, ProjectGitSyncService service,
        IProjectRepositoryFolderPicker picker, WpfProjectUnsavedEditsGate? editGate = null)
    {
        _projectId = projectId;
        _service = service;
        _picker = picker;
        _editGate = editGate;
        if (_editGate is not null) _editGate.WaitingChanged += (_, _) => Notify();
        _linkCommand = new AsyncCommand(LinkAsync, () => !_busy && !IsLinked);
        _syncCommand = new AsyncCommand(SyncAsync, () => !_busy && IsLinked);
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
    public string LastResult => _link?.LastResult ?? "Select an existing clean repository to link this Project.";
    public string PendingAction => _link?.SyncStatus switch
    {
        _ when _editGate?.WaitingProjectId == _projectId.Value =>
            "Sync is waiting for the unfinished edit to be saved or closed.",
        "Pending" => "Sync now to create the initial snapshot commit.",
        "PendingPush" => "A local commit is waiting for push. Retry sync after checking Git access.",
        "PendingRemote" => "The upstream advanced during sync. Retry to validate its changes.",
        "RemoteDeletedLocalKept" => "The shared repository Project was deleted. Local changes were kept; this Project is unlinked.",
        _ => string.Empty
    };
    public string? Message => _message;
    public bool HasPendingAction => !string.IsNullOrEmpty(PendingAction);
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    public async Task RefreshAsync(CancellationToken token = default)
    {
        try
        {
            _link = await _service.GetLinkAsync(_projectId, token);
            _message = null;
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

    private Task SyncAsync() => ExecuteAsync(async () =>
    {
        using CancellationTokenSource cancellation = new();
        _syncCancellation = cancellation;
        Notify();
        try { _link = await _service.SyncNowAsync(_projectId, cancellation.Token); }
        catch (ProjectNameCollisionException collision)
        {
            string? name = ProjectLocalNameDialog.Prompt(System.Windows.Application.Current.MainWindow,
                collision.ConflictingName);
            if (name is not null) _link = await _service.SyncNowAsync(_projectId, name, cancellation.Token);
        }
        finally { _syncCancellation = null; Notify(); }
    });

    private Task UnlinkAsync() => ExecuteAsync(async () =>
    {
        await _service.UnlinkAsync(_projectId);
        _link = null;
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
                     nameof(Branch), nameof(Upstream), nameof(LastResult), nameof(PendingAction), nameof(Message),
                     nameof(HasPendingAction), nameof(HasMessage), nameof(CanCancelSync) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        _linkCommand.NotifyCanExecuteChanged();
        _syncCommand.NotifyCanExecuteChanged();
        _unlinkCommand.NotifyCanExecuteChanged();
        _cancelSyncCommand.NotifyCanExecuteChanged();
    }
}

public sealed class ProjectRepositoryCardViewModelFactory(
    ProjectGitSyncService service, IProjectRepositoryFolderPicker picker,
    WpfProjectUnsavedEditsGate? editGate = null)
{
    public ProjectRepositoryCardViewModel Create(ProjectId projectId) => new(projectId, service, picker, editGate);
}

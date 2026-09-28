using System.Collections.Concurrent;
using EntityTracker.Domain;

namespace EntityTracker.Application.GitSync;

public enum ProjectSyncStateKind
{
    Unlinked, Idle, Syncing, UpToDate, LocalPending, DeletionApproval,
    Conflict, AuthenticationRequired, ConfigurationInvalid, Failed
}

public sealed record ProjectSyncState(ProjectSyncStateKind Kind, string LastResult,
    DateTimeOffset? LastAttemptUtc = null);

/// <summary>Schedules the existing Project sync operation without involving the UI thread.</summary>
public sealed class ProjectAutoSyncService : IDisposable
{
    private readonly IProjectSyncLinkStore _links;
    private readonly Func<ProjectId, CancellationToken, Task<ProjectSyncLink>> _sync;
    private readonly IProjectUnsavedEditsGate? _editGate;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<Guid, ProjectSyncState> _states = [];
    private readonly ConcurrentDictionary<Guid, byte> _inFlight = [];
    private readonly ConcurrentDictionary<Guid, byte> _manualInFlight = [];
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly SemaphoreSlim _settingsChanged = new(0, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _currentPass;
    private Task? _loop;
    private volatile bool _enabled;
    private TimeSpan _interval;

    public ProjectAutoSyncService(IProjectSyncLinkStore links,
        Func<ProjectId, CancellationToken, Task<ProjectSyncLink>> sync,
        bool enabled = true, int intervalMinutes = 5,
        IProjectUnsavedEditsGate? editGate = null, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(sync);
        ValidateInterval(intervalMinutes);
        _links = links;
        _sync = sync;
        _enabled = enabled;
        _interval = TimeSpan.FromMinutes(intervalMinutes);
        _editGate = editGate;
        _clock = clock ?? TimeProvider.System;
    }

    public event EventHandler<ProjectId>? StateChanged;
    public ProjectSyncState GetState(ProjectId projectId) =>
        _states.TryGetValue(projectId.Value, out ProjectSyncState? state)
            ? state : new(ProjectSyncStateKind.Unlinked, "No repository linked.");

    public void Start()
    {
        if (_loop is not null) return;
        _loop = Task.Run(RunLoopAsync);
    }

    public void Configure(bool enabled, int intervalMinutes)
    {
        ValidateInterval(intervalMinutes);
        _enabled = enabled;
        _interval = TimeSpan.FromMinutes(intervalMinutes);
        if (!enabled) CancelCurrentPass();
        SignalSettingsChanged();
    }

    public void ReportManual(ProjectId projectId, ProjectSyncState state) =>
        SetState(projectId, state);

    public void BeginManual(ProjectId projectId) => _manualInFlight[projectId.Value] = 0;

    public void EndManual(ProjectId projectId) => _manualInFlight.TryRemove(projectId.Value, out _);

    public void ReportUnlinked(ProjectId projectId) =>
        SetState(projectId, new(ProjectSyncStateKind.Unlinked, "No repository linked."));

    public static ProjectSyncState FromLink(ProjectSyncLink link,
        DateTimeOffset? attemptUtc = null) => new(link.SyncStatus switch
    {
        "Pending" or "PendingPush" or "PendingRemote" or "PendingDeletion" =>
            ProjectSyncStateKind.LocalPending,
        "Current" => ProjectSyncStateKind.UpToDate,
        "RemoteDeletedLocalKept" or "Deleted" => ProjectSyncStateKind.Unlinked,
        _ => ProjectSyncStateKind.Idle
    }, link.LastResult, attemptUtc);

    private static void ValidateInterval(int minutes)
    {
        if (minutes is not (1 or 5 or 15 or 30 or 60))
            throw new ArgumentOutOfRangeException(nameof(minutes));
    }

    private void SignalSettingsChanged()
    {
        try { _settingsChanged.Release(); }
        catch (SemaphoreFullException) { }
    }

    private async Task RunLoopAsync()
    {
        CancellationToken token = _lifetime.Token;
        while (!token.IsCancellationRequested)
        {
            if (!_enabled)
            {
                try { await _settingsChanged.WaitAsync(token); }
                catch (OperationCanceledException) { break; }
                continue;
            }
            using CancellationTokenSource pass = CancellationTokenSource.CreateLinkedTokenSource(token);
            lock (_passLock) _currentPass = pass;
            try { await RunPassAsync(pass.Token); }
            catch (OperationCanceledException) when (pass.IsCancellationRequested) { }
            catch { /* Per-Project failures are reported; a link-store failure retries next pass. */ }
            finally { lock (_passLock) _currentPass = null; }
            if (token.IsCancellationRequested) break;
            if (!_enabled) continue;
            try
            {
                using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(token);
                Task delay = Task.Delay(_interval, _clock, wait.Token);
                Task changed = _settingsChanged.WaitAsync(wait.Token);
                await Task.WhenAny(delay, changed);
                wait.Cancel();
            }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task RunPassAsync(CancellationToken token = default)
    {
        IReadOnlyList<ProjectSyncLink> links = await _links.ReadAllAsync(token);
        foreach (ProjectSyncLink link in links.OrderBy(link => link.ProjectId))
        {
            token.ThrowIfCancellationRequested();
            if (link.SyncStatus is "RemoteDeletedLocalKept" or "Deleted") continue;
            ProjectId projectId = new(link.ProjectId);
            _states.TryAdd(link.ProjectId, FromLink(link));
            if (_manualInFlight.ContainsKey(link.ProjectId) ||
                !_inFlight.TryAdd(link.ProjectId, 0)) continue;
            await RunProjectAsync(projectId, token);
        }
    }

    private async Task RunProjectAsync(ProjectId projectId, CancellationToken token)
    {
        bool deferred = false;
        try { await _runGate.WaitAsync(token); }
        catch (OperationCanceledException)
        {
            _inFlight.TryRemove(projectId.Value, out _);
            throw;
        }
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            SetState(projectId, new(ProjectSyncStateKind.Syncing,
                "Checking the linked repository…", _clock.GetUtcNow()));
            ProjectSyncLink result = await _sync(projectId, timeout.Token);
            SetState(projectId, FromLink(result, _clock.GetUtcNow()));
        }
        catch (ProjectSyncEditDeferredException)
        {
            deferred = true;
            SetState(projectId, new(ProjectSyncStateKind.Idle,
                "Waiting for the unfinished edit to be saved or closed.", _clock.GetUtcNow()));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (OperationCanceledException)
        {
            SetState(projectId, new(ProjectSyncStateKind.Failed,
                "Automatic sync exceeded five minutes. Check the repository and retry.", _clock.GetUtcNow()));
        }
        catch (Exception error)
        {
            SetState(projectId, FromError(error, _clock.GetUtcNow()));
        }
        finally
        {
            _runGate.Release();
            if (!deferred) _inFlight.TryRemove(projectId.Value, out _);
        }
        if (deferred) _ = WaitForEditAndRetryAsync(projectId, _lifetime.Token);
    }

    private async Task WaitForEditAndRetryAsync(ProjectId projectId, CancellationToken token)
    {
        try
        {
            if (_editGate is not null) await _editGate.WaitUntilReadyAsync(projectId, token);
            if (_enabled && !token.IsCancellationRequested &&
                (await _links.ReadAllAsync(token)).Any(link => link.ProjectId == projectId.Value))
                await RunProjectAsync(projectId, token);
            else _inFlight.TryRemove(projectId.Value, out _);
        }
        catch (OperationCanceledException) { _inFlight.TryRemove(projectId.Value, out _); }
        catch
        {
            _inFlight.TryRemove(projectId.Value, out _);
            SetState(projectId, new(ProjectSyncStateKind.Failed,
                "Automatic sync could not resume. Use Sync now to retry.", _clock.GetUtcNow()));
        }
    }

    private static ProjectSyncStateKind Classify(Exception error) => error switch
    {
        ProjectSyncDeletionApprovalRequiredException => ProjectSyncStateKind.DeletionApproval,
        ProjectSyncReviewRequiredException or ProjectNameCollisionException => ProjectSyncStateKind.Conflict,
        ProjectSyncAuthenticationException => ProjectSyncStateKind.AuthenticationRequired,
        ProjectSyncConfigurationException or InvalidDataException or ArgumentException =>
            ProjectSyncStateKind.ConfigurationInvalid,
        _ => ProjectSyncStateKind.Failed
    };

    private static string SafeResult(Exception error) => error switch
    {
        ProjectSyncDeletionApprovalRequiredException or ProjectSyncReviewRequiredException or
            ProjectSyncAuthenticationException or ProjectSyncConfigurationException => error.Message,
        ProjectNameCollisionException => "Choose a unique local Project name with Sync now.",
        InvalidDataException => "The repository snapshot is invalid. Open the Project for details.",
        _ => "Automatic sync failed. Open the Project and use Sync now for details."
    };

    public static ProjectSyncState FromError(Exception error, DateTimeOffset attemptUtc) =>
        new(Classify(error), SafeResult(error), attemptUtc);

    private void SetState(ProjectId projectId, ProjectSyncState state)
    {
        _states[projectId.Value] = state;
        StateChanged?.Invoke(this, projectId);
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        CancelCurrentPass();
        SignalSettingsChanged();
    }

    private readonly object _passLock = new();

    private void CancelCurrentPass()
    {
        lock (_passLock) _currentPass?.Cancel();
    }
}

public sealed class ProjectSyncAuthenticationException(string message) : InvalidOperationException(message);
public sealed class ProjectSyncConfigurationException(string message) : InvalidOperationException(message);

using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

using EntityTracker.Application.GitSync;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace EntityTracker.Wpf.Services;

/// <summary>
/// Keeps automatic sync in step with Windows sleeping and waking (#222). Going to sleep holds new syncs back,
/// so none starts just before the computer stops. Waking lets them start again and runs one as soon as the
/// network is back, instead of waiting out the rest of the interval.
/// </summary>
public sealed class SyncPowerWatcher : IDisposable
{
    /// <summary>How long after waking, with the network up, the sync waits for connections to settle.</summary>
    public static readonly TimeSpan DefaultSettleDelay = TimeSpan.FromSeconds(10);

    private readonly IAutoSyncControl _autoSync;
    private readonly Func<bool> _isNetworkAvailable;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _settleDelay;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private CancellationTokenSource? _pending;
    private bool _waitingForNetwork;
    private bool _attached;

    public SyncPowerWatcher(IAutoSyncControl autoSync, ILogger<SyncPowerWatcher>? logger = null,
        Func<bool>? isNetworkAvailable = null, TimeProvider? clock = null, TimeSpan? settleDelay = null)
    {
        _autoSync = autoSync;
        _logger = logger ?? NullLogger<SyncPowerWatcher>.Instance;
        _isNetworkAvailable = isNetworkAvailable ?? NetworkInterface.GetIsNetworkAvailable;
        _clock = clock ?? TimeProvider.System;
        _settleDelay = settleDelay ?? DefaultSettleDelay;
    }

    /// <summary>Starts listening to Windows power and network changes.</summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) OnSuspend();
        else if (e.Mode == PowerModes.Resume) OnResume();
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (e.IsAvailable) OnNetworkAvailable();
    }

    /// <summary>The computer is going to sleep.</summary>
    internal void OnSuspend()
    {
        lock (_gate)
        {
            _waitingForNetwork = false;
            _pending?.Cancel();
            _pending = null;
        }
        _autoSync.Pause("the computer is going to sleep");
    }

    /// <summary>The computer woke up.</summary>
    internal void OnResume()
    {
        _autoSync.Resume("the computer woke up");
        if (_isNetworkAvailable()) SyncAfterSettling();
        else
        {
            lock (_gate) _waitingForNetwork = true;
            _logger.LogInformation("The computer woke up without a network; automatic sync runs once it is back.");
        }
    }

    /// <summary>A network became available.</summary>
    internal void OnNetworkAvailable()
    {
        lock (_gate)
        {
            if (!_waitingForNetwork) return;
            _waitingForNetwork = false;
        }
        SyncAfterSettling();
    }

    private void SyncAfterSettling()
    {
        CancellationTokenSource pending;
        lock (_gate)
        {
            _pending?.Cancel();
            pending = _pending = new CancellationTokenSource();
        }
        _ = SyncAfterSettlingAsync(pending.Token);
    }

    private async Task SyncAfterSettlingAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_settleDelay, _clock, token);
            _autoSync.SyncSoon("the computer woke up and the network is back");
        }
        catch (OperationCanceledException)
        {
            // Not logged: the computer went to sleep again, or the app is closing, before the sync was due.
        }
    }

    public void Dispose()
    {
        if (_attached)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
            _attached = false;
        }
        lock (_gate)
        {
            _pending?.Cancel();
            _pending = null;
        }
    }
}

/// <summary>
/// Lets a running Project sync finish when Windows shuts down, restarts or logs off (#222). WPF raises no
/// <c>Window.Closing</c> then, so the "wait for the running sync" step of closing the window never runs.
/// New syncs are held back, Windows is told why the app is still busy, and the running sync gets up to
/// <see cref="Limit"/>. The session is never cancelled.
/// </summary>
public sealed class SessionEndSyncGuard(IAutoSyncControl autoSync, ILogger? logger = null, TimeSpan? limit = null)
{
    public const string BlockReason = "Finishing Project sync";

    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    /// <summary>How long a running sync may take once the session ends.</summary>
    public TimeSpan Limit { get; } = limit ?? TimeSpan.FromSeconds(10);

    /// <summary>
    /// Holds new syncs back and waits for a running one. <paramref name="blockShutdown"/> and
    /// <paramref name="allowShutdown"/> tell Windows why the app is still busy, and that it is done.
    /// Returns whether no sync is still running.
    /// </summary>
    public async Task<bool> FinishSyncAsync(Action<string> blockShutdown, Action allowShutdown)
    {
        autoSync.Pause("Windows is ending the session");
        if (!autoSync.HasActiveSync) return true;
        _logger.LogInformation("Windows is ending the session while a Project sync runs; waiting up to {LimitSeconds} s for it.",
            Limit.TotalSeconds);
        blockShutdown(BlockReason);
        try
        {
            bool finished = await autoSync.WaitUntilIdleAsync(Limit);
            if (finished) _logger.LogInformation("The Project sync finished before the session ended.");
            else _logger.LogWarning("The Project sync was still running when the session ended; the next sync repairs the checkout if needed.");
            return finished;
        }
        finally
        {
            allowShutdown();
        }
    }
}

/// <summary>Tells Windows why the app holds up shutdown, shown on the "apps are still running" screen.</summary>
internal static class ShutdownBlock
{
    public static void Create(IntPtr window, string reason)
    {
        if (window != IntPtr.Zero) ShutdownBlockReasonCreate(window, reason);
    }

    public static void Destroy(IntPtr window)
    {
        if (window != IntPtr.Zero) ShutdownBlockReasonDestroy(window);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShutdownBlockReasonCreate(IntPtr hWnd, string pwszReason);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShutdownBlockReasonDestroy(IntPtr hWnd);
}

using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.IO;

namespace EntityTracker.Wpf.Services;

/// <summary>Keeps one UI process per local data store and asks it to show its window.</summary>
public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private readonly EventWaitHandle _stop = new(false, EventResetMode.ManualReset);
    private bool _ownsMutex;

    public SingleInstanceCoordinator(string dataRoot)
    {
        string normalized = Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar)
            .ToUpperInvariant();
        string key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        _mutex = new Mutex(false, @"Local\EntityTracker-" + key);
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset,
            @"Local\EntityTracker-Activate-" + key);
    }

    public bool TryBecomePrimary(Action activate)
    {
        try { _ownsMutex = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { _ownsMutex = true; }
        if (!_ownsMutex)
        {
            _activate.Set();
            return false;
        }
        _ = Task.Run(() =>
        {
            WaitHandle[] handles = [_activate, _stop];
            while (WaitHandle.WaitAny(handles) == 0) activate();
        });
        return true;
    }

    public void Dispose()
    {
        _stop.Set();
        if (_ownsMutex) _mutex.ReleaseMutex();
        _activate.Dispose();
        _stop.Dispose();
        _mutex.Dispose();
    }
}

using EntityTracker.Application.GitSync;
using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf.Tests.Services;

/// <summary>Automatic sync follows Windows sleeping, waking and ending the session (#222).</summary>
public sealed class SyncPowerEventsTests
{
    [Fact]
    public void SleepPausesAndWakingWithANetworkSyncsSoon()
    {
        FakeAutoSync autoSync = new();
        using SyncPowerWatcher watcher = new(autoSync, isNetworkAvailable: () => true, settleDelay: TimeSpan.Zero);

        watcher.OnSuspend();
        Assert.True(autoSync.Paused);

        watcher.OnResume();
        Assert.False(autoSync.Paused);
        Assert.Equal(1, autoSync.SyncSoonCalls);
    }

    [Fact]
    public void WakingWithoutANetworkSyncsOnceTheNetworkIsBack()
    {
        FakeAutoSync autoSync = new();
        bool network = false;
        using SyncPowerWatcher watcher = new(autoSync, isNetworkAvailable: () => network, settleDelay: TimeSpan.Zero);

        watcher.OnSuspend();
        watcher.OnResume();
        Assert.Equal(0, autoSync.SyncSoonCalls);

        network = true;
        watcher.OnNetworkAvailable();
        watcher.OnNetworkAvailable();
        Assert.Equal(1, autoSync.SyncSoonCalls);
    }

    [Fact]
    public void ANetworkComingBackWithoutAWakeDoesNotSync()
    {
        FakeAutoSync autoSync = new();
        using SyncPowerWatcher watcher = new(autoSync, isNetworkAvailable: () => true, settleDelay: TimeSpan.Zero);

        watcher.OnNetworkAvailable();

        Assert.Equal(0, autoSync.SyncSoonCalls);
    }

    [Fact]
    public async Task SleepingAgainBeforeTheNetworkSettlesCancelsThePendingSync()
    {
        FakeAutoSync autoSync = new();
        using SyncPowerWatcher watcher = new(autoSync, isNetworkAvailable: () => true,
            settleDelay: TimeSpan.FromMilliseconds(200));

        watcher.OnResume();
        watcher.OnSuspend();
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.Equal(0, autoSync.SyncSoonCalls);
        Assert.True(autoSync.Paused);
    }

    [Fact]
    public async Task EndingTheSessionWaitsForTheRunningSyncAndTellsWindowsWhy()
    {
        FakeAutoSync autoSync = new() { HasActiveSync = true, IdleResult = true };
        SessionEndSyncGuard guard = new(autoSync, limit: TimeSpan.FromSeconds(10));
        List<string> calls = [];

        bool finished = await guard.FinishSyncAsync(reason => calls.Add("block: " + reason), () => calls.Add("allow"));

        Assert.True(finished);
        Assert.True(autoSync.Paused);
        Assert.Equal(TimeSpan.FromSeconds(10), autoSync.WaitedFor);
        Assert.Equal(["block: " + SessionEndSyncGuard.BlockReason, "allow"], calls);
    }

    [Fact]
    public async Task EndingTheSessionWithoutASyncDoesNotHoldWindowsUp()
    {
        FakeAutoSync autoSync = new();
        List<string> calls = [];

        bool finished = await new SessionEndSyncGuard(autoSync).FinishSyncAsync(
            reason => calls.Add(reason), () => calls.Add("allow"));

        Assert.True(finished);
        Assert.True(autoSync.Paused);
        Assert.Empty(calls);
        Assert.Null(autoSync.WaitedFor);
    }

    [Fact]
    public async Task ASyncStillRunningAtTheLimitStillLetsWindowsGoOn()
    {
        FakeAutoSync autoSync = new() { HasActiveSync = true, IdleResult = false };
        List<string> calls = [];

        bool finished = await new SessionEndSyncGuard(autoSync).FinishSyncAsync(
            reason => calls.Add("block"), () => calls.Add("allow"));

        Assert.False(finished);
        Assert.Equal(["block", "allow"], calls);
    }

    private sealed class FakeAutoSync : IAutoSyncControl
    {
        public bool HasActiveSync { get; set; }
        public bool IdleResult { get; set; } = true;
        public bool Paused { get; private set; }
        public int SyncSoonCalls { get; private set; }
        public TimeSpan? WaitedFor { get; private set; }

        public void Pause(string reason) => Paused = true;
        public void Resume(string reason) => Paused = false;
        public void SyncSoon(string reason) => SyncSoonCalls++;

        public Task<bool> WaitUntilIdleAsync(TimeSpan limit, CancellationToken cancellationToken = default)
        {
            WaitedFor = limit;
            return Task.FromResult(IdleResult);
        }
    }
}

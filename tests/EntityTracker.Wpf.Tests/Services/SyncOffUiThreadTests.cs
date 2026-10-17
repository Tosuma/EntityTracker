using System.Runtime.ExceptionServices;
using System.Windows.Threading;

using EntityTracker.Application.GitSync;
using EntityTracker.Application.Snapshots;
using EntityTracker.Domain;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.Services;

/// <summary>
/// Project sync runs off the UI thread, so the window keeps animating (the notice's progress bar, the
/// turning sync icon) while it reads, encodes and compares the Project; dialogs come back to the UI thread.
/// </summary>
public sealed class SyncOffUiThreadTests
{
    [Fact]
    public async Task UiWorkAskedForFromABackgroundThreadRunsOnTheUiThread()
    {
        (Dispatcher dispatcher, Thread uiThread) = StartUiThread();
        try
        {
            Thread? ranOn = null;
            int result = await Task.Run(() => UiThread.RunAsync(() =>
            {
                ranOn = Thread.CurrentThread;
                return 42;
            }, dispatcher));

            Assert.Equal(42, result);
            Assert.Same(uiThread, ranOn);
        }
        finally { dispatcher.InvokeShutdown(); }
    }

    [Fact]
    public async Task SyncNowFromTheProjectCardRunsTheSyncOffTheUiThread()
    {
        (Dispatcher dispatcher, Thread uiThread) = StartUiThread();
        try
        {
            ProjectId projectId = new(Guid.NewGuid());
            RecordingLinks links = new(new ProjectSyncLink(projectId.Value, "C:\\repo", "main", null, "abc", 1,
                "hash", "", "Current"));
            ProjectGitSyncService service = new(links, null!, new NoSnapshots(), null!, null!);
            ProjectRepositoryCardViewModel card = await dispatcher.InvokeAsync(() =>
                new ProjectRepositoryCardViewModel(projectId, service, null!)).Task;
            await dispatcher.InvokeAsync(() => card.RefreshAsync()).Task.Unwrap();
            links.Callers.Clear();

            // The sync stops at the Git step (no transport); what matters is where it ran.
            await dispatcher.InvokeAsync(() => card.SyncCommand.Execute(null));
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (links.Callers.IsEmpty && DateTime.UtcNow < deadline) await Task.Delay(20);
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

            Thread caller = Assert.Single(links.Callers);
            Assert.NotSame(uiThread, caller);
        }
        finally { dispatcher.InvokeShutdown(); }
    }

    private static (Dispatcher Dispatcher, Thread Thread) StartUiThread()
    {
        TaskCompletionSource<Dispatcher> started = new();
        Thread thread = new(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            started.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return (started.Task.GetAwaiter().GetResult(), thread);
    }

    private sealed class RecordingLinks(ProjectSyncLink link) : IProjectSyncLinkStore
    {
        public System.Collections.Concurrent.ConcurrentBag<Thread> Callers { get; } = [];

        public Task<IReadOnlyList<ProjectSyncLink>> ReadAllAsync(CancellationToken cancellationToken = default)
        {
            Callers.Add(Thread.CurrentThread);
            return Task.FromResult<IReadOnlyList<ProjectSyncLink>>([link]);
        }

        public Task SaveAsync(ProjectSyncLink saved, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoSnapshots : IProjectSnapshotStore
    {
        public Task<ProjectSnapshotRead> ReadAsync(ProjectId projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectSnapshotRead(null, 0));

        public Task<long> ApplyAsync(ProjectSnapshot snapshot, long expectedRevision,
            CancellationToken cancellationToken = default) => Task.FromResult(0L);
    }
}

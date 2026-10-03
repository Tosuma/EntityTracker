using EntityTracker.Application.GitSync;
using EntityTracker.Application.Snapshots;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests.GitSync;

public sealed class ProjectAutoSyncServiceTests
{
    [Theory]
    [InlineData(5, "behind")]
    [InlineData(0, "ahead")]
    public void UnsupportedProjectFormat_ReportsBothVersionsAndDirection(
        int projectVersion, string direction)
    {
        ProjectSnapshotFormatVersionException error = new(projectVersion);
        ProjectSyncState state = ProjectAutoSyncService.FromError(error, DateTimeOffset.UtcNow);

        Assert.Equal(ProjectSnapshot.CurrentFormatVersion, error.ApplicationFormatVersion);
        Assert.Equal(projectVersion, error.ProjectFormatVersion);
        Assert.Contains($"app's Project format version is {ProjectSnapshot.CurrentFormatVersion}",
            error.Message, StringComparison.Ordinal);
        Assert.Contains($"Project's format version is {projectVersion}",
            error.Message, StringComparison.Ordinal);
        Assert.Contains(direction, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ProjectSyncStateKind.ConfigurationInvalid, state.Kind);
        Assert.Equal(error.Message, state.LastResult);
        Assert.Equal("The repository snapshot is invalid. Open the Project for details.",
            ProjectAutoSyncService.FromError(new InvalidDataException("private detail"),
                DateTimeOffset.UtcNow).LastResult);
    }

    [Fact]
    public async Task ActiveSyncFlagTracksRunningAutomaticAndManualWork()
    {
        ProjectId project = ProjectId.New();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using ProjectAutoSyncService service = new(new LinkStore(Link(project)), async (id, _) =>
        {
            entered.SetResult();
            await release.Task;
            return Link(id) with { SyncStatus = "Current" };
        });

        Task pass = service.RunPassAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(service.HasActiveSync);
        release.SetResult();
        await pass;
        Assert.False(service.HasActiveSync);
        service.BeginManual(project);
        Assert.True(service.HasActiveSync);
        service.EndManual(project);
        Assert.False(service.HasActiveSync);
    }

    [Fact]
    public async Task StartRunsAnInitialPassAndDisabledSchedulerCanBeEnabled()
    {
        ProjectId project = ProjectId.New();
        LinkStore store = new(Link(project));
        TaskCompletionSource first = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        using ProjectAutoSyncService service = new(store, (id, _) =>
        {
            Interlocked.Increment(ref calls);
            first.TrySetResult();
            return Task.FromResult(Link(id) with { SyncStatus = "Current" });
        }, enabled: false);

        service.Start();
        await Task.Delay(100);
        Assert.Equal(0, calls);
        service.Configure(true, 1);
        await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ProjectSyncStateKind.UpToDate, service.GetState(project).Kind);
        service.Configure(false, 1);
    }

    [Fact]
    public async Task PassChecksAllLinkedProjectsSequentiallyAndIsolatesFailure()
    {
        ProjectId first = ProjectId.New();
        ProjectId second = ProjectId.New();
        ProjectId third = ProjectId.New();
        ProjectId[] projects = [first, second, third];
        List<Guid> visited = [];
        int active = 0;
        int peak = 0;
        using ProjectAutoSyncService service = new(new LinkStore(projects.Select(Link).ToArray()),
            async (id, token) =>
            {
                int count = Interlocked.Increment(ref active);
                peak = Math.Max(peak, count);
                visited.Add(id.Value);
                await Task.Delay(10, token);
                Interlocked.Decrement(ref active);
                if (id == second) throw new ProjectSyncConfigurationException("Relink checkout.");
                return Link(id) with { SyncStatus = "Current" };
            });

        await service.RunPassAsync();

        Assert.Equal(projects.Select(p => p.Value).OrderBy(p => p), visited);
        Assert.Equal(1, peak);
        Assert.Equal(ProjectSyncStateKind.UpToDate, service.GetState(first).Kind);
        Assert.Equal(ProjectSyncStateKind.ConfigurationInvalid, service.GetState(second).Kind);
        Assert.Equal(ProjectSyncStateKind.UpToDate, service.GetState(third).Kind);
    }

    [Fact]
    public async Task PassDoesNotPublishPendingDeletionAutomatically()
    {
        ProjectId pendingDeletion = ProjectId.New();
        ProjectId pendingSync = ProjectId.New();
        ProjectDeletionIntent deletion = new(pendingDeletion.Value, "snapshot-hash",
            DateTimeOffset.UtcNow, 1);
        List<ProjectId> synced = [];
        using ProjectAutoSyncService service = new(new LinkStore(
            Link(pendingDeletion) with { SyncStatus = "PendingDeletion", PendingDeletion = deletion },
            Link(pendingSync)), (id, _) =>
            {
                synced.Add(id);
                return Task.FromResult(Link(id) with { SyncStatus = "Current" });
            });

        await service.RunPassAsync();

        Assert.DoesNotContain(pendingDeletion, synced);
        Assert.Contains(pendingSync, synced);
        Assert.Equal(ProjectSyncStateKind.LocalPending, service.GetState(pendingDeletion).Kind);
    }

    [Fact]
    public async Task DeferredEditDoesNotBlockOtherProjectAndResumesAfterEditCloses()
    {
        ProjectId deferred = ProjectId.New();
        ProjectId other = ProjectId.New();
        TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource resumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        using ProjectAutoSyncService service = new(new LinkStore(Link(deferred), Link(other)),
            (id, _) =>
            {
                if (id == deferred && Interlocked.Increment(ref attempts) == 1)
                    throw new ProjectSyncEditDeferredException();
                if (id == deferred) resumed.TrySetResult();
                return Task.FromResult(Link(id) with { SyncStatus = "Current" });
            }, editGate: new Gate(ready.Task));

        await service.RunPassAsync();
        Assert.Equal(ProjectSyncStateKind.UpToDate, service.GetState(other).Kind);
        Assert.Equal(ProjectSyncStateKind.Idle, service.GetState(deferred).Kind);
        ready.SetResult();
        await resumed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ProjectSyncStateKind.UpToDate, service.GetState(deferred).Kind);
    }

    [Fact]
    public async Task DisposeCancelsStalledTransport()
    {
        ProjectId project = ProjectId.New();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using ProjectAutoSyncService service = new(new LinkStore(Link(project)), async (_, token) =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { cancelled.SetResult(); throw; }
            return Link(project);
        });
        service.Start();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.Dispose();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AuthenticationStateAndGenericFailureDoNotExposeSensitiveErrors()
    {
        ProjectId auth = ProjectId.New();
        ProjectId failed = ProjectId.New();
        using ProjectAutoSyncService service = new(new LinkStore(Link(auth), Link(failed)),
            (id, _) => id == auth
                ? throw new ProjectSyncAuthenticationException("Repair Git credentials outside EntityTracker.")
                : throw new InvalidOperationException("https://user:secret@example.invalid/repository"));

        await service.RunPassAsync();

        Assert.Equal(ProjectSyncStateKind.AuthenticationRequired, service.GetState(auth).Kind);
        Assert.Contains("Repair Git credentials", service.GetState(auth).LastResult);
        Assert.Equal(ProjectSyncStateKind.Failed, service.GetState(failed).Kind);
        Assert.DoesNotContain("secret", service.GetState(failed).LastResult);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(60)]
    public async Task EachConfiguredIntervalSchedulesNextPass(int minutes)
    {
        ManualTimeProvider clock = new();
        TaskCompletionSource second = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int passes = 0;
        using ProjectAutoSyncService service = new(new LinkStore(Link(ProjectId.New())),
            (id, _) =>
            {
                if (Interlocked.Increment(ref passes) == 2) second.TrySetResult();
                return Task.FromResult(Link(id) with { SyncStatus = "Current" });
            }, intervalMinutes: minutes, clock: clock);

        service.Start();
        Assert.Equal(TimeSpan.FromMinutes(minutes),
            await clock.FirstDue.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, passes);
        clock.Fire();
        await second.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.Configure(false, minutes);
    }

    private static ProjectSyncLink Link(ProjectId id) => new(id.Value,
        @"C:\Projects\example", "main", "origin/main:hash", null, 1, null,
        "Pending", "Pending");

    private sealed class LinkStore(params ProjectSyncLink[] links) : IProjectSyncLinkStore
    {
        public Task<IReadOnlyList<ProjectSyncLink>> ReadAllAsync(CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<ProjectSyncLink>>(links);
        public Task SaveAsync(ProjectSyncLink link, CancellationToken token = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken token = default) => Task.CompletedTask;
    }

    private sealed class Gate(Task ready) : IProjectUnsavedEditsGate
    {
        public Task WaitUntilReadyAsync(ProjectId id, CancellationToken token = default) =>
            ready.WaitAsync(token);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private TimerStub? _timer;
        public TaskCompletionSource<TimeSpan> FirstDue { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ITimer CreateTimer(TimerCallback callback, object? state,
            TimeSpan dueTime, TimeSpan period)
        {
            _timer = new TimerStub(callback, state);
            FirstDue.TrySetResult(dueTime);
            return _timer;
        }
        public void Fire() => _timer?.Fire();

        private sealed class TimerStub(TimerCallback callback, object? state) : ITimer
        {
            private bool _disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
            public void Fire() { if (!_disposed) callback(state); }
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}

using EntityTracker.Application.GitSync;
using EntityTracker.Application.Snapshots;
using EntityTracker.Domain;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.Services;

/// <summary>#109: closing syncs the Projects with changes that are not synced yet, and says when one can't be.</summary>
public sealed class SyncBeforeClosingTests
{
    private static readonly Guid Synced = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Changed = Guid.Parse("00000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task ChangesNotSyncedYetAreFoundFromTheCurrentRevision()
    {
        // The icons are refreshed on a timer; closing reads the state afresh instead of trusting them.
        ProjectSyncIndicators indicators = new(
            new Links(Link(Synced, revision: 4), Link(Changed, revision: 4)),
            new Revisions(new() { [Synced] = 4, [Changed] = 5 }));

        IReadOnlyDictionary<Guid, ProjectSyncIndicator> current = await indicators.ReadCurrentAsync();

        Assert.Equal(ProjectSyncIndicatorKind.Synced, current[Synced].Kind);
        Assert.Equal(ProjectSyncIndicatorKind.Unsynced, current[Changed].Kind);
        Assert.Equal(ProjectSyncIndicatorKind.None, indicators.Get(Changed).Kind);
    }

    [Fact]
    public void NothingIsReportedWhenEveryProjectIsSynced() =>
        Assert.Null(ShellViewModel.DescribeUnsyncedProjects(
            new Dictionary<Guid, ProjectSyncIndicator>
            {
                [Synced] = new(ProjectSyncIndicatorKind.Synced, "Synced."),
                [Changed] = ProjectSyncIndicator.None
            },
            static _ => "Alpha", static _ => null));

    [Fact]
    public void AProjectThatCouldNotBeSyncedIsNamedWithTheReason()
    {
        string? problem = ShellViewModel.DescribeUnsyncedProjects(
            new Dictionary<Guid, ProjectSyncIndicator>
            {
                [Synced] = new(ProjectSyncIndicatorKind.Synced, "Synced."),
                [Changed] = new(ProjectSyncIndicatorKind.NeedsAttention, "Sync needs attention: …")
            },
            id => id == Changed ? "Order platform" : "Alpha",
            id => id == Changed ? "Git authentication failed. Repair access with command-line Git outside EntityTracker, then retry." : null);

        Assert.Equal("Not every Project could be synced. “Order platform”: Git authentication failed. " +
                     "Repair access with command-line Git outside EntityTracker, then retry.", problem);
    }

    [Fact]
    public void WithoutASyncResultTheIconsSentenceSaysWhy() =>
        Assert.Equal("Not every Project could be synced. “A Project”: Changes not synced yet.",
            ShellViewModel.DescribeUnsyncedProjects(
                new Dictionary<Guid, ProjectSyncIndicator> { [Changed] = new(ProjectSyncIndicatorKind.Unsynced, "Changes not synced yet.") },
                static _ => null, static _ => ""));

    [Fact]
    public void TheClosingNoticeKeepsItsCloseWithoutSyncingButtonWhileItProgresses()
    {
        NotificationCenter notifications = new();
        NotificationItem notice = notifications.BeginProgress("Closing EntityTracker", "Checking…");

        notifications.SetAction(notice, "Close without syncing", static () => Task.CompletedTask);
        notifications.Progress(notice, "Syncing your changes before closing…");

        Assert.True(notice.IsProgress);
        Assert.True(notice.HasAction);
        Assert.Equal("Close without syncing", notice.ActionLabel);
        Assert.Equal("Syncing your changes before closing…", notice.Message);

        notifications.Complete(notice, "Not every Project could be synced.", NotificationKind.Failure);
        Assert.False(notice.HasAction);
    }

    private static ProjectSyncLink Link(Guid project, long revision) =>
        new(project, @"C:\Projects\" + project, "main", "origin/main", "abc", revision, "hash", "Snapshot pushed", "Current");

    private sealed class Links(params ProjectSyncLink[] links) : IProjectSyncLinkStore
    {
        public Task<IReadOnlyList<ProjectSyncLink>> ReadAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProjectSyncLink>>(links);

        public Task SaveAsync(ProjectSyncLink link, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Revisions(Dictionary<Guid, long> revisions) : IProjectSnapshotStore
    {
        public Task<ProjectSnapshotRead> ReadAsync(ProjectId projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectSnapshotRead(null, revisions[projectId.Value]));

        public Task<long> ApplyAsync(ProjectSnapshot snapshot, long expectedRevision,
            CancellationToken cancellationToken = default) => Task.FromResult(0L);

        public Task<long> GetRevisionAsync(ProjectId projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(revisions[projectId.Value]);
    }
}

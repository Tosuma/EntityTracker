using EntityTracker.Application.GitSync;
using EntityTracker.Application.Snapshots;
using EntityTracker.Domain;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.Services;

public sealed class ProjectSyncIndicatorsTests
{
    [Fact]
    public async Task AnEditTurnsTheIconToUnsyncedAndOnlyARealChangeIsAnnounced()
    {
        Guid id = Guid.NewGuid();
        Links links = new([new ProjectSyncLink(id, "C:\\repo", "main", null, "abc", 4, "hash", "", "Current")]);
        Revisions revisions = new() { Revision = 4 };
        ProjectSyncIndicators indicators = new(links, revisions);
        int changes = 0;
        indicators.Changed += (_, _) => changes++;

        await indicators.RefreshAsync();
        Assert.Equal(ProjectSyncIndicatorKind.Synced, indicators.Get(id).Kind);
        Assert.Equal(1, changes);

        await indicators.RefreshAsync();
        Assert.Equal(1, changes);

        revisions.Revision = 5;
        await indicators.RefreshAsync();
        Assert.Equal(ProjectSyncIndicatorKind.Unsynced, indicators.Get(id).Kind);
        Assert.Equal(2, changes);

        Assert.Equal(ProjectSyncIndicatorKind.None, indicators.Get(Guid.NewGuid()).Kind);
    }

    private sealed class Links(IReadOnlyList<ProjectSyncLink> links) : IProjectSyncLinkStore
    {
        public Task<IReadOnlyList<ProjectSyncLink>> ReadAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(links);

        public Task SaveAsync(ProjectSyncLink link, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RemoveAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class Revisions : IProjectSnapshotStore
    {
        public long Revision { get; set; }

        public Task<ProjectSnapshotRead> ReadAsync(ProjectId projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectSnapshotRead(null, Revision));

        public Task<long> ApplyAsync(ProjectSnapshot snapshot, long expectedRevision,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

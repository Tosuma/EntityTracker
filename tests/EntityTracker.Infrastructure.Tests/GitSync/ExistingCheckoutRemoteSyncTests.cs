using System.Diagnostics;
using EntityTracker.Application.GitSync;
using EntityTracker.Application.Snapshots;
using EntityTracker.Domain;
using EntityTracker.Domain.Collaboration;
using EntityTracker.Infrastructure.GitSync;
using EntityTracker.Infrastructure.Persistence;
using EntityTracker.Infrastructure.Snapshots;
using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Tests.GitSync;

public sealed class ExistingCheckoutRemoteSyncTests
{
    [Fact]
    public async Task RemoteSnapshotBlobsAreBatchedAndOnlyReusedForTheSameCommit()
    {
        using GitWorkspace workspace = new();
        string publisher = workspace.CreateRemoteCheckout("publisher", Snapshot(Guid.NewGuid(), "Project"));
        Dictionary<string, byte[]> expected = new(StringComparer.Ordinal);
        for (int i = 0; i < 240; i++)
        {
            string relative = $".entitytracker/extra/{i:D4}.json";
            byte[] bytes = [0, 10, 255, (byte)i];
            string full = Path.Combine(publisher, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
            expected.Add(relative, bytes);
        }
        workspace.Git(publisher, "add", ".entitytracker");
        workspace.Git(publisher, "commit", "-m", "Add many snapshot blobs");
        workspace.Git(publisher, "push", "origin", "main");
        string checkout = workspace.Clone("reader");
        SystemGitTransport transport = new();
        List<GitFetchTiming> timings = [];
        ImmediateProgress<GitFetchTiming> report = new(timings.Add);

        GitRemoteState first = await transport.FetchAsync(checkout, null, default, report);
        Assert.Equal(1, Assert.Single(timings).BlobReadProcesses);
        Assert.True(first.SnapshotFiles.Count >= expected.Count);
        foreach ((string path, byte[] bytes) in expected)
            Assert.Equal(bytes, first.SnapshotFiles[path]);
        IReadOnlyDictionary<string, byte[]> historical =
            await transport.ReadSnapshotAtCommitAsync(checkout, first.Head);
        foreach ((string path, byte[] bytes) in expected)
            Assert.Equal(bytes, historical[path]);

        timings.Clear();
        GitRemoteState reused = await transport.FetchAsync(checkout, first, default, report);
        Assert.Same(first, reused);
        Assert.True(Assert.Single(timings).ReusedSnapshot);
        Assert.Equal(0, timings[0].BlobReadProcesses);

        File.WriteAllText(Path.Combine(publisher, "README.md"), "New revision\n");
        workspace.Git(publisher, "add", "README.md");
        workspace.Git(publisher, "commit", "-m", "Advance remote");
        workspace.Git(publisher, "push", "origin", "main");
        timings.Clear();
        GitRemoteState changed = await transport.FetchAsync(checkout, first, default, report);
        Assert.NotEqual(first.Head, changed.Head);
        Assert.False(Assert.Single(timings).ReusedSnapshot);
        Assert.Equal(1, timings[0].BlobReadProcesses);
        foreach ((string path, byte[] bytes) in expected)
            Assert.Equal(bytes, changed.SnapshotFiles[path]);
    }

    [Fact]
    public async Task BatchReadRejectsOversizedSnapshotBlob()
    {
        using GitWorkspace workspace = new();
        string publisher = workspace.CreateRemoteCheckout("publisher", Snapshot(Guid.NewGuid(), "Project"));
        string full = Path.Combine(publisher, ".entitytracker", "oversized.json");
        File.WriteAllBytes(full, new byte[8 * 1024 * 1024 + 1]);
        workspace.Git(publisher, "add", ".entitytracker");
        workspace.Git(publisher, "commit", "-m", "Oversized blob");
        workspace.Git(publisher, "push", "origin", "main");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new SystemGitTransport().FetchAsync(workspace.Clone("reader")));
    }

    [Fact]
    public async Task SyncReportsStageTimingAndSkipsUnchangedRemoteBlobs()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string checkout = workspace.CreateRemoteCheckout("reader", Snapshot(id, "Project"));
        await using TestCatalog catalog = await TestCatalog.CreateAsync(workspace, "catalog");
        ProjectGitSyncService sync = catalog.Sync(checkout);
        await sync.ImportAsync(checkout);
        List<ProjectSyncTiming> timings = [];

        ProjectSyncLink result = await sync.SyncNowAsync(new ProjectId(id),
            timing: new ImmediateProgress<ProjectSyncTiming>(timings.Add));

        Assert.Equal("Current", result.SyncStatus);
        ProjectSyncTiming timing = Assert.Single(timings);
        Assert.Equal("Completed", timing.Outcome);
        Assert.True(timing.Total > TimeSpan.Zero);
        Assert.Contains(ProjectSyncPhase.Exporting, timing.Stages.Keys);
        Assert.Contains(ProjectSyncPhase.Rechecking, timing.Stages.Keys);
        Assert.True(timing.GitSnapshotFileCount > 0);
        Assert.True(timing.ReusedSnapshots >= 2);
        Assert.Equal(0, timing.BlobReadProcesses);
    }

    [Fact]
    public async Task IndependentCollaboratorsConvergeWithTwoParentCanonicalCommit()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string first = workspace.CreateRemoteCheckout("first", Snapshot(id, "Project"));
        string second = workspace.Clone("second");
        await using TestCatalog left = await TestCatalog.CreateAsync(workspace, "left");
        await using TestCatalog right = await TestCatalog.CreateAsync(workspace, "right");
        ProjectGitSyncService leftSync = left.Sync(first, review: new ChooseRemoteReview());
        ProjectGitSyncService rightSync = right.Sync(second, review: new ChooseRemoteReview());
        await leftSync.ImportAsync(first);
        await rightSync.ImportAsync(second);
        ProjectId projectId = new(id);
        ProjectSnapshotRead leftRead = await left.Snapshots.ReadAsync(projectId);
        SnapshotTracker leftTracker = leftRead.Snapshot!.Trackers[0];
        SnapshotEntity leftEntity = leftTracker.Entities[0];
        await left.Snapshots.ApplyAsync(leftRead.Snapshot with
        {
            Trackers = [leftTracker with { Entities = leftTracker.Entities
                .Select(e => e.Id == leftEntity.Id ? e with { Notes = "Collaborator note" } : e).ToArray() },
                leftRead.Snapshot.Trackers[1]]
        }, leftRead.Revision);
        ProjectSnapshotRead rightRead = await right.Snapshots.ReadAsync(projectId);
        SnapshotTracker rightTracker = rightRead.Snapshot!.Trackers[0];
        await right.Snapshots.ApplyAsync(rightRead.Snapshot with
        {
            Trackers = [rightTracker with { Entities = rightTracker.Entities
                .Select(e => e.Id == leftEntity.Id ? e with { GroupName = "Shared team" } : e).ToArray() },
                rightRead.Snapshot.Trackers[1]]
        }, rightRead.Revision);
        string baseHead = workspace.Git(second, "rev-parse", "HEAD");
        await leftSync.SyncNowAsync(projectId);
        string remoteHead = workspace.Git(first, "rev-parse", "HEAD");
        List<ProjectSyncPhase> phases = [];
        ProjectSyncLink merged = await rightSync.SyncNowAsync(projectId,
            progress: new RecordingProgress(phases));
        Assert.Contains(ProjectSyncPhase.Fetching, phases);
        Assert.Contains(ProjectSyncPhase.Reviewing, phases);
        Assert.Contains(ProjectSyncPhase.Committing, phases);
        Assert.Contains(ProjectSyncPhase.Pushing, phases);
        Assert.True(phases.IndexOf(ProjectSyncPhase.Fetching) < phases.IndexOf(ProjectSyncPhase.Reviewing));
        Assert.True(phases.IndexOf(ProjectSyncPhase.Committing) < phases.IndexOf(ProjectSyncPhase.Pushing));
        Assert.Equal("Current", merged.SyncStatus);
        string mergeHead = workspace.Git(second, "rev-parse", "HEAD");
        Assert.Equal($"{baseHead} {remoteHead}", workspace.Git(second,
            "show", "-s", "--format=%P", mergeHead));
        await leftSync.SyncNowAsync(projectId);
        Assert.Equal(mergeHead, workspace.Git(first, "rev-parse", "HEAD"));
        ProjectSnapshotJsonCodec codec = new();
        ProjectSnapshot leftSnapshot = (await left.Snapshots.ReadAsync(projectId)).Snapshot!;
        ProjectSnapshot rightSnapshot = (await right.Snapshots.ReadAsync(projectId)).Snapshot!;
        Assert.Equal(codec.Encode(leftSnapshot).Sha256, codec.Encode(rightSnapshot).Sha256);
        Assert.Equal(3, rightSnapshot.Trackers[0].ProgressHistory.Count);
        SnapshotEntity entity = rightSnapshot.Trackers[0].Entities.Single(e => e.Id == leftEntity.Id);
        Assert.Equal("Collaborator note", entity.Notes);
        Assert.Equal("Shared team", entity.GroupName);
    }

    [Fact]
    public async Task AlreadyFastForwardedCheckoutMergesSeparateSqliteEditAgainstRecordedBase()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string first = workspace.CreateRemoteCheckout("first", Snapshot(id, "Project"));
        string second = workspace.Clone("second");
        await using TestCatalog catalog = await TestCatalog.CreateAsync(workspace, "catalog");
        ProjectGitSyncService sync = catalog.Sync(second);
        await sync.ImportAsync(second);
        ProjectId projectId = new(id);
        ProjectSnapshotRead local = await catalog.Snapshots.ReadAsync(projectId);
        await catalog.Snapshots.ApplyAsync(local.Snapshot! with
        {
            Project = local.Snapshot.Project with { Name = "Local edit" }
        }, local.Revision);
        ProjectSnapshot remote = Snapshot(id, "Project");
        SnapshotTracker tracker = remote.Trackers[0];
        remote = remote with { Trackers = [tracker with
        {
            Entities = tracker.Entities.Select(e => e with
            {
                GroupName = e.SourceName == "Orders" ? "Remote team" : e.GroupName
            }).ToArray()
        }, remote.Trackers[1]] };
        foreach ((string relative, byte[] bytes) in new ProjectSnapshotJsonCodec().Encode(remote).Files)
            File.WriteAllBytes(Path.Combine(first,
                relative.Replace('/', Path.DirectorySeparatorChar)), bytes);
        workspace.Git(first, "add", ".entitytracker");
        workspace.Git(first, "commit", "-m", "Remote entity edit");
        workspace.Git(first, "push", "origin", "main");
        workspace.Git(second, "pull", "--ff-only");
        string remoteHead = workspace.Git(second, "rev-parse", "HEAD");

        Assert.Equal("Current", (await sync.SyncNowAsync(projectId)).SyncStatus);

        Assert.Equal(remoteHead, workspace.Git(second, "rev-parse", "HEAD^"));
        ProjectSnapshot merged = (await catalog.Snapshots.ReadAsync(projectId)).Snapshot!;
        Assert.Equal("Local edit", merged.Project.Name);
        Assert.Equal("Remote team", merged.Trackers[0].Entities.Single(e => e.SourceName == "Orders").GroupName);
        Assert.Equal(workspace.Git(second, "rev-parse", "HEAD"),
            workspace.Git(workspace.Root, "--git-dir", Path.Combine(workspace.Root, "remote.git"),
                "rev-parse", "refs/heads/main"));
    }

    [Fact]
    public async Task DivergentReadmeIsRejectedBeforeSqliteMerge()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string first = workspace.CreateRemoteCheckout("first", Snapshot(id, "Project"));
        string second = workspace.Clone("second");
        await using TestCatalog catalog = await TestCatalog.CreateAsync(workspace, "catalog");
        await catalog.Sync(second).ImportAsync(second);
        ProjectId projectId = new(id);
        ProjectSnapshotRead read = await catalog.Snapshots.ReadAsync(projectId);
        await catalog.Snapshots.ApplyAsync(read.Snapshot! with
        {
            Project = read.Snapshot.Project with { Name = "Local edit" }
        }, read.Revision);
        File.WriteAllText(Path.Combine(first, "README.md"), "Remote documentation\n");
        foreach ((string relative, byte[] bytes) in new ProjectSnapshotJsonCodec()
            .Encode(Snapshot(id, "Remote edit")).Files)
            File.WriteAllBytes(Path.Combine(first,
                relative.Replace('/', Path.DirectorySeparatorChar)), bytes);
        workspace.Git(first, "add", "README.md", ".entitytracker");
        workspace.Git(first, "commit", "-m", "Edit documentation");
        workspace.Git(first, "push", "origin", "main");
        string localHead = workspace.Git(second, "rev-parse", "HEAD");

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => catalog.Sync(second).SyncNowAsync(projectId));

        Assert.Contains("command-line Git", error.Message, StringComparison.Ordinal);
        Assert.Equal(localHead, workspace.Git(second, "rev-parse", "HEAD"));
        Assert.Equal("Local edit", (await catalog.Snapshots.ReadAsync(projectId)).Snapshot!.Project.Name);
    }

    [Fact]
    public async Task MergeRevisionRaceRollsBackWithoutCommittingGit()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string first = workspace.CreateRemoteCheckout("first", Snapshot(id, "Project"));
        string second = workspace.Clone("second");
        await using TestCatalog catalog = await TestCatalog.CreateAsync(workspace, "catalog");
        await catalog.Sync(second).ImportAsync(second);
        ProjectId projectId = new(id);
        ProjectSnapshotRead read = await catalog.Snapshots.ReadAsync(projectId);
        ProjectSnapshot local = read.Snapshot! with
        {
            Project = read.Snapshot.Project with { Name = "Local edit" }
        };
        await catalog.Snapshots.ApplyAsync(local, read.Revision);
        foreach ((string relative, byte[] bytes) in new ProjectSnapshotJsonCodec()
            .Encode(Snapshot(id, "Remote edit")).Files)
            File.WriteAllBytes(Path.Combine(first,
                relative.Replace('/', Path.DirectorySeparatorChar)), bytes);
        workspace.Git(first, "add", ".entitytracker");
        workspace.Git(first, "commit", "-m", "Remote change");
        workspace.Git(first, "push", "origin", "main");
        string localHead = workspace.Git(second, "rev-parse", "HEAD");
        IProjectSyncBackup racing = new RevisionChangingBackup(catalog.Backup(),
            catalog.Snapshots, projectId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            catalog.Sync(second, backup: racing, review: new ChooseRemoteReview())
                .SyncNowAsync(projectId));

        Assert.Equal(localHead, workspace.Git(second, "rev-parse", "HEAD"));
        Assert.Equal("Competing edit", (await catalog.Snapshots.ReadAsync(projectId)).Snapshot!.Project.Name);
    }

    [Fact]
    public async Task AutomaticSyncDefersConflictsWithoutChangingSqliteOrOpeningReview()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string publisher = workspace.CreateRemoteCheckout("publisher", Snapshot(id, "Project"));
        string receiver = workspace.Clone("receiver");
        await using TestCatalog catalog = await TestCatalog.CreateAsync(workspace, "catalog");
        await catalog.Sync(receiver).ImportAsync(receiver);
        ProjectId projectId = new(id);
        ProjectSnapshotRead read = await catalog.Snapshots.ReadAsync(projectId);
        await catalog.Snapshots.ApplyAsync(read.Snapshot! with
        {
            Project = read.Snapshot.Project with { Name = "Local edit" }
        }, read.Revision);
        foreach ((string relative, byte[] bytes) in new ProjectSnapshotJsonCodec()
            .Encode(Snapshot(id, "Remote edit")).Files)
            File.WriteAllBytes(Path.Combine(publisher,
                relative.Replace('/', Path.DirectorySeparatorChar)), bytes);
        workspace.Git(publisher, "add", ".entitytracker");
        workspace.Git(publisher, "commit", "-m", "Remote change");
        workspace.Git(publisher, "push", "origin", "main");
        string head = workspace.Git(receiver, "rev-parse", "HEAD");
        int backupsBefore = Directory.GetFiles(catalog.BackupDirectory,
            "entity-tracker-pre-sync-*.db", SearchOption.AllDirectories).Length;

        await Assert.ThrowsAsync<ProjectSyncReviewRequiredException>(() =>
            catalog.Sync(receiver).SyncNowAsync(projectId, mode: ProjectSyncMode.Automatic));

        Assert.Equal(head, workspace.Git(receiver, "rev-parse", "HEAD"));
        Assert.Equal("Local edit", (await catalog.Snapshots.ReadAsync(projectId)).Snapshot!.Project.Name);
        Assert.Equal(backupsBefore, Directory.GetFiles(catalog.BackupDirectory,
            "entity-tracker-pre-sync-*.db", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task SyncWaitsForUnfinishedEditThenResumesAndCanBeCancelled()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string first = workspace.CreateRemoteCheckout("publisher", Snapshot(id, "Project"));
        string second = workspace.Clone("receiver");
        await using TestCatalog publisher = await TestCatalog.CreateAsync(workspace, "publisher-db");
        await using TestCatalog receiver = await TestCatalog.CreateAsync(workspace, "receiver-db");
        ProjectGitSyncService publishing = publisher.Sync(first);
        await publishing.ImportAsync(first);
        await receiver.Sync(second).ImportAsync(second);
        ProjectId projectId = new(id);
        ProjectSnapshotRead source = await publisher.Snapshots.ReadAsync(projectId);
        await publisher.Snapshots.ApplyAsync(source.Snapshot! with
        {
            Project = source.Snapshot.Project with { Name = "Incoming edit" }
        }, source.Revision);
        await publishing.SyncNowAsync(projectId);
        string before = workspace.Git(second, "rev-parse", "HEAD");
        BlockingEditGate gate = new();
        ProjectGitSyncService receiving = receiver.Sync(second, editGate: gate);
        using CancellationTokenSource cancel = new();
        List<ProjectSyncTiming> cancelledTimings = [];
        Task<ProjectSyncLink> cancelled = receiving.SyncNowAsync(projectId, cancel.Token,
            timing: new ImmediateProgress<ProjectSyncTiming>(cancelledTimings.Add));
        await gate.Entered;
        Assert.Equal(before, workspace.Git(second, "rev-parse", "HEAD"));
        Assert.Equal("Project", (await receiver.Snapshots.ReadAsync(projectId)).Snapshot!.Project.Name);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal("Cancelled", Assert.Single(cancelledTimings).Outcome);
        Assert.Contains(ProjectSyncPhase.CheckingEdits, cancelledTimings[0].Stages.Keys);

        BlockingEditGate released = new();
        List<ProjectSyncTiming> resumedTimings = [];
        Task<ProjectSyncLink> resumed = receiver.Sync(second, editGate: released).SyncNowAsync(projectId,
            timing: new ImmediateProgress<ProjectSyncTiming>(resumedTimings.Add));
        await released.Entered;
        released.Release();
        Assert.Equal("Current", (await resumed).SyncStatus);
        Assert.Equal("Completed", Assert.Single(resumedTimings).Outcome);
        Assert.Equal("Incoming edit", (await receiver.Snapshots.ReadAsync(projectId)).Snapshot!.Project.Name);
    }

    [Fact]
    public async Task TrackerDeletionApprovalListsItsNestedObjects()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string checkout = workspace.CreateLocalCheckout("project", Snapshot(id, "Project"));
        await using TestCatalog catalog = await TestCatalog.CreateAsync(workspace, "catalog");
        RecordingApproval approval = new();
        ProjectGitSyncService sync = catalog.Sync(checkout, approval: approval);
        await sync.ImportAsync(checkout);
        ProjectId projectId = new(id);
        ProjectSnapshotRead read = await catalog.Snapshots.ReadAsync(projectId);
        SnapshotTracker removed = read.Snapshot!.Trackers[1];
        await catalog.Snapshots.ApplyAsync(read.Snapshot with
        {
            Trackers = [read.Snapshot.Trackers[0]]
        }, read.Revision);

        await sync.SyncNowAsync(projectId);

        IReadOnlyList<string> deletionSet = Assert.Single(approval.Calls);
        Assert.Contains($"Tracker {removed.Id:D}", deletionSet);
        Assert.Contains($"Entity {removed.Entities[0].Id:D}", deletionSet);
        Assert.Contains($"Progress record {removed.ProgressHistory[0].SnapshotId:D}", deletionSet);
    }

    [Fact]
    public async Task AutomaticSyncDefersDeletionWithoutOpeningApprovalOrChangingRepository()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string checkout = workspace.CreateLocalCheckout("project", Snapshot(id, "Project"));
        await using TestCatalog catalog = await TestCatalog.CreateAsync(workspace, "catalog");
        RecordingApproval approval = new();
        ProjectGitSyncService sync = catalog.Sync(checkout, approval: approval);
        await sync.ImportAsync(checkout);
        ProjectId projectId = new(id);
        ProjectSnapshotRead read = await catalog.Snapshots.ReadAsync(projectId);
        await catalog.Snapshots.ApplyAsync(read.Snapshot! with
        {
            Trackers = [read.Snapshot.Trackers[0]]
        }, read.Revision);
        string head = workspace.Git(checkout, "rev-parse", "HEAD");

        await Assert.ThrowsAsync<ProjectSyncDeletionApprovalRequiredException>(() =>
            sync.SyncNowAsync(projectId, mode: ProjectSyncMode.Automatic));

        Assert.Empty(approval.Calls);
        Assert.Equal(head, workspace.Git(checkout, "rev-parse", "HEAD"));
        Assert.Single((await catalog.Snapshots.ReadAsync(projectId)).Snapshot!.Trackers);
    }

    [Fact]
    public async Task PendingDeletionCannotPublishWhileSqliteProjectStillExists()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string checkout = workspace.CreateLocalCheckout("project", Snapshot(id, "Project"));
        await using TestCatalog catalog = await TestCatalog.CreateAsync(workspace, "catalog");
        ProjectGitSyncService sync = catalog.Sync(checkout);
        ProjectSyncLink link = await sync.ImportAsync(checkout);
        ProjectSnapshotRead read = await catalog.Snapshots.ReadAsync(new ProjectId(id));
        await catalog.Links.SaveAsync(link with
        {
            PendingDeletion = new ProjectDeletionIntent(id,
                new ProjectSnapshotJsonCodec().Encode(read.Snapshot!).Sha256,
                DateTimeOffset.UtcNow, read.Revision)
        });
        string before = workspace.Git(checkout, "rev-parse", "HEAD");

        await Assert.ThrowsAsync<InvalidOperationException>(() => sync.SyncNowAsync(new ProjectId(id)));

        Assert.Equal(before, workspace.Git(checkout, "rev-parse", "HEAD"));
        Assert.NotNull((await catalog.Snapshots.ReadAsync(new ProjectId(id))).Snapshot);
    }

    [Fact]
    public async Task ProjectTombstoneSurvivesLocalPurgeAndDeletesReceivingCatalog()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string first = workspace.CreateRemoteCheckout("publisher", Snapshot(id, "Project"));
        string second = workspace.Clone("receiver");
        await using TestCatalog publisher = await TestCatalog.CreateAsync(workspace, "publisher-db");
        await using TestCatalog receiver = await TestCatalog.CreateAsync(workspace, "receiver-db");
        ProjectGitSyncService publish = publisher.Sync(first, review: new ChooseRemoteReview());
        ProjectGitSyncService receive = receiver.Sync(second, review: new ChooseRemoteReview());
        await publish.ImportAsync(first);
        await receive.ImportAsync(second);
        ProjectId projectId = new(id);
        ProjectSnapshotRead read = await publisher.Snapshots.ReadAsync(projectId);
        await publisher.Snapshots.ApplyAsync(read.Snapshot! with
        {
            Project = read.Snapshot.Project with
            {
                LifecycleState = "Recycled",
                RecycledAtUtc = read.Snapshot.Project.UpdatedAtUtc.AddDays(1)
            }
        }, read.Revision);
        Assert.True(await publish.PurgeLinkedProjectAsync(projectId));
        Assert.Null((await publisher.Snapshots.ReadAsync(projectId)).Snapshot);
        Assert.Single(await publish.ListPendingDeletionsAsync());
        Assert.NotNull((await publish.GetLinkAsync(projectId))!.PendingDeletion);

        await publish.SyncNowAsync(projectId);
        Assert.Null(await publish.GetLinkAsync(projectId));
        Assert.True(File.Exists(Path.Combine(first, ".entitytracker", "deleted-project.json")));
        await receive.SyncNowAsync(projectId);
        Assert.Null((await receiver.Snapshots.ReadAsync(projectId)).Snapshot);
        Assert.Null(await receive.GetLinkAsync(projectId));
        Assert.Equal(workspace.Git(first, "rev-parse", "HEAD"),
            workspace.Git(second, "rev-parse", "HEAD"));
    }

    [Fact]
    public async Task RemoteProjectAdvanceRequiresFreshDeletionApprovalBeforeTombstoneMerge()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string publisherCheckout = workspace.CreateRemoteCheckout("publisher", Snapshot(id, "Project"));
        string other = workspace.Clone("other");
        await using TestCatalog catalog = await TestCatalog.CreateAsync(workspace, "publisher-db");
        RecordingApproval approval = new();
        ProjectGitSyncService sync = catalog.Sync(publisherCheckout, approval: approval);
        await sync.ImportAsync(publisherCheckout);
        ProjectId projectId = new(id);
        ProjectSnapshotRead read = await catalog.Snapshots.ReadAsync(projectId);
        await catalog.Snapshots.ApplyAsync(read.Snapshot! with
        {
            Project = read.Snapshot.Project with
            {
                LifecycleState = "Recycled",
                RecycledAtUtc = read.Snapshot.Project.UpdatedAtUtc.AddDays(1)
            }
        }, read.Revision);
        Assert.True(await sync.PurgeLinkedProjectAsync(projectId));
        string localHead = workspace.Git(publisherCheckout, "rev-parse", "HEAD");
        foreach ((string relative, byte[] bytes) in new ProjectSnapshotJsonCodec()
            .Encode(Snapshot(id, "Remote update")).Files)
            File.WriteAllBytes(Path.Combine(other,
                relative.Replace('/', Path.DirectorySeparatorChar)), bytes);
        workspace.Git(other, "add", ".entitytracker");
        workspace.Git(other, "commit", "-m", "Advance Project");
        workspace.Git(other, "push", "origin", "main");
        string remoteHead = workspace.Git(other, "rev-parse", "HEAD");

        ProjectSyncLink result = await sync.SyncNowAsync(projectId);

        Assert.Equal("Deleted", result.SyncStatus);
        Assert.Equal(2, approval.Calls.Count);
        Assert.Contains($"Project {id:D}", approval.Calls[1]);
        Assert.Equal($"{localHead} {remoteHead}", workspace.Git(publisherCheckout,
            "show", "-s", "--format=%P", "HEAD"));
        Assert.Null(await sync.GetLinkAsync(projectId));
    }

    [Fact]
    public async Task InboundTombstoneRecoversIfCheckoutUpdateFailsAfterSqlitePurge()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string first = workspace.CreateRemoteCheckout("publisher", Snapshot(id, "Project"));
        string second = workspace.Clone("receiver");
        await using TestCatalog publisher = await TestCatalog.CreateAsync(workspace, "publisher-db");
        await using TestCatalog receiver = await TestCatalog.CreateAsync(workspace, "receiver-db");
        ProjectGitSyncService publishing = publisher.Sync(first);
        await publishing.ImportAsync(first);
        await receiver.Sync(second).ImportAsync(second);
        ProjectId projectId = new(id);
        ProjectSnapshotRead read = await publisher.Snapshots.ReadAsync(projectId);
        await publisher.Snapshots.ApplyAsync(read.Snapshot! with
        {
            Project = read.Snapshot.Project with
            {
                LifecycleState = "Recycled",
                RecycledAtUtc = read.Snapshot.Project.UpdatedAtUtc.AddDays(1)
            }
        }, read.Revision);
        await publishing.PurgeLinkedProjectAsync(projectId);
        await publishing.SyncNowAsync(projectId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => receiver.Sync(second,
            transport: new FailFastForwardTransport()).SyncNowAsync(projectId));
        Assert.Null((await receiver.Snapshots.ReadAsync(projectId)).Snapshot);
        Assert.NotNull(await receiver.Sync(second).GetLinkAsync(projectId));

        Assert.Equal("Deleted", (await receiver.Sync(second).SyncNowAsync(projectId)).SyncStatus);
        Assert.Null(await receiver.Sync(second).GetLinkAsync(projectId));
        Assert.Equal(workspace.Git(first, "rev-parse", "HEAD"),
            workspace.Git(second, "rev-parse", "HEAD"));
    }

    [Fact]
    public async Task ConcurrentLocalChangeCanBeKeptUnlinkedAfterRemoteDeletion()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string first = workspace.CreateRemoteCheckout("publisher", Snapshot(id, "Project"));
        string second = workspace.Clone("receiver");
        await using TestCatalog publisher = await TestCatalog.CreateAsync(workspace, "publisher-db");
        await using TestCatalog receiver = await TestCatalog.CreateAsync(workspace, "receiver-db");
        ProjectGitSyncService publish = publisher.Sync(first);
        ProjectGitSyncService receive = receiver.Sync(second, review: new ChooseLocalReview());
        await publish.ImportAsync(first);
        await receive.ImportAsync(second);
        ProjectId projectId = new(id);
        ProjectSnapshotRead original = await receiver.Snapshots.ReadAsync(projectId);
        await receiver.Snapshots.ApplyAsync(original.Snapshot! with
        {
            Project = original.Snapshot.Project with { Name = "Local work" }
        }, original.Revision);
        ProjectSnapshotRead read = await publisher.Snapshots.ReadAsync(projectId);
        await publisher.Snapshots.ApplyAsync(read.Snapshot! with
        {
            Project = read.Snapshot.Project with
            {
                LifecycleState = "Recycled",
                RecycledAtUtc = read.Snapshot.Project.UpdatedAtUtc.AddDays(1)
            }
        }, read.Revision);
        await publish.PurgeLinkedProjectAsync(projectId);
        await publish.SyncNowAsync(projectId);

        ProjectSyncLink kept = await receive.SyncNowAsync(projectId);
        Assert.Equal("RemoteDeletedLocalKept", kept.SyncStatus);
        Assert.Equal("Local work", (await receiver.Snapshots.ReadAsync(projectId)).Snapshot!.Project.Name);
        Assert.Contains("deleted", kept.LastResult, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TombstoneCodecRejectsInvalidDocumentsAndPreservesCanonicalBytes()
    {
        ProjectSnapshotJsonCodec codec = new();
        ProjectTombstone tombstone = new(1, Guid.NewGuid(), new string('a', 64),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        ProjectSnapshotPackage encoded = codec.EncodeTombstone(tombstone);
        Assert.True(codec.TryDecodeTombstone(encoded.Files, out ProjectTombstone? decoded));
        Assert.Equal(tombstone, decoded);
        Assert.Equal(encoded.Sha256, codec.EncodeTombstone(decoded!).Sha256);
        Dictionary<string, byte[]> invalid = encoded.Files.ToDictionary(pair => pair.Key, pair => pair.Value);
        invalid[".entitytracker/project.json"] = [];
        Assert.Throws<InvalidDataException>(() => codec.TryDecodeTombstone(invalid, out _));
        Assert.Throws<ProjectSnapshotFormatVersionException>(() => codec.EncodeTombstone(tombstone with
        {
            FormatVersion = 5
        }));
        Dictionary<string, byte[]> newerManifest = encoded.Files.ToDictionary(pair => pair.Key, pair => pair.Value);
        newerManifest[".entitytracker/manifest.json"] = System.Text.Encoding.UTF8.GetBytes(
            $"{{\"formatVersion\":5,\"projectId\":\"{tombstone.ProjectId:D}\"}}");
        ProjectSnapshotFormatVersionException versionError =
            Assert.Throws<ProjectSnapshotFormatVersionException>(() =>
                codec.TryDecodeTombstone(newerManifest, out _));
        Assert.Equal(ProjectSnapshot.CurrentFormatVersion, versionError.ApplicationFormatVersion);
        Assert.Equal(5, versionError.ProjectFormatVersion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleSqliteRevisionOrRemoteHeadInvalidatesMergeReview(bool advanceRemote)
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string first = workspace.CreateRemoteCheckout("first", Snapshot(id, "Project"));
        string second = workspace.Clone("second");
        await using TestCatalog left = await TestCatalog.CreateAsync(workspace, "left");
        await using TestCatalog right = await TestCatalog.CreateAsync(workspace, "right");
        ProjectGitSyncService leftSync = left.Sync(first);
        await leftSync.ImportAsync(first);
        await right.Sync(second).ImportAsync(second);
        ProjectId projectId = new(id);
        ProjectSnapshotRead leftRead = await left.Snapshots.ReadAsync(projectId);
        SnapshotTracker lt = leftRead.Snapshot!.Trackers[0];
        await left.Snapshots.ApplyAsync(leftRead.Snapshot with
        {
            Trackers = [lt with { Entities = lt.Entities.Select(e =>
                e with { Notes = "Remote note" }).ToArray() }, leftRead.Snapshot.Trackers[1]]
        }, leftRead.Revision);
        await leftSync.SyncNowAsync(projectId);
        ProjectSnapshotRead rightRead = await right.Snapshots.ReadAsync(projectId);
        SnapshotTracker rt = rightRead.Snapshot!.Trackers[0];
        await right.Snapshots.ApplyAsync(rightRead.Snapshot with
        {
            Trackers = [rt with { Entities = rt.Entities.Select(e =>
                e with { Notes = "Local note" }).ToArray() }, rightRead.Snapshot.Trackers[1]]
        }, rightRead.Revision);
        string originalHead = workspace.Git(second, "rev-parse", "HEAD");
        IProjectMergeReview review = new MutatingReview(async () =>
        {
            if (advanceRemote)
            {
                workspace.Git(first, "commit", "--allow-empty", "-m", "Advance during review");
                workspace.Git(first, "push", "origin", "main");
            }
            else
            {
                ProjectSnapshotRead changed = await right.Snapshots.ReadAsync(projectId);
                await right.Snapshots.ApplyAsync(changed.Snapshot! with
                {
                    Project = changed.Snapshot.Project with { Name = "Changed during review" }
                }, changed.Revision);
            }
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            right.Sync(second, review: review).SyncNowAsync(projectId));
        Assert.Equal(originalHead, workspace.Git(second, "rev-parse", "HEAD"));
        Assert.Equal("Local note", (await right.Snapshots.ReadAsync(projectId))
            .Snapshot!.Trackers[0].Entities[0].Notes);
    }

    [Fact]
    public async Task ProjectDeletionApprovalIsBoundToExactProjectRevision()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string checkout = workspace.CreateRemoteCheckout("first", Snapshot(id, "Project"));
        await using TestCatalog catalog = await TestCatalog.CreateAsync(workspace, "catalog");
        await catalog.Sync(checkout).ImportAsync(checkout);
        ProjectId projectId = new(id);
        ProjectSnapshotRead read = await catalog.Snapshots.ReadAsync(projectId);
        await catalog.Snapshots.ApplyAsync(read.Snapshot! with
        {
            Project = read.Snapshot.Project with
            {
                LifecycleState = "Recycled",
                RecycledAtUtc = read.Snapshot.Project.UpdatedAtUtc.AddDays(1)
            }
        }, read.Revision);
        IOutboundDeletionApproval approval = new MutatingApproval(async () =>
        {
            ProjectSnapshotRead changed = await catalog.Snapshots.ReadAsync(projectId);
            await catalog.Snapshots.ApplyAsync(changed.Snapshot! with
            {
                Project = changed.Snapshot.Project with { Name = "Changed after approval" }
            }, changed.Revision);
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            catalog.Sync(checkout, approval: approval).PurgeLinkedProjectAsync(projectId));
        Assert.Equal("Changed after approval",
            (await catalog.Snapshots.ReadAsync(projectId)).Snapshot!.Project.Name);
        Assert.Empty(await catalog.Sync(checkout).ListPendingDeletionsAsync());
    }
    [Theory]
    [InlineData("clone", "https://example.invalid/repo.git")]
    [InlineData("init", "--bare")]
    [InlineData("remote", "add")]
    [InlineData("checkout", "main")]
    [InlineData("reset", "--hard")]
    [InlineData("stash", "push")]
    [InlineData("push", "--force")]
    [InlineData("merge", "--no-ff")]
    [InlineData("config", "user.name")]
    [InlineData("cat-file", "--batch-all-objects")]
    public void GitAdapterRejectsRepositoryManagementCommands(string verb, string argument) =>
        Assert.Throws<InvalidOperationException>(() => SystemGitTransport.ValidateCommand([verb, argument]));

    [Theory]
    [InlineData("missing")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa tree 4")]
    [InlineData("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb blob 4")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa blob -1")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa blob 8388609")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa blob unknown")]
    public void BatchReaderRejectsMalformedBlobHeaders(string header) =>
        Assert.Throws<InvalidDataException>(() => SystemGitTransport.ParseBatchHeader(
            header, new string('a', 40)));

    [Fact]
    public async Task ExistingVersion13DatabaseMigratesLocalNameStorage()
    {
        using GitWorkspace workspace = new();
        string path = Path.Combine(workspace.Root, "migration.db");
        SqliteDatabase database = new(path);
        await database.InitializeAsync();
        await using (SqliteConnection connection = new($"Data Source={path}"))
        {
            await connection.OpenAsync();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "DROP TABLE project_snapshot_names; PRAGMA user_version = 13;";
            await command.ExecuteNonQueryAsync();
        }
        await database.InitializeAsync();
        Assert.Equal(SqliteDatabase.CurrentSchemaVersion, await database.GetStoredSchemaVersionAsync());
        await using SqliteConnection verified = await database.OpenConnectionAsync(CancellationToken.None);
        using SqliteCommand check = verified.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'project_snapshot_names';";
        Assert.Equal(1L, await check.ExecuteScalarAsync());
    }

    [Fact]
    public async Task ImportPushAndFastForwardPreserveIdentityAndCreateInboundBackup()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string first = workspace.CreateRemoteCheckout("first", Snapshot(id, "Project"));
        string second = workspace.Clone("second");
        await using TestCatalog firstCatalog = await TestCatalog.CreateAsync(workspace, "first-db");
        await using TestCatalog secondCatalog = await TestCatalog.CreateAsync(workspace, "second-db");
        ProjectGitSyncService firstSync = firstCatalog.Sync(first);
        ProjectGitSyncService secondSync = secondCatalog.Sync(second);
        await firstSync.ImportAsync(first);
        await secondSync.ImportAsync(second);

        ProjectId projectId = new(id);
        ProjectSnapshotRead before = await firstCatalog.Snapshots.ReadAsync(projectId);
        Assert.Equal(id, before.Snapshot!.Project.Id);
        Assert.Equal(2, before.Snapshot.Trackers.Count);
        Assert.Equal(new ProjectSnapshotJsonCodec().Encode(Snapshot(id, "Project")).Sha256,
            new ProjectSnapshotJsonCodec().Encode(before.Snapshot).Sha256);
        await firstCatalog.Snapshots.ApplyAsync(before.Snapshot with
        {
            Project = before.Snapshot.Project with { Name = "Renamed project" }
        }, before.Revision);
        ProjectSyncLink pushed = await firstSync.SyncNowAsync(projectId);
        Assert.Equal("Current", pushed.SyncStatus);
        Assert.Equal("Snapshot pushed", pushed.LastResult);

        ProjectSyncLink received = await secondSync.SyncNowAsync(projectId);
        Assert.Equal("Current", received.SyncStatus);
        Assert.Equal("Renamed project", (await secondCatalog.Snapshots.ReadAsync(projectId)).Snapshot!.Project.Name);
        Assert.NotEmpty(Directory.GetFiles(secondCatalog.BackupDirectory,
            "entity-tracker-pre-sync-*.db", SearchOption.AllDirectories));
        Assert.Equal(workspace.Git(first, "rev-parse", "HEAD"), workspace.Git(second, "rev-parse", "HEAD"));
    }

    [Fact]
    public async Task NameCollisionUsesPrivateAliasAndKeepsCanonicalSnapshot()
    {
        using GitWorkspace workspace = new();
        Guid firstId = Guid.NewGuid();
        string first = workspace.CreateRemoteCheckout("first", Snapshot(firstId, "Shared name"));
        await using TestCatalog catalog = await TestCatalog.CreateAsync(workspace, "catalog");
        await catalog.Sync(first).ImportAsync(first);

        Guid secondId = Guid.NewGuid();
        string second = workspace.CreateLocalCheckout("second", Snapshot(secondId, "Shared name"));
        ProjectGitSyncService sync = catalog.Sync(second);
        await Assert.ThrowsAsync<ProjectNameCollisionException>(() => sync.ImportAsync(second));
        Assert.Null((await catalog.Snapshots.ReadAsync(new ProjectId(secondId))).Snapshot);
        await sync.ImportAsync(second, "Second local name");
        Assert.Equal("Second local name", (await catalog.Projects.GetAsync(new ProjectId(secondId)))!.Name);
        Assert.Equal("Shared name", (await catalog.Snapshots.ReadAsync(new ProjectId(secondId))).Snapshot!.Project.Name);
        Assert.Equal("Already current", (await sync.SyncNowAsync(new ProjectId(secondId))).LastResult);
    }

    [Fact]
    public async Task PristineDefaultProjectCanBeReplacedByImportedDefaultName()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string checkout = workspace.CreateLocalCheckout("default", Snapshot(id, "Default project"));
        await using TestCatalog catalog = await TestCatalog.CreateAsync(workspace, "catalog");
        await catalog.Sync(checkout).ImportAsync(checkout);
        Project imported = Assert.Single(await catalog.Projects.GetAllAsync());
        Assert.Equal(id, imported.Id.Value);
        Assert.Equal("Default project", imported.Name);
    }

    [Fact]
    public async Task InvalidSnapshotAndDirtyCheckoutLeaveCatalogUntouched()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string checkout = workspace.CreateLocalCheckout("invalid", Snapshot(id, "Project"));
        await using TestCatalog catalog = await TestCatalog.CreateAsync(workspace, "catalog");
        ProjectGitSyncService sync = catalog.Sync(checkout);
        File.WriteAllText(Path.Combine(checkout, "untracked.txt"), "dirty");
        await Assert.ThrowsAsync<InvalidOperationException>(() => sync.ImportAsync(checkout));
        File.Delete(Path.Combine(checkout, "untracked.txt"));
        File.WriteAllText(Path.Combine(checkout, ".entitytracker", "manifest.json"), "{invalid");
        workspace.Git(checkout, "add", ".entitytracker");
        workspace.Git(checkout, "commit", "-m", "Invalid");
        await Assert.ThrowsAnyAsync<Exception>(() => sync.ImportAsync(checkout));
        Assert.Null((await catalog.Snapshots.ReadAsync(new ProjectId(id))).Snapshot);
        Assert.False(Directory.Exists(catalog.BackupDirectory));
    }

    [Fact]
    public async Task SameIdLinksOnlyAnIdenticalSnapshot()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string first = workspace.CreateRemoteCheckout("first", Snapshot(id, "Project"));
        string identical = workspace.Clone("identical");
        string differing = workspace.CreateLocalCheckout("differing", Snapshot(id, "Changed"));
        await using TestCatalog catalog = await TestCatalog.CreateAsync(workspace, "catalog");
        ProjectGitSyncService sync = catalog.Sync(first);
        await sync.ImportAsync(first);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            catalog.Sync(differing, review: new ChooseRemoteReview()).ImportAsync(differing));
        Assert.Equal("Project", (await catalog.Snapshots.ReadAsync(new ProjectId(id))).Snapshot!.Project.Name);
        await sync.UnlinkAsync(new ProjectId(id));
        ProjectSyncLink linked = await sync.ImportAsync(identical);
        Assert.Equal(id, linked.ProjectId);
        await sync.UnlinkAsync(new ProjectId(id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sync.ImportAsync(differing));
        Assert.Equal("Project", (await catalog.Snapshots.ReadAsync(new ProjectId(id))).Snapshot!.Project.Name);
    }

    [Fact]
    public async Task UnsupportedVersionAndTombstoneAreRejectedBeforeBackup()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string checkout = workspace.CreateLocalCheckout("invalid", Snapshot(id, "Project"));
        await using TestCatalog catalog = await TestCatalog.CreateAsync(workspace, "catalog");
        ProjectGitSyncService sync = catalog.Sync(checkout);
        string manifestPath = Path.Combine(checkout, ".entitytracker", "manifest.json");
        string manifest = File.ReadAllText(manifestPath);
        Assert.Contains("\"formatVersion\":4", manifest);
        File.WriteAllText(manifestPath, manifest.Replace("\"formatVersion\":4", "\"formatVersion\":99"));
        workspace.Git(checkout, "add", ".entitytracker");
        workspace.Git(checkout, "commit", "-m", "Unsupported version");
        ProjectSnapshotFormatVersionException versionError =
            await Assert.ThrowsAsync<ProjectSnapshotFormatVersionException>(() => sync.ImportAsync(checkout));
        Assert.Equal(ProjectSnapshot.CurrentFormatVersion, versionError.ApplicationFormatVersion);
        Assert.Equal(99, versionError.ProjectFormatVersion);
        File.WriteAllText(manifestPath, manifest);
        File.WriteAllText(Path.Combine(checkout, ".entitytracker", "deleted-project.json"), "{}");
        workspace.Git(checkout, "add", ".entitytracker");
        workspace.Git(checkout, "commit", "-m", "Tombstone");
        await Assert.ThrowsAsync<InvalidDataException>(() => sync.ImportAsync(checkout));
        Assert.Null((await catalog.Snapshots.ReadAsync(new ProjectId(id))).Snapshot);
        Assert.False(Directory.Exists(catalog.BackupDirectory));
    }

    [Fact]
    public async Task BrokenSnapshotReferenceIsRejectedBeforeSqliteMutation()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        ProjectSnapshot snapshot = Snapshot(id, "Project");
        string checkout = workspace.CreateLocalCheckout("broken", snapshot);
        string entityPath = Directory.GetFiles(Path.Combine(checkout, ".entitytracker"), "*.json",
            SearchOption.AllDirectories).Single(path => path.EndsWith(
                snapshot.Trackers[0].Entities[0].Id.ToString("D") + ".json", StringComparison.Ordinal));
        Guid target = snapshot.Trackers[0].Entities[1].Id;
        string document = File.ReadAllText(entityPath);
        Assert.Contains(target.ToString("D"), document);
        File.WriteAllText(entityPath, document.Replace(target.ToString("D"), Guid.NewGuid().ToString("D")));
        workspace.Git(checkout, "add", ".entitytracker");
        workspace.Git(checkout, "commit", "-m", "Broken reference");
        await using TestCatalog catalog = await TestCatalog.CreateAsync(workspace, "catalog");
        await Assert.ThrowsAsync<InvalidDataException>(() => catalog.Sync(checkout).ImportAsync(checkout));
        Assert.Null((await catalog.Snapshots.ReadAsync(new ProjectId(id))).Snapshot);
        Assert.False(Directory.Exists(catalog.BackupDirectory));
    }

    [Fact]
    public async Task DetachedHeadAndChangedLinkedBranchAreRejected()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string checkout = workspace.CreateLocalCheckout("local", Snapshot(id, "Project"));
        await using TestCatalog catalog = await TestCatalog.CreateAsync(workspace, "catalog");
        ProjectGitSyncService sync = catalog.Sync(checkout);
        workspace.Git(checkout, "checkout", "--detach", "HEAD");
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => sync.ImportAsync(checkout));
        workspace.Git(checkout, "checkout", "main");
        await sync.ImportAsync(checkout);
        workspace.Git(checkout, "branch", "-m", "other");
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => sync.SyncNowAsync(new ProjectId(id)));
    }

    [Fact]
    public async Task ImportNameRaceRollsBackAndRetainsPreApplyBackup()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string checkout = workspace.CreateLocalCheckout("local", Snapshot(id, "Project"));
        await using TestCatalog catalog = await TestCatalog.CreateAsync(workspace, "catalog");
        ProjectGitSyncService sync = catalog.Sync(checkout, backup: new RacyBackup(
            catalog.Backup(), catalog.Snapshots, Snapshot(Guid.NewGuid(), "Project")));
        await Assert.ThrowsAsync<SqliteException>(() => sync.ImportAsync(checkout));
        Assert.Null((await catalog.Snapshots.ReadAsync(new ProjectId(id))).Snapshot);
        Assert.Single(Directory.GetFiles(catalog.BackupDirectory,
            "entity-tracker-pre-sync-*.db", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task MissingRemoteReportsCommandLineGitActionWithoutChangingSqlite()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string checkout = workspace.CreateRemoteCheckout("first", Snapshot(id, "Project"));
        await using TestCatalog catalog = await TestCatalog.CreateAsync(workspace, "catalog");
        ProjectGitSyncService sync = catalog.Sync(checkout);
        await sync.ImportAsync(checkout);
        string remote = Path.Combine(workspace.Root, "remote.git");
        string unavailable = Path.Combine(workspace.Root, "remote-unavailable.git");
        Directory.Move(remote, unavailable);
        try
        {
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => sync.SyncNowAsync(new ProjectId(id)));
            Assert.Contains("command-line Git", error.Message);
            Assert.Equal("Project", (await catalog.Snapshots.ReadAsync(new ProjectId(id))).Snapshot!.Project.Name);
        }
        finally { Directory.Move(unavailable, remote); }
    }

    [Fact]
    public async Task PushRaceFetchesAndMergesAdvancedUpstream()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string first = workspace.CreateRemoteCheckout("first", Snapshot(id, "Project"));
        string other = workspace.Clone("other");
        await using TestCatalog catalog = await TestCatalog.CreateAsync(workspace, "catalog");
        await catalog.Sync(first).ImportAsync(first);
        ProjectId projectId = new(id);
        ProjectSnapshotRead read = await catalog.Snapshots.ReadAsync(projectId);
        await catalog.Snapshots.ApplyAsync(read.Snapshot! with
        {
            Project = read.Snapshot.Project with { Name = "Local edit" }
        }, read.Revision);
        RaceTransport transport = new(() =>
        {
            workspace.Git(other, "commit", "--allow-empty", "-m", "Remote advanced");
            workspace.Git(other, "push", "origin", "main");
        });
        ProjectGitSyncService sync = catalog.Sync(first, transport);
        ProjectSyncLink result = await sync.SyncNowAsync(projectId);
        Assert.Equal("Current", result.SyncStatus);
        Assert.Equal("Local edit", (await catalog.Snapshots.ReadAsync(projectId)).Snapshot!.Project.Name);
        Assert.Equal(workspace.Git(first, "rev-parse", "HEAD"),
            workspace.Git(workspace.Root, "--git-dir", Path.Combine(workspace.Root, "remote.git"),
                "rev-parse", "refs/heads/main"));
        Assert.Equal(2, workspace.Git(first, "show", "-s", "--format=%P", "HEAD")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task LostPushAcknowledgementIsRevalidatedAsCurrent()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string checkout = workspace.CreateRemoteCheckout("first", Snapshot(id, "Project"));
        await using TestCatalog catalog = await TestCatalog.CreateAsync(workspace, "catalog");
        await catalog.Sync(checkout).ImportAsync(checkout);
        ProjectId projectId = new(id);
        ProjectSnapshotRead read = await catalog.Snapshots.ReadAsync(projectId);
        await catalog.Snapshots.ApplyAsync(read.Snapshot! with
        {
            Project = read.Snapshot.Project with { Name = "Pushed" }
        }, read.Revision);
        ProjectGitSyncService sync = catalog.Sync(checkout, new RaceTransport(() => { }, true));
        Assert.Equal("Current", (await sync.SyncNowAsync(projectId)).SyncStatus);
        Assert.Equal("Current", (await sync.GetLinkAsync(projectId))!.SyncStatus);
    }

    [Fact]
    public async Task RemoteCommitAfterPushFastForwardsWithoutDivergence()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string checkout = workspace.CreateRemoteCheckout("first", Snapshot(id, "Project"));
        string other = workspace.Clone("other");
        await using TestCatalog catalog = await TestCatalog.CreateAsync(workspace, "catalog");
        await catalog.Sync(checkout).ImportAsync(checkout);
        ProjectId projectId = new(id);
        ProjectSnapshotRead read = await catalog.Snapshots.ReadAsync(projectId);
        await catalog.Snapshots.ApplyAsync(read.Snapshot! with
        {
            Project = read.Snapshot.Project with { Name = "Pushed" }
        }, read.Revision);
        RaceTransport transport = new(() => { }, true, () =>
        {
            workspace.Git(other, "pull", "--ff-only");
            File.WriteAllText(Path.Combine(other, "README.md"), "After push\n");
            workspace.Git(other, "add", "README.md");
            workspace.Git(other, "commit", "-m", "Advance after push");
            workspace.Git(other, "push", "origin", "main");
        });
        ProjectGitSyncService sync = catalog.Sync(checkout, transport);
        Assert.Equal("Current", (await sync.SyncNowAsync(projectId)).SyncStatus);
        Assert.Equal(workspace.Git(checkout, "rev-parse", "HEAD"), workspace.Git(other, "rev-parse", "HEAD"));
        Assert.Equal("Pushed", (await catalog.Snapshots.ReadAsync(projectId)).Snapshot!.Project.Name);
    }

    [Fact]
    public async Task ReadmeOnlyUpstreamCommitFastForwardsBeforeLocalProjectPush()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string first = workspace.CreateRemoteCheckout("first", Snapshot(id, "Project"));
        string other = workspace.Clone("other");
        await using TestCatalog catalog = await TestCatalog.CreateAsync(workspace, "catalog");
        ProjectGitSyncService sync = catalog.Sync(first);
        await sync.ImportAsync(first);
        int backupsBeforeSync = Directory.GetFiles(catalog.BackupDirectory,
            "entity-tracker-pre-sync-*.db", SearchOption.AllDirectories).Length;
        ProjectId projectId = new(id);
        ProjectSnapshotRead read = await catalog.Snapshots.ReadAsync(projectId);
        await catalog.Snapshots.ApplyAsync(read.Snapshot! with
        {
            Project = read.Snapshot.Project with { Name = "Local edit" }
        }, read.Revision);
        File.WriteAllText(Path.Combine(other, "README.md"), "Edited by another member\n");
        workspace.Git(other, "add", "README.md");
        workspace.Git(other, "commit", "-m", "Update README");
        workspace.Git(other, "push", "origin", "main");
        Assert.Equal("Current", (await sync.SyncNowAsync(projectId)).SyncStatus);
        Assert.Equal("Edited by another member\n",
            File.ReadAllText(Path.Combine(first, "README.md")).Replace("\r\n", "\n"));
        Assert.Equal("Local edit", (await catalog.Snapshots.ReadAsync(projectId)).Snapshot!.Project.Name);
        Assert.Equal(backupsBeforeSync, Directory.GetFiles(catalog.BackupDirectory,
            "entity-tracker-pre-sync-*.db", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task InboundApplyRetriesAfterBackupFailureWithoutLosingLocalData()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string first = workspace.CreateRemoteCheckout("first", Snapshot(id, "Project"));
        string second = workspace.Clone("second");
        await using TestCatalog publisher = await TestCatalog.CreateAsync(workspace, "publisher");
        await using TestCatalog receiver = await TestCatalog.CreateAsync(workspace, "receiver");
        ProjectGitSyncService firstSync = publisher.Sync(first);
        await firstSync.ImportAsync(first);
        await receiver.Sync(second).ImportAsync(second);
        ProjectId projectId = new(id);
        ProjectSnapshotRead read = await publisher.Snapshots.ReadAsync(projectId);
        await publisher.Snapshots.ApplyAsync(read.Snapshot! with
        {
            Project = read.Snapshot.Project with { Name = "Remote edit" }
        }, read.Revision);
        await firstSync.SyncNowAsync(projectId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            receiver.Sync(second, backup: new RejectBackup()).SyncNowAsync(projectId));
        Assert.Equal("Project", (await receiver.Snapshots.ReadAsync(projectId)).Snapshot!.Project.Name);
        Assert.Equal(workspace.Git(first, "rev-parse", "HEAD"), workspace.Git(second, "rev-parse", "HEAD"));
        Assert.Equal("Current", (await receiver.Sync(second).SyncNowAsync(projectId)).SyncStatus);
        Assert.Equal("Remote edit", (await receiver.Snapshots.ReadAsync(projectId)).Snapshot!.Project.Name);
    }

    [Fact]
    public async Task CommitIsRecoveredWhenPendingLinkWriteFails()
    {
        using GitWorkspace workspace = new();
        Guid id = Guid.NewGuid();
        string checkout = workspace.CreateRemoteCheckout("first", Snapshot(id, "Project"));
        await using TestCatalog catalog = await TestCatalog.CreateAsync(workspace, "catalog");
        await catalog.Sync(checkout).ImportAsync(checkout);
        ProjectId projectId = new(id);
        ProjectSnapshotRead read = await catalog.Snapshots.ReadAsync(projectId);
        await catalog.Snapshots.ApplyAsync(read.Snapshot! with
        {
            Project = read.Snapshot.Project with { Name = "Local edit" }
        }, read.Revision);
        await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.Sync(checkout,
            linkStore: new FailOncePendingLinkStore(catalog.Links)).SyncNowAsync(projectId));
        Assert.NotEqual(workspace.Git(checkout, "rev-parse", "HEAD"),
            workspace.Git(workspace.Root, "--git-dir", Path.Combine(workspace.Root, "remote.git"),
                "rev-parse", "refs/heads/main"));
        Assert.Equal("Current", (await catalog.Sync(checkout).SyncNowAsync(projectId)).SyncStatus);
    }

    private static ProjectSnapshot Snapshot(Guid id, string name)
    {
        DateTimeOffset time = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset later = time.AddDays(1);
        Guid trackerId = Derive(id, 1);
        Guid recycledTrackerId = Derive(id, 2);
        Guid entityId = Derive(id, 3);
        Guid dependencyId = Derive(id, 4);
        Guid copiedEntityId = Derive(id, 5);
        Guid firstEvent = Derive(id, 6);
        SnapshotEntity entity = new(entityId, trackerId, "Orders", "InProgress", "Keep notes",
            "Active", "ManualAndImported", 2, "", "Core", time, later, later,
            [new SnapshotDependency(entityId, dependencyId, "Mandatory", time, later)],
            [new SnapshotUnresolvedDependency(entityId, "External", "Optional", time, later)],
            [new SnapshotOverride(entityId, "External", "Suppress", time, later)],
            [new SnapshotResponsibilityPeriod(Derive(id, 12), entityId, Derive(id, 14), later, null)]);
        SnapshotEntity dependency = new(dependencyId, trackerId, "Customers", "Reconciled", "Archived notes",
            "Archived", "Imported", 4, "", "Legacy", time, later, later, [], [], [],
            [new SnapshotResponsibilityPeriod(Derive(id, 13), dependencyId, Derive(id, 15), later, null)]);
        SnapshotEntity copied = new(copiedEntityId, recycledTrackerId, "Copied entity", "NotStarted", "",
            "Active", "Copied", null, "", "", time, later, later, [], [], []);
        SnapshotTracker tracker = new(trackerId, id, "Delivery", "Active", time, later, null, null,
            [entity, dependency],
            [new SnapshotStatusEvent(firstEvent, entityId, null, null, "NotStarted", "Created", time, 0),
             new SnapshotStatusEvent(Derive(id, 7), dependencyId, null, null, "Reconciled", "Baseline", time, 1),
             new SnapshotStatusEvent(Derive(id, 8), entityId, firstEvent, "NotStarted", "InProgress", "Transition", later, 2)],
            [new SnapshotProgress(Derive(id, 9), time, 1, 0, 0, 0, 0, 1, 0),
             new SnapshotProgress(Derive(id, 10), later, 0, 0, 1, 0, 0, 1, 1)],
            new SnapshotImportSummary(later, "schema.csv", "Partial", 1, 2, 3, 4, 5));
        SnapshotTracker recycled = new(recycledTrackerId, id, "Archive", "Recycled", time, later, later,
            trackerId, [copied], [], [new SnapshotProgress(Derive(id, 11), later, 1, 0, 0, 0, 0, 0, 0)], null);
        return new ProjectSnapshot(ProjectSnapshot.CurrentFormatVersion,
            new SnapshotProject(id, name, "Active", time, later, null),
            [tracker, recycled],
            [new SnapshotDeveloper(Derive(id, 14), id, "Ada", "", false),
             new SnapshotDeveloper(Derive(id, 15), id, "Grace", "", false)]);
    }

    private static Guid Derive(Guid id, byte salt)
    {
        byte[] bytes = id.ToByteArray();
        bytes[0] ^= salt;
        return new Guid(bytes);
    }

    private sealed class TestCatalog : IAsyncDisposable
    {
        private readonly GitWorkspace _workspace;
        private readonly SqliteDatabase _database;
        private readonly IProjectSyncLinkStore _links;
        public SqliteProjectSnapshotStore Snapshots { get; }
        public SqliteProjectRepository Projects { get; }
        public IProjectSyncLinkStore Links => _links;
        public string BackupDirectory { get; }

        private TestCatalog(GitWorkspace workspace, string name)
        {
            _workspace = workspace;
            _database = new SqliteDatabase(Path.Combine(workspace.Root, name + ".db"));
            _links = new JsonProjectSyncLinkStore(Path.Combine(workspace.Root, name + "-links.json"));
            Snapshots = new SqliteProjectSnapshotStore(_database);
            Projects = new SqliteProjectRepository(_database);
            BackupDirectory = Path.Combine(workspace.Root, name + "-backups");
        }

        public static async Task<TestCatalog> CreateAsync(GitWorkspace workspace, string name)
        {
            TestCatalog catalog = new(workspace, name);
            await catalog._database.InitializeAsync();
            return catalog;
        }

        public SqliteBackupService Backup() => new(_database, BackupDirectory);

        public ProjectGitSyncService Sync(string checkout, ILocalGitTransport? transport = null,
            IProjectSyncBackup? backup = null, IProjectSyncLinkStore? linkStore = null,
            IProjectMergeReview? review = null, IOutboundDeletionApproval? approval = null,
            IProjectUnsavedEditsGate? editGate = null) => new(
            linkStore ?? _links, transport ?? new SystemGitTransport(), Snapshots,
            new ProjectSnapshotJsonCodec(), approval ?? new ApproveDeletions(), Projects,
            backup ?? Backup(), review, editGate);

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ApproveDeletions : IOutboundDeletionApproval
    {
        public Task<bool> ApproveAsync(IReadOnlyList<string> deletedObjects, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class RecordingApproval : IOutboundDeletionApproval
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public Task<bool> ApproveAsync(IReadOnlyList<string> deletedObjects,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(deletedObjects.ToArray());
            return Task.FromResult(true);
        }
    }

    private sealed class BlockingEditGate : IProjectUnsavedEditsGate
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;
        public Task WaitUntilReadyAsync(ProjectId projectId,
            CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult();
            return _release.Task.WaitAsync(cancellationToken);
        }
        public void Release() => _release.TrySetResult();
    }

    private sealed class ChooseRemoteReview : IProjectMergeReview
    {
        public Task<IReadOnlyDictionary<string, MergeSide>?> ReviewAsync(
            IReadOnlyList<ProjectMergeConflict> conflicts, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, MergeSide>?>(
                conflicts.ToDictionary(c => c.Path, _ => MergeSide.Remote));
    }

    private sealed class ChooseLocalReview : IProjectMergeReview
    {
        public Task<IReadOnlyDictionary<string, MergeSide>?> ReviewAsync(
            IReadOnlyList<ProjectMergeConflict> conflicts, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, MergeSide>?>(
                conflicts.ToDictionary(c => c.Path, _ => MergeSide.Local));
    }

    private sealed class MutatingReview(Func<Task> mutate) : IProjectMergeReview
    {
        public async Task<IReadOnlyDictionary<string, MergeSide>?> ReviewAsync(
            IReadOnlyList<ProjectMergeConflict> conflicts, CancellationToken cancellationToken = default)
        {
            await mutate();
            return conflicts.ToDictionary(c => c.Path, _ => MergeSide.Remote);
        }
    }

    private sealed class MutatingApproval(Func<Task> mutate) : IOutboundDeletionApproval
    {
        public async Task<bool> ApproveAsync(IReadOnlyList<string> deletedObjects,
            CancellationToken cancellationToken = default)
        {
            Assert.Contains(deletedObjects, name => name.StartsWith("Project ", StringComparison.Ordinal));
            await mutate();
            return true;
        }
    }

    private sealed class RacyBackup(SqliteBackupService backup, SqliteProjectSnapshotStore snapshots,
        ProjectSnapshot competing) : IProjectSyncBackup
    {
        public async Task<string> CreatePreApplyBackupAsync(CancellationToken cancellationToken = default)
        {
            string path = await backup.CreatePreApplyBackupAsync(cancellationToken);
            await snapshots.ApplyAsync(competing, 0, cancellationToken);
            return path;
        }
    }

    private sealed class RevisionChangingBackup(SqliteBackupService backup,
        SqliteProjectSnapshotStore snapshots, ProjectId projectId) : IProjectSyncBackup
    {
        public async Task<string> CreatePreApplyBackupAsync(
            CancellationToken cancellationToken = default)
        {
            string path = await backup.CreatePreApplyBackupAsync(cancellationToken);
            ProjectSnapshotRead current = await snapshots.ReadAsync(projectId, cancellationToken);
            await snapshots.ApplyAsync(current.Snapshot! with
            {
                Project = current.Snapshot.Project with { Name = "Competing edit" }
            }, current.Revision, cancellationToken);
            return path;
        }
    }

    private sealed class RejectBackup : IProjectSyncBackup
    {
        public Task<string> CreatePreApplyBackupAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Backup unavailable.");
    }

    private sealed class FailOncePendingLinkStore(IProjectSyncLinkStore inner) : IProjectSyncLinkStore
    {
        private bool _failed;
        public Task<IReadOnlyList<ProjectSyncLink>> ReadAllAsync(CancellationToken cancellationToken = default) =>
            inner.ReadAllAsync(cancellationToken);
        public Task SaveAsync(ProjectSyncLink link, CancellationToken cancellationToken = default)
        {
            if (!_failed && link.SyncStatus == "PendingPush")
            {
                _failed = true;
                throw new InvalidOperationException("Link store unavailable.");
            }
            return inner.SaveAsync(link, cancellationToken);
        }
        public Task RemoveAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            inner.RemoveAsync(projectId, cancellationToken);
    }

    private sealed class RaceTransport(Action beforeFirstPush, bool pretendAckLost = false,
        Action? afterSuccessfulPush = null) : ILocalGitTransport
    {
        private readonly SystemGitTransport _inner = new();
        private bool _raced;
        public Task<IAsyncDisposable> LockAsync(string path, CancellationToken cancellationToken = default) =>
            _inner.LockAsync(path, cancellationToken);
        public Task<GitWorkingTreeState> InspectAsync(string path, CancellationToken cancellationToken = default) =>
            _inner.InspectAsync(path, cancellationToken);
        public Task<string> CommitSnapshotAsync(string path, IReadOnlyDictionary<string, byte[]> oldFiles,
            ProjectSnapshotPackage package, CancellationToken cancellationToken = default) =>
            _inner.CommitSnapshotAsync(path, oldFiles, package, cancellationToken);
        public Task<GitRemoteState> FetchAsync(string path, CancellationToken cancellationToken = default) =>
            _inner.FetchAsync(path, cancellationToken);
        public Task<bool> IsAncestorAsync(string path, string ancestor, string descendant,
            CancellationToken cancellationToken = default) =>
            _inner.IsAncestorAsync(path, ancestor, descendant, cancellationToken);
        public Task FastForwardAsync(string path, string expectedHead, string remoteHead,
            CancellationToken cancellationToken = default) =>
            _inner.FastForwardAsync(path, expectedHead, remoteHead, cancellationToken);
        public Task<string?> MergeBaseAsync(string path, string localHead, string remoteHead,
            CancellationToken cancellationToken = default) =>
            _inner.MergeBaseAsync(path, localHead, remoteHead, cancellationToken);
        public Task<IReadOnlyDictionary<string, byte[]>> ReadSnapshotAtCommitAsync(
            string path, string commit, CancellationToken cancellationToken = default) =>
            _inner.ReadSnapshotAtCommitAsync(path, commit, cancellationToken);
        public Task ValidateMergeScopeAsync(string path, string localHead, string remoteHead,
            CancellationToken cancellationToken = default) =>
            _inner.ValidateMergeScopeAsync(path, localHead, remoteHead, cancellationToken);
        public Task<string> CommitMergeSnapshotAsync(string path, string localHead,
            string remoteHead, IReadOnlyDictionary<string, byte[]> oldFiles,
            ProjectSnapshotPackage package, CancellationToken cancellationToken = default) =>
            _inner.CommitMergeSnapshotAsync(path, localHead, remoteHead, oldFiles,
                package, cancellationToken);
        public async Task PushAsync(string path, CancellationToken cancellationToken = default)
        {
            if (!_raced)
            {
                _raced = true;
                beforeFirstPush();
                if (pretendAckLost)
                {
                    await _inner.PushAsync(path, cancellationToken);
                    afterSuccessfulPush?.Invoke();
                    throw new InvalidOperationException("Push acknowledgement was lost.");
                }
            }
            await _inner.PushAsync(path, cancellationToken);
        }
    }

    private sealed class FailFastForwardTransport : ILocalGitTransport
    {
        private readonly SystemGitTransport _inner = new();
        public Task<IAsyncDisposable> LockAsync(string path, CancellationToken cancellationToken = default) =>
            _inner.LockAsync(path, cancellationToken);
        public Task<GitWorkingTreeState> InspectAsync(string path, CancellationToken cancellationToken = default) =>
            _inner.InspectAsync(path, cancellationToken);
        public Task<string> CommitSnapshotAsync(string path, IReadOnlyDictionary<string, byte[]> oldFiles,
            ProjectSnapshotPackage package, CancellationToken cancellationToken = default) =>
            _inner.CommitSnapshotAsync(path, oldFiles, package, cancellationToken);
        public Task<GitRemoteState> FetchAsync(string path, CancellationToken cancellationToken = default) =>
            _inner.FetchAsync(path, cancellationToken);
        public Task<bool> IsAncestorAsync(string path, string ancestor, string descendant,
            CancellationToken cancellationToken = default) =>
            _inner.IsAncestorAsync(path, ancestor, descendant, cancellationToken);
        public Task FastForwardAsync(string path, string expectedHead, string remoteHead,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated checkout update failure.");
    }

    private sealed class RecordingProgress(List<ProjectSyncPhase> phases) : IProgress<ProjectSyncPhase>
    {
        public void Report(ProjectSyncPhase value) => phases.Add(value);
    }

    private sealed class ImmediateProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class GitWorkspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "entitytracker-gs03-" + Guid.NewGuid().ToString("N"));
        private string Bare => Path.Combine(Root, "remote.git");

        public GitWorkspace() => Directory.CreateDirectory(Root);

        public string CreateRemoteCheckout(string name, ProjectSnapshot snapshot)
        {
            Git(Root, "init", "--bare", "-b", "main", Bare);
            string checkout = Clone(name);
            Seed(checkout, snapshot);
            Git(checkout, "push", "-u", "origin", "main");
            return checkout;
        }

        public string Clone(string name)
        {
            string checkout = Path.Combine(Root, name);
            Git(Root, "clone", Bare, checkout);
            Git(checkout, "config", "user.name", "GS03 Test");
            Git(checkout, "config", "user.email", "test@example.invalid");
            return checkout;
        }

        public string CreateLocalCheckout(string name, ProjectSnapshot snapshot)
        {
            string checkout = Path.Combine(Root, name);
            Directory.CreateDirectory(checkout);
            Git(checkout, "init", "-b", "main");
            Git(checkout, "config", "user.name", "GS03 Test");
            Git(checkout, "config", "user.email", "test@example.invalid");
            Seed(checkout, snapshot);
            return checkout;
        }

        private void Seed(string checkout, ProjectSnapshot snapshot)
        {
            File.WriteAllText(Path.Combine(checkout, "README.md"), "User managed repository\n");
            foreach ((string relative, byte[] bytes) in new ProjectSnapshotJsonCodec().Encode(snapshot).Files)
            {
                string destination = Path.Combine(checkout, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, bytes);
            }
            Git(checkout, "add", "README.md", ".entitytracker");
            Git(checkout, "commit", "-m", "Initial snapshot");
        }

        public string Git(string workingDirectory, params string[] args)
        {
            ProcessStartInfo start = new("git") { WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true };
            foreach (string arg in args) start.ArgumentList.Add(arg);
            using Process process = Process.Start(start)!;
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException(error);
            return output.TrimEnd('\r', '\n');
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (!Directory.Exists(Root)) return;
            foreach (string path in Directory.EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(Root, true);
        }
    }
}

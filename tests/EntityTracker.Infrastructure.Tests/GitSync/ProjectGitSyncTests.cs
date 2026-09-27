using System.Diagnostics;
using EntityTracker.Application.GitSync;
using EntityTracker.Application.Snapshots;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.GitSync;
using EntityTracker.Infrastructure.Snapshots;
using EntityTracker.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Tests.GitSync;

public sealed class ProjectGitSyncTests
{
    [Fact]
    public async Task LinkSyncIdempotenceAndUnlinkUseExistingLocalRepository()
    {
        using TestRepository repo = new();
        repo.Init();
        repo.Write("README.md", "User owned\n");
        repo.Git("add", "README.md");
        repo.Git("commit", "-m", "Start");
        Guid id = Guid.NewGuid();
        ProjectGitSyncService service = CreateService(repo, new TestSnapshotStore(Snapshot(id)));
        ProjectId projectId = new(id);
        ProjectSyncLink link = await service.LinkAsync(projectId, repo.Root);
        Assert.Equal("main", link.Branch);
        Assert.Null(link.UpstreamIdentity);
        Assert.Equal("Pending", link.SyncStatus);
        string before = repo.Git("rev-parse", "HEAD");
        ProjectSyncLink synced = await service.SyncNowAsync(projectId);
        Assert.NotEqual(before, synced.LastCommonCommit);
        Assert.Equal("Current", synced.SyncStatus);
        Assert.Contains(".entitytracker/manifest.json", repo.Git("show", "--pretty=format:", "--name-only", "HEAD"));
        Assert.DoesNotContain("README.md", repo.Git("show", "--pretty=format:", "--name-only", "HEAD"));
        Assert.Equal("User owned\n", File.ReadAllText(Path.Combine(repo.Root, "README.md")));
        string head = repo.Git("rev-parse", "HEAD");
        Assert.Equal("Already current", (await service.SyncNowAsync(projectId)).LastResult);
        Assert.Equal(head, repo.Git("rev-parse", "HEAD"));
        await service.UnlinkAsync(projectId);
        Assert.Null(await service.GetLinkAsync(projectId));
        Assert.True(File.Exists(Path.Combine(repo.Root, ".entitytracker", "manifest.json")));
    }

    [Fact]
    public async Task DirtyIdentityUnsupportedDuplicateAndBranchChangeAreRejected()
    {
        using TestRepository repo = new();
        repo.Init();
        Guid id = Guid.NewGuid();
        TestSnapshotStore snapshots = new(Snapshot(id));
        ProjectGitSyncService service = CreateService(repo, snapshots);
        repo.Write("untracked.txt", "dirty");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LinkAsync(new ProjectId(id), repo.Root));
        File.Delete(Path.Combine(repo.Root, "untracked.txt"));
        repo.Git("config", "user.email", "");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LinkAsync(new ProjectId(id), repo.Root));
        repo.Git("config", "user.email", "test@example.invalid");
        repo.Write("unsupported.txt", "no");
        repo.Git("add", "unsupported.txt");
        repo.Git("commit", "-m", "Unsupported");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LinkAsync(new ProjectId(id), repo.Root));
        repo.Git("rm", "unsupported.txt");
        repo.Git("commit", "-m", "Remove unsupported");
        await service.LinkAsync(new ProjectId(id), repo.Root);
        snapshots.Snapshot = Snapshot(Guid.NewGuid());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LinkAsync(new ProjectId(snapshots.Snapshot.Project.Id), repo.Root));
        repo.Git("branch", "-m", "renamed");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SyncNowAsync(new ProjectId(id)));
    }

    [Fact]
    public async Task DifferingSnapshotCannotBeLinkedAndNestedFolderIsRejected()
    {
        using TestRepository repo = new();
        repo.Init();
        Guid id = Guid.NewGuid();
        ProjectSnapshotPackage package = new ProjectSnapshotJsonCodec().Encode(Snapshot(Guid.NewGuid()));
        foreach ((string relative, byte[] bytes) in package.Files)
        {
            string full = Path.Combine(repo.Root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
        }
        repo.Git("add", ".entitytracker");
        repo.Git("commit", "-m", "Other Project");
        string head = repo.Git("rev-parse", "HEAD");
        ProjectGitSyncService service = CreateService(repo, new TestSnapshotStore(Snapshot(id)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LinkAsync(new ProjectId(id), repo.Root));
        Assert.Equal(head, repo.Git("rev-parse", "HEAD"));
        Directory.CreateDirectory(Path.Combine(repo.Root, "nested"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SystemGitTransport().InspectAsync(Path.Combine(repo.Root, "nested")));
    }

    [Fact]
    public async Task MatchingSnapshotLinksWithoutCommitAndInvalidSnapshotIsRejected()
    {
        using TestRepository repo = new();
        repo.Init();
        Guid id = Guid.NewGuid();
        ProjectSnapshot snapshot = Snapshot(id);
        ProjectSnapshotPackage package = new ProjectSnapshotJsonCodec().Encode(snapshot);
        foreach ((string relative, byte[] bytes) in package.Files)
        {
            string full = Path.Combine(repo.Root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
        }
        repo.Git("add", ".entitytracker");
        repo.Git("commit", "-m", "Existing snapshot");
        string head = repo.Git("rev-parse", "HEAD");
        ProjectGitSyncService service = CreateService(repo, new TestSnapshotStore(snapshot));
        Assert.Equal("Current", (await service.LinkAsync(new ProjectId(id), repo.Root)).SyncStatus);
        Assert.Equal("Already current", (await service.SyncNowAsync(new ProjectId(id))).LastResult);
        Assert.Equal(head, repo.Git("rev-parse", "HEAD"));
        await service.UnlinkAsync(new ProjectId(id));

        repo.Write(Path.Combine(".entitytracker", "manifest.json"), "{invalid");
        repo.Git("add", ".entitytracker");
        repo.Git("commit", "-m", "Invalid snapshot");
        await Assert.ThrowsAnyAsync<Exception>(() => service.LinkAsync(new ProjectId(id), repo.Root));
        Assert.Null(await service.GetLinkAsync(new ProjectId(id)));
    }

    [Fact]
    public async Task UnapprovedTrackerDeletionLeavesRepositoryUntouched()
    {
        using TestRepository repo = new();
        repo.Init();
        Guid id = Guid.NewGuid();
        ProjectSnapshot initial = Snapshot(id);
        SnapshotTracker tracker = new(Guid.NewGuid(), id, "Tracker", "Active",
            initial.Project.CreatedAtUtc, initial.Project.UpdatedAtUtc, null, null, [], [], [], null);
        TestSnapshotStore snapshots = new(initial with { Trackers = [tracker] });
        ProjectGitSyncService service = CreateService(repo, snapshots);
        await service.LinkAsync(new ProjectId(id), repo.Root);
        await service.SyncNowAsync(new ProjectId(id));
        string head = repo.Git("rev-parse", "HEAD");
        snapshots.Snapshot = initial;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SyncNowAsync(new ProjectId(id)));
        Assert.Equal(head, repo.Git("rev-parse", "HEAD"));
        Assert.Equal("", repo.Git("status", "--porcelain"));
    }

    [Fact]
    public async Task StalledGitInspectionDoesNotHoldSqliteRead()
    {
        using TestRepository repo = new();
        repo.Init();
        string databasePath = Path.Combine(repo.Root + "-database.db");
        try
        {
            SqliteDatabase database = new(databasePath);
            await database.InitializeAsync();
            SqliteProjectSnapshotStore snapshots = new(database);
            Guid id = Guid.NewGuid();
            await snapshots.ApplyAsync(Snapshot(id), 0);
            StalledTransport transport = new(repo.Root);
            ProjectGitSyncService service = new(new JsonProjectSyncLinkStore(repo.Root + "-links.json"),
                transport, snapshots, new ProjectSnapshotJsonCodec(), new RejectDeletions());
            Task<ProjectSyncLink> linking = service.LinkAsync(new ProjectId(id), repo.Root);
            await transport.Entered.WaitAsync(TimeSpan.FromSeconds(3));
            ProjectSnapshotRead read = await snapshots.ReadAsync(new ProjectId(id)).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.NotNull(read.Snapshot);
            transport.Release();
            await linking;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string suffix in new[] { "", "-wal", "-shm" })
                if (File.Exists(databasePath + suffix)) File.Delete(databasePath + suffix);
        }
    }

    private static ProjectGitSyncService CreateService(TestRepository repo, TestSnapshotStore snapshots) => new(
        new JsonProjectSyncLinkStore(repo.Root + "-links.json"), new SystemGitTransport(), snapshots,
        new ProjectSnapshotJsonCodec(), new RejectDeletions());

    private static ProjectSnapshot Snapshot(Guid id) => new(1,
        new SnapshotProject(id, "Project", "Active", new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), null), []);

    private sealed class TestSnapshotStore(ProjectSnapshot snapshot) : IProjectSnapshotStore
    {
        public ProjectSnapshot Snapshot { get; set; } = snapshot;
        public Task<ProjectSnapshotRead> ReadAsync(ProjectId projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectSnapshotRead(Snapshot, 1));
        public Task<long> ApplyAsync(ProjectSnapshot snapshot, long expectedRevision, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RejectDeletions : IOutboundDeletionApproval
    {
        public Task<bool> ApproveAsync(IReadOnlyList<string> deletedObjects, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class StalledTransport(string root) : ILocalGitTransport
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;
        public void Release() => _release.TrySetResult();
        public Task<IAsyncDisposable> LockAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult<IAsyncDisposable>(new NoopLock());
        public async Task<GitWorkingTreeState> InspectAsync(string path, CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return new GitWorkingTreeState(root, "main", null, null, new Dictionary<string, byte[]>());
        }
        public Task<string> CommitSnapshotAsync(string path, IReadOnlyDictionary<string, byte[]> oldFiles,
            ProjectSnapshotPackage package, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        private sealed class NoopLock : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class TestRepository : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "entitytracker-gs02-" + Guid.NewGuid().ToString("N"));
        public void Init()
        {
            Directory.CreateDirectory(Root);
            Git("init", "-b", "main");
            Git("config", "user.name", "GS02 Test");
            Git("config", "user.email", "test@example.invalid");
        }
        public void Write(string relative, string value) => File.WriteAllText(Path.Combine(Root, relative), value);
        public string Git(params string[] args)
        {
            ProcessStartInfo start = new("git") { WorkingDirectory = Root, RedirectStandardOutput = true,
                RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
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
            if (Directory.Exists(Root))
            {
                foreach (string path in Directory.EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(path, FileAttributes.Normal);
                Directory.Delete(Root, true);
            }
            if (File.Exists(Root + "-links.json")) File.Delete(Root + "-links.json");
        }
    }
}

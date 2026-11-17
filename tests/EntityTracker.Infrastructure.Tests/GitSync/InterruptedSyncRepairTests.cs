using System.Diagnostics;
using EntityTracker.Application.GitSync;
using EntityTracker.Application.Snapshots;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.GitSync;
using EntityTracker.Infrastructure.Snapshots;

namespace EntityTracker.Infrastructure.Tests.GitSync;

/// <summary>
/// A sync stopped while it writes the snapshot (the laptop closed, the app was shut down, a time limit
/// passed) leaves the checkout with changed, staged or new snapshot files and sometimes a Git lock file.
/// The next sync repairs that and goes on, because the snapshot files are only an export of the
/// database (#219). Each test leaves the checkout the way such a sync does.
/// </summary>
public sealed class InterruptedSyncRepairTests
{
    [Fact]
    public async Task ChangedSnapshotFilesThatWereNotStagedAreRepaired()
    {
        using Checkout repo = await Checkout.CreateSyncedAsync();
        repo.Snapshots.Change(Snapshot(repo.ProjectId, "Second"));
        repo.WriteSnapshotFile("project.json", "{ \"half-written");

        await AssertRepairedAndPushedAsync(repo, "Second", "project.json");
    }

    [Fact]
    public async Task StagedSnapshotFilesAreRepaired()
    {
        using Checkout repo = await Checkout.CreateSyncedAsync();
        repo.Snapshots.Change(Snapshot(repo.ProjectId, "Second"));
        repo.WriteSnapshotFile("project.json", "{ \"half-written");
        repo.Git("add", "-A", "--", ".entitytracker");

        await AssertRepairedAndPushedAsync(repo, "Second", "project.json");
    }

    [Fact]
    public async Task StagedSnapshotFilesAndAnOldIndexLockAreRepaired()
    {
        using Checkout repo = await Checkout.CreateSyncedAsync();
        repo.Snapshots.Change(Snapshot(repo.ProjectId, "Second"));
        repo.WriteSnapshotFile("project.json", "{ \"half-written");
        repo.Git("add", "-A", "--", ".entitytracker");
        string indexLock = repo.LeaveLock(".git/index.lock", TimeSpan.FromHours(1));

        await AssertRepairedAndPushedAsync(repo, "Second", "project.json");
        Assert.False(File.Exists(indexLock));
    }

    [Fact]
    public async Task AnOldBranchLockIsRemoved()
    {
        using Checkout repo = await Checkout.CreateSyncedAsync();
        repo.Snapshots.Change(Snapshot(repo.ProjectId, "Second"));
        string branchLock = repo.LeaveLock(".git/refs/heads/main.lock", TimeSpan.FromHours(1));

        ProjectSyncLink result = await repo.Service.SyncNowAsync(repo.ProjectId);

        Assert.Equal("Current", result.SyncStatus);
        Assert.False(File.Exists(branchLock));
        Assert.Contains("repaired", result.LastResult, StringComparison.Ordinal);
        Assert.Contains("Second", repo.Git("show", "HEAD:.entitytracker/project.json"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NewAndDeletedSnapshotFilesLeftStagedAreRepaired()
    {
        // What a merge commit stopped before its update-ref leaves: the merged files staged, HEAD unchanged.
        using Checkout repo = await Checkout.CreateSyncedAsync();
        repo.Snapshots.Change(Snapshot(repo.ProjectId, "Second"));
        repo.WriteSnapshotFile("developers/" + Guid.NewGuid().ToString("D") + ".json", "{}");
        repo.WriteSnapshotFile("project.json", "{ \"merged");
        File.Delete(Path.Combine(repo.Root, ".entitytracker", "manifest.json"));
        repo.Git("add", "-A", "--", ".entitytracker");
        repo.WriteSnapshotFile("trackers/untracked.json", "{}");

        await AssertRepairedAndPushedAsync(repo, "Second", "project.json");
        Assert.False(Directory.Exists(Path.Combine(repo.Root, ".entitytracker", "developers")));
        Assert.False(File.Exists(Path.Combine(repo.Root, ".entitytracker", "trackers", "untracked.json")));
        Assert.True(File.Exists(Path.Combine(repo.Root, ".entitytracker", "manifest.json")));
    }

    [Fact]
    public async Task ARecentIndexLockIsLeftAloneAndReported()
    {
        using Checkout repo = await Checkout.CreateSyncedAsync();
        repo.Snapshots.Change(Snapshot(repo.ProjectId, "Second"));
        string indexLock = repo.LeaveLock(".git/index.lock", TimeSpan.Zero);

        ProjectSyncRepositoryBusyException error = await Assert.ThrowsAsync<ProjectSyncRepositoryBusyException>(() =>
            repo.Service.SyncNowAsync(repo.ProjectId));

        Assert.Contains("Another Git program is using this repository", error.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(indexLock));
    }

    [Fact]
    public async Task AChangedFileOutsideTheSnapshotStillStopsTheSyncAndNothingIsTouched()
    {
        using Checkout repo = await Checkout.CreateSyncedAsync();
        repo.Snapshots.Change(Snapshot(repo.ProjectId, "Second"));
        repo.WriteSnapshotFile("project.json", "{ \"half-written");
        File.WriteAllText(Path.Combine(repo.Root, "README.md"), "Edited by hand\n");

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.Service.SyncNowAsync(repo.ProjectId));

        Assert.Contains("working tree must be clean", error.Message, StringComparison.Ordinal);
        Assert.Equal("{ \"half-written", repo.ReadSnapshotFile("project.json"));
        Assert.False(Directory.Exists(repo.RecoveryDirectory));
    }

    [Theory]
    [InlineData("merge", "A Git merge is in progress")]
    [InlineData("rebase", "A Git rebase is in progress")]
    public async Task AMergeOrRebaseInProgressStillStopsTheSyncAndNothingIsTouched(string kind, string message)
    {
        using Checkout repo = await Checkout.CreateSyncedAsync();
        repo.Snapshots.Change(Snapshot(repo.ProjectId, "Second"));
        repo.WriteSnapshotFile("project.json", "{ \"half-written");
        if (kind == "merge") File.WriteAllText(Path.Combine(repo.Root, ".git", "MERGE_HEAD"), repo.Git("rev-parse", "HEAD") + "\n");
        else Directory.CreateDirectory(Path.Combine(repo.Root, ".git", "rebase-merge"));

        ProjectSyncConfigurationException error = await Assert.ThrowsAsync<ProjectSyncConfigurationException>(() =>
            repo.Service.SyncNowAsync(repo.ProjectId));

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
        Assert.Equal("{ \"half-written", repo.ReadSnapshotFile("project.json"));
        Assert.False(Directory.Exists(repo.RecoveryDirectory));
    }

    [Fact]
    public async Task ASyncWritesOnlyTheSnapshotFilesThatChanged()
    {
        using Checkout repo = await Checkout.CreateSyncedAsync();
        string manifest = Path.Combine(repo.Root, ".entitytracker", "manifest.json");
        DateTime old = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(manifest, old);
        repo.Snapshots.Change(Snapshot(repo.ProjectId, "Second"));

        ProjectSyncLink result = await repo.Service.SyncNowAsync(repo.ProjectId);

        Assert.Equal("Current", result.SyncStatus);
        Assert.Contains("Second", repo.Git("show", "HEAD:.entitytracker/project.json"), StringComparison.Ordinal);
        Assert.Equal(old, File.GetLastWriteTimeUtc(manifest));
    }

    [Fact]
    public async Task CancellingASyncWhileItCommitsLetsTheCommitFinish()
    {
        using Checkout repo = await Checkout.CreateSyncedAsync();
        // A pre-commit hook keeps the commit running while the sync is cancelled.
        string hook = Path.Combine(repo.Root, ".git", "hooks", "pre-commit");
        File.WriteAllText(hook, "#!/bin/sh\nsleep 3\n");
        SystemGitTransport git = new(recoveryDirectory: repo.RecoveryDirectory);
        GitWorkingTreeState state = await git.InspectAsync(repo.Root);
        ProjectSnapshotPackage package = new ProjectSnapshotJsonCodec().Encode(Snapshot(repo.ProjectId, "Second"));
        using CancellationTokenSource cancel = new(TimeSpan.FromMilliseconds(500));

        string head = await git.CommitSnapshotAsync(repo.Root, state.SnapshotFiles, package, cancel.Token);

        Assert.True(cancel.IsCancellationRequested);
        Assert.Equal(head, repo.Git("rev-parse", "HEAD"));
        Assert.Contains("Second", repo.Git("show", "HEAD:.entitytracker/project.json"), StringComparison.Ordinal);
        Assert.Empty(repo.Git("status", "--porcelain"));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(repo.Root, ".git"), "*.lock", SearchOption.AllDirectories));
    }

    [Fact]
    public void OnlyTheSnapshotRestoreIsAllowed()
    {
        SystemGitTransport.ValidateCommand(["restore", "--source=HEAD", "--staged", "--worktree", "--", ".entitytracker"]);
        Assert.Throws<InvalidOperationException>(() =>
            SystemGitTransport.ValidateCommand(["restore", "--source=HEAD", "--staged", "--worktree", "--", "."]));
        Assert.Throws<InvalidOperationException>(() =>
            SystemGitTransport.ValidateCommand(["restore", "--", "README.md"]));
    }

    private static async Task AssertRepairedAndPushedAsync(Checkout repo, string expectedName, string changedFile)
    {
        ProjectSyncLink result = await repo.Service.SyncNowAsync(repo.ProjectId);

        Assert.Equal("Current", result.SyncStatus);
        Assert.Contains("repaired", result.LastResult, StringComparison.Ordinal);
        string head = repo.Git("rev-parse", "HEAD");
        Assert.Equal(head, result.LastCommonCommit);
        Assert.Equal(head, repo.Git("rev-parse", "origin/main"));
        Assert.Equal(head, repo.Remote("rev-parse", "main"));
        Assert.Contains(expectedName, repo.Git("show", "HEAD:.entitytracker/project.json"), StringComparison.Ordinal);
        Assert.Empty(repo.Git("status", "--porcelain"));
        string copy = Assert.Single(Directory.GetDirectories(repo.RecoveryDirectory));
        Assert.True(File.Exists(Path.Combine(copy, ".entitytracker", changedFile)));
    }

    private static ProjectSnapshot Snapshot(ProjectId id, string name) => new(ProjectSnapshot.CurrentFormatVersion,
        new SnapshotProject(id.Value, name, "Active", new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), null), [], []);

    /// <summary>A Project store whose content and revision move forward like the real database.</summary>
    private sealed class EditableSnapshotStore(ProjectSnapshot snapshot) : IProjectSnapshotStore
    {
        private ProjectSnapshot _snapshot = snapshot;
        private long _revision = 1;

        public void Change(ProjectSnapshot snapshot)
        {
            _snapshot = snapshot;
            _revision++;
        }

        public Task<ProjectSnapshotRead> ReadAsync(ProjectId projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectSnapshotRead(_snapshot, _revision));

        public Task<long> ApplyAsync(ProjectSnapshot snapshot, long expectedRevision, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ApproveDeletions : IOutboundDeletionApproval
    {
        public Task<bool> ApproveAsync(IReadOnlyList<string> deletedObjects, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    /// <summary>A checkout with a bare remote, linked and synced once.</summary>
    private sealed class Checkout : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "entitytracker-repair-" + Guid.NewGuid().ToString("N"));

        private Checkout()
        {
            Snapshots = new EditableSnapshotStore(Snapshot(ProjectId, "First"));
            Service = new ProjectGitSyncService(new JsonProjectSyncLinkStore(Path.Combine(_folder, "links.json")),
                new SystemGitTransport(recoveryDirectory: RecoveryDirectory), Snapshots,
                new ProjectSnapshotJsonCodec(), new ApproveDeletions());
        }

        public string Root => Path.Combine(_folder, "checkout");
        public string RemotePath => Path.Combine(_folder, "remote.git");
        public string RecoveryDirectory => Path.Combine(_folder, "recovery");
        public ProjectId ProjectId { get; } = ProjectId.New();
        public EditableSnapshotStore Snapshots { get; }
        public ProjectGitSyncService Service { get; }

        public static async Task<Checkout> CreateSyncedAsync()
        {
            Checkout repo = new();
            Directory.CreateDirectory(repo.Root);
            Run(repo._folder, "init", "--bare", "-b", "main", repo.RemotePath);
            repo.Git("init", "-b", "main");
            repo.Git("config", "user.name", "Repair Test");
            repo.Git("config", "user.email", "test@example.invalid");
            File.WriteAllText(Path.Combine(repo.Root, "README.md"), "Start\n");
            repo.Git("add", "README.md");
            repo.Git("commit", "-m", "Start");
            repo.Git("remote", "add", "origin", repo.RemotePath);
            repo.Git("push", "-u", "origin", "main");
            await repo.Service.LinkAsync(repo.ProjectId, repo.Root);
            ProjectSyncLink synced = await repo.Service.SyncNowAsync(repo.ProjectId);
            Assert.Equal("Current", synced.SyncStatus);
            return repo;
        }

        public void WriteSnapshotFile(string relative, string content)
        {
            string path = Path.Combine(Root, ".entitytracker", relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public string ReadSnapshotFile(string relative) =>
            File.ReadAllText(Path.Combine(Root, ".entitytracker", relative.Replace('/', Path.DirectorySeparatorChar)));

        /// <summary>Leaves a Git lock file as a killed Git command does, last written <paramref name="age"/> ago.</summary>
        public string LeaveLock(string relative, TimeSpan age)
        {
            string path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
            return path;
        }

        public string Git(params string[] args) => Run(Root, args);

        public string Remote(params string[] args) => Run(RemotePath, args);

        private static string Run(string folder, params string[] args)
        {
            ProcessStartInfo start = new("git") { WorkingDirectory = folder, RedirectStandardOutput = true,
                RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (string arg in args) start.ArgumentList.Add(arg);
            using Process process = Process.Start(start)!;
            Task<string> error = process.StandardError.ReadToEndAsync();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException(error.Result);
            return output.TrimEnd('\r', '\n');
        }

        public void Dispose()
        {
            if (!Directory.Exists(_folder)) return;
            foreach (string path in Directory.EnumerateFileSystemEntries(_folder, "*", SearchOption.AllDirectories))
                File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(_folder, true);
        }
    }
}

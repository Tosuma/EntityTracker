using System.Diagnostics;
using EntityTracker.Application.GitSync;
using EntityTracker.Application.Snapshots;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.GitSync;
using EntityTracker.Infrastructure.Snapshots;

namespace EntityTracker.Infrastructure.Tests.GitSync;

/// <summary>
/// A sync must never leave a commit the sync link does not know about, and a checkout already in
/// that state ("Repository HEAD changed since the last sync") recovers when the unknown commits are
/// EntityTracker's own snapshot commits.
/// </summary>
public sealed class UnrecordedCommitRecoveryTests
{
    [Fact]
    public async Task AnEditDuringSyncStillRecordsTheCommitSoTheNextSyncContinues()
    {
        using Repository repo = Repository.Create();
        EditableSnapshotStore snapshots = new(Snapshot(repo.ProjectId, "First"));
        CommitHookTransport git = new();
        ProjectGitSyncService service = Service(repo, snapshots, git);
        await service.LinkAsync(repo.ProjectId, repo.Root);
        await service.SyncNowAsync(repo.ProjectId);

        // The Project is renamed, and edited again while the sync is committing the rename.
        snapshots.Change(Snapshot(repo.ProjectId, "Second"));
        git.AfterCommit = () => snapshots.Change(Snapshot(repo.ProjectId, "Third"));
        InvalidOperationException stopped = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SyncNowAsync(repo.ProjectId));
        Assert.Contains("changed while syncing", stopped.Message, StringComparison.Ordinal);

        string committed = repo.Git("rev-parse", "HEAD");
        Assert.Equal(committed, (await service.GetLinkAsync(repo.ProjectId))!.LastCommonCommit);

        git.AfterCommit = null;
        ProjectSyncLink next = await service.SyncNowAsync(repo.ProjectId);

        Assert.Equal("Current", next.SyncStatus);
        Assert.NotEqual(committed, next.LastCommonCommit);
        Assert.Equal(next.LastCommonCommit, repo.Git("rev-parse", "HEAD"));
        Assert.Contains("Third", repo.Git("show", "HEAD:.entitytracker/project.json"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACheckoutLeftWithAnUnrecordedSnapshotCommitAndLaterEditsRecovers()
    {
        using Repository repo = Repository.Create();
        EditableSnapshotStore snapshots = new(Snapshot(repo.ProjectId, "First"));
        ProjectGitSyncService service = Service(repo, snapshots, new SystemGitTransport());
        await service.LinkAsync(repo.ProjectId, repo.Root);
        ProjectSyncLink recorded = await service.SyncNowAsync(repo.ProjectId);

        // What an interrupted sync used to leave behind: EntityTracker's own commit, not recorded,
        // followed by more edits in the app.
        await LeaveUnrecordedSnapshotCommitAsync(repo, Snapshot(repo.ProjectId, "Second"));
        snapshots.Change(Snapshot(repo.ProjectId, "Third"));

        ProjectSyncLink next = await service.SyncNowAsync(repo.ProjectId);

        Assert.Equal("Current", next.SyncStatus);
        Assert.NotEqual(recorded.LastCommonCommit, next.LastCommonCommit);
        Assert.Equal(next.LastCommonCommit, repo.Git("rev-parse", "HEAD"));
        Assert.Contains("Third", repo.Git("show", "HEAD:.entitytracker/project.json"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHandMadeCommitStillNeedsAReview()
    {
        using Repository repo = Repository.Create();
        EditableSnapshotStore snapshots = new(Snapshot(repo.ProjectId, "First"));
        ProjectGitSyncService service = Service(repo, snapshots, new SystemGitTransport());
        await service.LinkAsync(repo.ProjectId, repo.Root);
        await service.SyncNowAsync(repo.ProjectId);
        await LeaveUnrecordedSnapshotCommitAsync(repo, Snapshot(repo.ProjectId, "Second"));
        repo.Git("commit", "--amend", "-m", "Tweak the snapshot by hand");
        snapshots.Change(Snapshot(repo.ProjectId, "Third"));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SyncNowAsync(repo.ProjectId));

        Assert.Contains("Repository HEAD changed since the last sync", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASnapshotCommitThatAlsoTouchesOtherFilesStillNeedsAReview()
    {
        using Repository repo = Repository.Create();
        EditableSnapshotStore snapshots = new(Snapshot(repo.ProjectId, "First"));
        ProjectGitSyncService service = Service(repo, snapshots, new SystemGitTransport());
        await service.LinkAsync(repo.ProjectId, repo.Root);
        await service.SyncNowAsync(repo.ProjectId);
        await LeaveUnrecordedSnapshotCommitAsync(repo, Snapshot(repo.ProjectId, "Second"));
        File.WriteAllText(Path.Combine(repo.Root, "README.md"), "Changed\n");
        repo.Git("add", "README.md");
        repo.Git("commit", "--amend", "--no-edit");
        snapshots.Change(Snapshot(repo.ProjectId, "Third"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SyncNowAsync(repo.ProjectId));
    }

    [Fact]
    public async Task ListCommitsReturnsSubjectsParentsAndChangedPathsOldestFirst()
    {
        using Repository repo = Repository.Create();
        string start = repo.Git("rev-parse", "HEAD");
        File.WriteAllText(Path.Combine(repo.Root, "a.txt"), "a\n");
        repo.Git("add", "a.txt");
        repo.Git("commit", "-m", "First change");
        File.WriteAllText(Path.Combine(repo.Root, "b.txt"), "b\n");
        repo.Git("add", "b.txt");
        repo.Git("commit", "-m", "Second change");
        string head = repo.Git("rev-parse", "HEAD");

        IReadOnlyList<GitCommitSummary> commits = await new SystemGitTransport().ListCommitsAsync(repo.Root, start, head);

        Assert.Equal(["First change", "Second change"], commits.Select(commit => commit.Subject));
        Assert.Equal(["a.txt"], commits[0].ChangedPaths);
        Assert.Equal(["b.txt"], commits[1].ChangedPaths);
        Assert.Equal([commits[0].Id], commits[1].Parents);
        Assert.Equal(head, commits[1].Id);
    }

    private static async Task LeaveUnrecordedSnapshotCommitAsync(Repository repo, ProjectSnapshot snapshot)
    {
        SystemGitTransport git = new();
        GitWorkingTreeState state = await git.InspectAsync(repo.Root);
        await git.CommitSnapshotAsync(repo.Root, state.SnapshotFiles, new ProjectSnapshotJsonCodec().Encode(snapshot));
    }

    private static ProjectGitSyncService Service(Repository repo, IProjectSnapshotStore snapshots, ILocalGitTransport git) =>
        new(new JsonProjectSyncLinkStore(repo.Root + "-links.json"), git, snapshots,
            new ProjectSnapshotJsonCodec(), new ApproveDeletions());

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

    /// <summary>The real git transport, with a hook that runs right after a snapshot commit.</summary>
    private sealed class CommitHookTransport : ILocalGitTransport
    {
        private readonly SystemGitTransport _git = new();

        public Action? AfterCommit { get; set; }

        public Task<IAsyncDisposable> LockAsync(string path, CancellationToken cancellationToken = default) =>
            _git.LockAsync(path, cancellationToken);

        public Task<GitWorkingTreeState> InspectAsync(string path, CancellationToken cancellationToken = default) =>
            _git.InspectAsync(path, cancellationToken);

        public async Task<string> CommitSnapshotAsync(string path, IReadOnlyDictionary<string, byte[]> oldFiles,
            ProjectSnapshotPackage package, CancellationToken cancellationToken = default)
        {
            string head = await _git.CommitSnapshotAsync(path, oldFiles, package, cancellationToken);
            AfterCommit?.Invoke();
            return head;
        }

        public Task<bool> IsAncestorAsync(string path, string ancestor, string descendant,
            CancellationToken cancellationToken = default) =>
            _git.IsAncestorAsync(path, ancestor, descendant, cancellationToken);

        public Task<IReadOnlyList<GitCommitSummary>> ListCommitsAsync(string path, string from, string to,
            CancellationToken cancellationToken = default) =>
            _git.ListCommitsAsync(path, from, to, cancellationToken);
    }

    private sealed class ApproveDeletions : IOutboundDeletionApproval
    {
        public Task<bool> ApproveAsync(IReadOnlyList<string> deletedObjects, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class Repository : IDisposable
    {
        private Repository() { }

        public string Root { get; } = Path.Combine(Path.GetTempPath(), "entitytracker-unrecorded-" + Guid.NewGuid().ToString("N"));
        public ProjectId ProjectId { get; } = ProjectId.New();

        public static Repository Create()
        {
            Repository repo = new();
            Directory.CreateDirectory(repo.Root);
            repo.Git("init", "-b", "main");
            repo.Git("config", "user.name", "Recovery Test");
            repo.Git("config", "user.email", "test@example.invalid");
            File.WriteAllText(Path.Combine(repo.Root, "README.md"), "Start\n");
            repo.Git("add", "README.md");
            repo.Git("commit", "-m", "Start");
            return repo;
        }

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

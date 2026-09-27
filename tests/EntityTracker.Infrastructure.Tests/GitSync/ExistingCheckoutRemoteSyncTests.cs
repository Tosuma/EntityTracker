using System.Diagnostics;
using EntityTracker.Application.GitSync;
using EntityTracker.Application.Snapshots;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.GitSync;
using EntityTracker.Infrastructure.Persistence;
using EntityTracker.Infrastructure.Snapshots;
using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Tests.GitSync;

public sealed class ExistingCheckoutRemoteSyncTests
{
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
    public void GitAdapterRejectsRepositoryManagementCommands(string verb, string argument) =>
        Assert.Throws<InvalidOperationException>(() => SystemGitTransport.ValidateCommand([verb, argument]));

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
        Assert.NotEmpty(Directory.GetFiles(secondCatalog.BackupDirectory, "entity-tracker-pre-sync-*.db"));
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
        Assert.Contains("\"formatVersion\":1", manifest);
        File.WriteAllText(manifestPath, manifest.Replace("\"formatVersion\":1", "\"formatVersion\":99"));
        workspace.Git(checkout, "add", ".entitytracker");
        workspace.Git(checkout, "commit", "-m", "Unsupported version");
        await Assert.ThrowsAsync<InvalidDataException>(() => sync.ImportAsync(checkout));
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
        await Assert.ThrowsAsync<InvalidOperationException>(() => sync.ImportAsync(checkout));
        workspace.Git(checkout, "checkout", "main");
        await sync.ImportAsync(checkout);
        workspace.Git(checkout, "branch", "-m", "other");
        await Assert.ThrowsAsync<InvalidOperationException>(() => sync.SyncNowAsync(new ProjectId(id)));
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
        Assert.Single(Directory.GetFiles(catalog.BackupDirectory, "entity-tracker-pre-sync-*.db"));
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
    public async Task PushRaceKeepsLocalCommitPendingForReview()
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
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sync.SyncNowAsync(projectId));
        Assert.Contains("GS-04", error.Message);
        Assert.Equal("PendingPush", (await sync.GetLinkAsync(projectId))!.SyncStatus);
        Assert.Equal("Local edit", (await catalog.Snapshots.ReadAsync(projectId)).Snapshot!.Project.Name);
        Assert.NotEqual(workspace.Git(first, "rev-parse", "HEAD"), workspace.Git(other, "rev-parse", "HEAD"));
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
            "entity-tracker-pre-sync-*.db").Length;
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
            "entity-tracker-pre-sync-*.db").Length);
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
            "Active", "ManualAndImported", 2, "Ada", "Core", time, later, later,
            [new SnapshotDependency(entityId, dependencyId, "Mandatory", time, later)],
            [new SnapshotUnresolvedDependency(entityId, "External", "Optional", time, later)],
            [new SnapshotOverride(entityId, "External", "Suppress", time, later)]);
        SnapshotEntity dependency = new(dependencyId, trackerId, "Customers", "Reconciled", "Archived notes",
            "Archived", "Imported", 4, "Grace", "Legacy", time, later, later, [], [], []);
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
        return new ProjectSnapshot(1, new SnapshotProject(id, name, "Active", time, later, null),
            [tracker, recycled]);
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
            IProjectSyncBackup? backup = null, IProjectSyncLinkStore? linkStore = null) => new(
            linkStore ?? _links, transport ?? new SystemGitTransport(), Snapshots,
            new ProjectSnapshotJsonCodec(), new ApproveDeletions(), Projects,
            backup ?? Backup());

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

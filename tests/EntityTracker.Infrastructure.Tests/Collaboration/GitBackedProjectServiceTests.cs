using System.Diagnostics;

using EntityTracker.Application.Collaboration;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Collaboration;
using EntityTracker.Infrastructure.Git;
using EntityTracker.Infrastructure.Persistence;
using EntityTracker.Infrastructure.RepositoryFormat;
using EntityTracker.Infrastructure.Tests.Persistence;

namespace EntityTracker.Infrastructure.Tests.Collaboration;

public sealed class GitBackedProjectServiceTests
{
    [Fact]
    public async Task DuplicateOutboxOperation_RollsBackTheSqliteMutation()
    {
        await using TemporarySqliteFile sqliteFile = new();
        using TemporaryGitRepository repository = new();
        SqliteDatabase database = new(sqliteFile.DatabasePath);
        await database.InitializeAsync();
        TestSystem system = CreateSystem(database, repository.RegistryPath);
        Project project = Assert.Single(await system.Projects.GetAllAsync());
        await system.Service.LinkAsync(project.Id, repository.Path);
        OperationId operationId = OperationId.New();
        DateTimeOffset occurred = DateTimeOffset.UtcNow;

        await system.Service.ApplyAsync(new RenameProjectMutation(
            project.Id, "First queued name", operationId, occurred));
        await Assert.ThrowsAsync<InvalidOperationException>(() => system.Service.ApplyAsync(
            new RenameProjectMutation(project.Id, "Must roll back", operationId, occurred)));

        Assert.Equal("First queued name", (await system.Projects.GetAsync(project.Id))!.Name);
        Assert.Equal(1, (await system.Service.GetCachedStatusAsync(project.Id)).PendingOperationCount);
        Assert.Equal(1, repository.CommitCount());
    }

    [Fact]
    public async Task EntityMutation_StaysInSqliteUntilSyncThenCreatesOneCommit()
    {
        await using TemporarySqliteFile sqliteFile = new();
        using TemporaryGitRepository repository = new();
        SqliteDatabase database = new(sqliteFile.DatabasePath);
        await database.InitializeAsync();
        TestSystem system = CreateSystem(database, repository.RegistryPath);
        Project project = Assert.Single(await system.Projects.GetAllAsync());
        Tracker tracker = Assert.Single(await system.Trackers.GetByProjectAsync(project.Id));
        await system.Service.LinkAsync(project.Id, repository.Path);
        ProjectMutationCoordinator coordinator = new(system.Service, system.Trackers);
        TrackedEntity entity = new(EntityId.New(), tracker.Id, "queued_entity");

        await coordinator.ApplyAsync(tracker.Id, new TrackedStateChangeSet(
            [entity], [], [], [], [], []));

        Assert.Equal(1, repository.CommitCount());
        Assert.Contains(entity.Id, (await new SqliteEntityRepository(database).GetAllAsync(tracker.Id)).Select(item => item.Id));
        Assert.Equal(1, (await system.Service.GetCachedStatusAsync(project.Id)).PendingOperationCount);

        ProjectSyncResult result = await system.Service.SyncAsync(project.Id);

        Assert.True(result.Outcome == ProjectSyncOutcome.LocalCommitted, result.Message);
        Assert.Equal(1, result.LocalCommitCount);
        Assert.Equal(2, repository.CommitCount());
        Assert.Equal(0, (await system.Service.GetCachedStatusAsync(project.Id)).PendingOperationCount);
    }

    [Fact]
    public async Task LocalRepositoryWithoutRemote_LinksCommitsMutatesAndRebuildsExactCache()
    {
        await using TemporarySqliteFile sqliteFile = new();
        using TemporaryGitRepository repository = new();
        SqliteDatabase database = new(sqliteFile.DatabasePath);
        await database.InitializeAsync();
        TestSystem system = CreateSystem(database, repository.RegistryPath);
        Project project = Assert.Single(await system.Projects.GetAllAsync());

        await system.Service.LinkAsync(project.Id, repository.Path);

        Assert.Equal(1, repository.CommitCount());
        Assert.Empty(repository.Lines("remote"));
        Assert.True(File.Exists(Path.Combine(repository.Path, ProjectRepositoryCodec.ManifestPath)));
        ProjectRepositoryStatus cached = await system.Service.GetCachedStatusAsync(project.Id);
        Assert.Equal(ProjectRepositoryStatusKind.GitRegistered, cached.Kind);
        Assert.Contains("SQLite working copy", cached.Diagnostic, StringComparison.Ordinal);
        ProjectRepositoryStatus linked = await system.Service.GetStatusAsync(project.Id);
        Assert.Equal(ProjectRepositoryStatusKind.GitClean, linked.Kind);
        Assert.Equal(ProjectSyncState.NoUpstream, linked.SyncState);
        ProjectSyncResult localOnlySync = await system.Service.SyncAsync(project.Id);
        Assert.Equal(ProjectSyncOutcome.UpToDate, localOnlySync.Outcome);
        Assert.Equal(1, repository.CommitCount());

        ProjectMutationCoordinator coordinator = new(system.Service, system.Trackers);
        await coordinator.RenameProjectAsync(project.Id, "Git Project");

        Assert.Equal(1, repository.CommitCount());
        Assert.Equal("Git Project", (await system.Projects.GetAsync(project.Id))!.Name);
        Assert.Equal(1, (await system.Service.GetCachedStatusAsync(project.Id)).PendingOperationCount);

        ProjectSyncResult committed = await system.Service.SyncAsync(project.Id);

        Assert.Equal(ProjectSyncOutcome.LocalCommitted, committed.Outcome);
        Assert.Equal(1, committed.LocalCommitCount);
        Assert.Equal(2, repository.CommitCount());
        string commitMessage = string.Join("\n", repository.Lines("log", "-1", "--format=%B"));
        Assert.Contains("Rename Project", commitMessage, StringComparison.Ordinal);
        Assert.Contains("EntityTracker-Operation-Id:", commitMessage, StringComparison.Ordinal);

        await system.SqliteCatalog.RenameProjectAsync(project.Id, "Corrupt cache value");
        await system.Service.RebuildCacheAsync(project.Id);

        Assert.Equal("Git Project", (await system.Projects.GetAsync(project.Id))!.Name);
        Assert.Equal(2, repository.CommitCount());
    }

    [Fact]
    public async Task DirtyManagedFile_DoesNotBlockSqliteMutationButBlocksExplicitSync()
    {
        await using TemporarySqliteFile sqliteFile = new();
        using TemporaryGitRepository repository = new();
        SqliteDatabase database = new(sqliteFile.DatabasePath);
        await database.InitializeAsync();
        TestSystem system = CreateSystem(database, repository.RegistryPath);
        Project project = Assert.Single(await system.Projects.GetAllAsync());
        await system.Service.LinkAsync(project.Id, repository.Path);
        await File.AppendAllTextAsync(
            Path.Combine(repository.Path, ProjectRepositoryCodec.ManifestPath),
            " ");

        ProjectMutationCoordinator coordinator = new(system.Service, system.Trackers);
        await coordinator.RenameProjectAsync(project.Id, "SQLite remains available");

        Assert.Equal(1, repository.CommitCount());
        Assert.Equal("SQLite remains available", (await system.Projects.GetAsync(project.Id))!.Name);
        Assert.Equal(1, (await system.Service.GetCachedStatusAsync(project.Id)).PendingOperationCount);
        ProjectSyncResult sync = await system.Service.SyncAsync(project.Id);
        Assert.Equal(ProjectSyncOutcome.Failed, sync.Outcome);
        Assert.Equal(ProjectSyncFailureKind.RepositoryBlocked, sync.FailureKind);
        Assert.Contains("clean", sync.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ProjectRepositoryStatusKind.Blocked,
            (await system.Service.GetStatusAsync(project.Id)).Kind);
    }

    [Fact]
    public async Task OpenThenLocateMovedRepository_RebuildsAnotherInstallWithoutHistoryLoss()
    {
        await using TemporarySqliteFile sourceFile = new();
        await using TemporarySqliteFile destinationFile = new();
        using TemporaryGitRepository repository = new();
        SqliteDatabase sourceDatabase = new(sourceFile.DatabasePath);
        await sourceDatabase.InitializeAsync();
        string sourceRegistry = repository.RegistryPath;
        TestSystem source = CreateSystem(sourceDatabase, sourceRegistry);
        Project sourceProject = Assert.Single(await source.Projects.GetAllAsync());
        await source.SqliteCatalog.RenameProjectAsync(sourceProject.Id, "Portable Git Project");
        await source.Service.LinkAsync(sourceProject.Id, repository.Path);
        string expectedHead = repository.Lines("rev-parse", "HEAD").Single();

        SqliteDatabase destinationDatabase = new(destinationFile.DatabasePath);
        await destinationDatabase.InitializeAsync();
        string destinationRegistry = repository.RegistryPath + ".destination";
        TestSystem destination = CreateSystem(destinationDatabase, destinationRegistry);
        ProjectId openedId = await destination.Service.OpenAsync(repository.Path);

        Assert.Equal(sourceProject.Id, openedId);
        Assert.Equal("Portable Git Project", (await destination.Projects.GetAsync(openedId))!.Name);
        Assert.Equal(ProjectRepositoryStatusKind.GitClean,
            (await destination.Service.GetStatusAsync(openedId)).Kind);

        string movedPath = repository.MoveToSibling();
        Assert.Equal(ProjectRepositoryStatusKind.Unavailable,
            (await destination.Service.GetStatusAsync(openedId)).Kind);
        await destination.Service.LocateAsync(openedId, movedPath);

        ProjectRepositoryStatus relocated = await destination.Service.GetStatusAsync(openedId);
        Assert.Equal(ProjectRepositoryStatusKind.GitClean, relocated.Kind);
        Assert.Equal(Path.GetFullPath(movedPath), relocated.RepositoryPath);
        Assert.Equal(expectedHead, repository.Lines("rev-parse", "HEAD").Single());
        if (File.Exists(destinationRegistry)) File.Delete(destinationRegistry);
    }

    [Fact]
    public async Task MissingLocalGitIdentity_BlocksLinkWithoutFilesCommitOrRegistration()
    {
        await using TemporarySqliteFile sqliteFile = new();
        using TemporaryGitRepository repository = new(configureIdentity: false);
        SqliteDatabase database = new(sqliteFile.DatabasePath);
        await database.InitializeAsync();
        TestSystem system = CreateSystem(database, repository.RegistryPath);
        Project project = Assert.Single(await system.Projects.GetAllAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => system.Service.LinkAsync(project.Id, repository.Path));

        Assert.False(File.Exists(Path.Combine(repository.Path, ProjectRepositoryCodec.ManifestPath)));
        Assert.Null(await new LocalRepositoryRegistry(repository.RegistryPath).GetAsync(project.Id));
    }

    [Fact]
    public async Task RegistryAcceptsSha256ObjectIdsAndDoesNotOverwriteNewerVersion()
    {
        string root = Path.Combine(Path.GetTempPath(), "EntityTracker-RegistryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "repositories.json");
        try
        {
            LocalRepositoryRegistry registry = new(path);
            ProjectId projectId = ProjectId.New();
            await registry.UpsertAsync(new LocalRepositoryRegistration(
                projectId, root, "main", new string('a', 64)));
            Assert.Equal(new string('a', 64), (await registry.GetAsync(projectId))!.LastProjectedCommit);

            await File.WriteAllTextAsync(path, "{\"version\":3,\"repositories\":[]}");
            await Assert.ThrowsAsync<InvalidDataException>(() => registry.GetAllAsync());
            Assert.Equal("{\"version\":3,\"repositories\":[]}", await File.ReadAllTextAsync(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RegistryVersionOneLoadsAndUpgradesWithoutInventingSyncTimes()
    {
        string root = Path.Combine(Path.GetTempPath(), "EntityTracker-RegistryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "repositories.json");
        ProjectId projectId = ProjectId.New();
        try
        {
            await File.WriteAllTextAsync(path,
                $$"""
                {
                  "version": 1,
                  "repositories": [
                    {
                      "projectId": "{{projectId.Value:D}}",
                      "repositoryPath": "{{root.Replace("\\", "\\\\", StringComparison.Ordinal)}}",
                      "managedBranch": "main",
                      "lastProjectedCommit": "{{new string('a', 40)}}"
                    }
                  ]
                }
                """);
            LocalRepositoryRegistry registry = new(path);

            LocalRepositoryRegistration loaded = Assert.Single(await registry.GetAllAsync());
            Assert.Null(loaded.LastSuccessfulFetchAtUtc);
            Assert.Null(loaded.LastSuccessfulPushAtUtc);

            await registry.UpsertAsync(loaded with
            {
                LastSuccessfulFetchAtUtc = new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero)
            });
            string upgraded = await File.ReadAllTextAsync(path);
            Assert.Contains("\"version\": 2", upgraded, StringComparison.Ordinal);
            Assert.Contains("lastSuccessfulFetchAtUtc", upgraded, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExplicitSync_PushesFastForwardsAndBlocksDivergenceWithoutChangingCache()
    {
        await using TemporarySqliteFile sourceFile = new();
        await using TemporarySqliteFile destinationFile = new();
        using TemporaryGitRepository sourceRepository = new();
        string bareRemote = Path.Combine(Path.GetTempPath(), "EntityTracker-GitBackedTests", $"{Guid.NewGuid():N}.git");
        string? destinationPath = null;
        try
        {
            Directory.CreateDirectory(bareRemote);
            sourceRepository.RunGit("init", "--bare", "--initial-branch=main", bareRemote);
            GitCommandClient sourceGit = new("git", null, allowLocalRemotesForTesting: true);
            SqliteDatabase sourceDatabase = new(sourceFile.DatabasePath);
            await sourceDatabase.InitializeAsync();
            TestSystem source = CreateSystem(sourceDatabase, sourceRepository.RegistryPath, sourceGit);
            Project project = Assert.Single(await source.Projects.GetAllAsync());
            await source.SqliteCatalog.RenameProjectAsync(project.Id, "Shared Project");
            await source.Service.LinkAsync(project.Id, sourceRepository.Path);
            sourceRepository.RunGit("remote", "add", "team", bareRemote);
            sourceRepository.RunGit("push", "-u", "team", "refs/heads/main:refs/heads/main");

            ProjectSyncResult equal = await source.Service.SyncAsync(project.Id);
            Assert.Equal(ProjectSyncOutcome.UpToDate, equal.Outcome);

            destinationPath = Path.Combine(Path.GetTempPath(), "EntityTracker-GitBackedTests", Guid.NewGuid().ToString("N"));
            sourceRepository.RunGit("-c", "core.autocrlf=false", "clone", bareRemote, destinationPath);
            RunGit(destinationPath, "config", "core.autocrlf", "false");
            RunGit(destinationPath, "config", "user.name", "EntityTracker Tests");
            RunGit(destinationPath, "config", "user.email", "entitytracker@example.invalid");
            SqliteDatabase destinationDatabase = new(destinationFile.DatabasePath);
            await destinationDatabase.InitializeAsync();
            GitCommandClient destinationGit = new("git", null, allowLocalRemotesForTesting: true);
            TestSystem destination = CreateSystem(
                destinationDatabase, destinationPath + "-registry.json", destinationGit);
            ProjectId openedId = await destination.Service.OpenAsync(destinationPath);

            ProjectMutationCoordinator sourceMutations = new(source.Service, source.Trackers);
            await sourceMutations.RenameProjectAsync(project.Id, "Pushed Project");
            ProjectSyncResult pushed = await source.Service.SyncAsync(project.Id);
            Assert.Equal(ProjectSyncOutcome.Pushed, pushed.Outcome);
            Assert.Equal(ProjectSyncState.UpToDate,
                (await source.Service.GetStatusAsync(project.Id)).SyncState);

            ProjectSyncResult fastForwarded = await destination.Service.SyncAsync(openedId);
            Assert.Equal(ProjectSyncOutcome.FastForwarded, fastForwarded.Outcome);
            Assert.Equal("Pushed Project", (await destination.Projects.GetAsync(openedId))!.Name);

            string destinationHeadBeforeInvalidRemote = RunGit(destinationPath, "rev-parse", "HEAD").Trim();
            await File.WriteAllTextAsync(Path.Combine(sourceRepository.Path, "unmanaged.txt"), "unsupported\n");
            sourceRepository.RunGit("add", "unmanaged.txt");
            sourceRepository.RunGit("commit", "-m", "unsupported external edit");
            sourceRepository.RunGit("push", "team", "main:main");

            ProjectSyncResult invalidRemote = await destination.Service.SyncAsync(openedId);
            Assert.Equal(ProjectSyncOutcome.Failed, invalidRemote.Outcome);
            Assert.Equal(ProjectSyncFailureKind.InvalidRemote, invalidRemote.FailureKind);
            Assert.Equal(destinationHeadBeforeInvalidRemote, RunGit(destinationPath, "rev-parse", "HEAD").Trim());
            Assert.Equal("Pushed Project", (await destination.Projects.GetAsync(openedId))!.Name);

            sourceRepository.RunGit("rm", "unmanaged.txt");
            sourceRepository.RunGit("commit", "-m", "remove unsupported external edit");
            sourceRepository.RunGit("push", "team", "main:main");

            string manifestPath = Path.Combine(sourceRepository.Path, ProjectRepositoryCodec.ManifestPath);
            string canonicalManifest = await File.ReadAllTextAsync(manifestPath);
            await File.WriteAllTextAsync(
                manifestPath,
                canonicalManifest.Replace("\"formatVersion\": 1", "\"formatVersion\": 99", StringComparison.Ordinal));
            sourceRepository.RunGit("add", ProjectRepositoryCodec.ManifestPath);
            sourceRepository.RunGit("commit", "-m", "unsupported future schema");
            sourceRepository.RunGit("push", "team", "main:main");

            ProjectSyncResult newerSchema = await destination.Service.SyncAsync(openedId);
            Assert.Equal(ProjectSyncOutcome.Failed, newerSchema.Outcome);
            Assert.Equal(ProjectSyncFailureKind.UnsupportedSchema, newerSchema.FailureKind);
            Assert.Equal(destinationHeadBeforeInvalidRemote, RunGit(destinationPath, "rev-parse", "HEAD").Trim());

            await File.WriteAllTextAsync(manifestPath, canonicalManifest);
            sourceRepository.RunGit("add", ProjectRepositoryCodec.ManifestPath);
            sourceRepository.RunGit("commit", "-m", "restore supported schema");
            sourceRepository.RunGit("push", "team", "main:main");
            await source.Service.RebuildCacheAsync(project.Id);
            Assert.Equal(ProjectSyncOutcome.FastForwarded,
                (await destination.Service.SyncAsync(openedId)).Outcome);

            await sourceMutations.RenameProjectAsync(project.Id, "Source divergence");
            ProjectMutationCoordinator destinationMutations = new(destination.Service, destination.Trackers);
            await destinationMutations.RenameProjectAsync(openedId, "Destination divergence");
            Assert.Equal(ProjectSyncOutcome.Pushed, (await source.Service.SyncAsync(project.Id)).Outcome);
            string destinationHead = RunGit(destinationPath, "rev-parse", "HEAD").Trim();

            ProjectSyncResult diverged = await destination.Service.SyncAsync(openedId);

            Assert.Equal(ProjectSyncOutcome.MergeRequired, diverged.Outcome);
            Assert.Equal(ProjectSyncState.MergeRequired, diverged.Status.SyncState);
            Assert.Equal(destinationHead, RunGit(destinationPath, "rev-parse", "HEAD").Trim());
            Assert.Equal("Destination divergence", (await destination.Projects.GetAsync(openedId))!.Name);
            Assert.NotNull(diverged.Status.LastSuccessfulFetchAtUtc);
        }
        finally
        {
            DeleteGitDirectory(destinationPath);
            DeleteGitDirectory(bareRemote);
            if (destinationPath is not null && File.Exists(destinationPath + "-registry.json"))
                File.Delete(destinationPath + "-registry.json");
        }
    }

    private static TestSystem CreateSystem(
        SqliteDatabase database,
        string registryPath,
        GitCommandClient? gitClient = null)
    {
        SqliteProjectRepository projects = new(database);
        SqliteTrackerRepository trackers = new(database);
        SqliteProjectTrackerStore catalog = new(database);
        SqliteTrackedStateStore tracked = new(database);
        ProjectRepositoryCodec codec = new();
        GitCommandClient git = gitClient ?? new();
        GitBackedProjectService service = new(
            new LocalRepositoryRegistry(registryPath),
            new GitRepositoryValidator(git),
            git,
            new ProjectRepositoryStore(codec),
            codec,
            new SqliteProjectStateStore(
                database,
                projects,
                trackers,
                new SqliteEntityRepository(database),
                new SqliteEntityAuditReader(database),
                new SqliteDependencyRepository(database),
                new SqliteManualDependencyOverrideRepository(database)),
            new ProjectRepositoryStateReducer(),
            catalog,
            tracked,
            projects);
        return new TestSystem(service, projects, trackers, catalog);
    }

    private static string RunGit(string workingDirectory, params string[] arguments)
    {
        ProcessStartInfo start = new()
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(error);
        return output;
    }

    private static void DeleteGitDirectory(string? path)
    {
        if (path is null || !Directory.Exists(path)) return;
        foreach (string item in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(item, FileAttributes.Normal);
        Directory.Delete(path, recursive: true);
    }

    private sealed record TestSystem(
        GitBackedProjectService Service,
        SqliteProjectRepository Projects,
        SqliteTrackerRepository Trackers,
        SqliteProjectTrackerStore SqliteCatalog);

    private sealed class TemporaryGitRepository : IDisposable
    {
        public TemporaryGitRepository(bool configureIdentity = true)
        {
            Path = System.IO.Path.Combine(PathRoot, Guid.NewGuid().ToString("N"));
            RegistryPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Path)!, $"{System.IO.Path.GetFileName(Path)}-registry.json");
            Directory.CreateDirectory(Path);
            Run("init", "--initial-branch=main");
            if (configureIdentity)
            {
                Run("config", "user.name", "EntityTracker Tests");
                Run("config", "user.email", "entitytracker@example.invalid");
            }
            else
            {
                Run("config", "user.name", "");
                Run("config", "user.email", "");
            }
        }

        private static string PathRoot => System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "EntityTracker-GitBackedTests");
        public string Path { get; private set; }
        public string RegistryPath { get; }

        public string MoveToSibling()
        {
            string destination = Path + "-moved";
            Directory.Move(Path, destination);
            Path = destination;
            return destination;
        }

        public int CommitCount() => int.Parse(Lines("rev-list", "--count", "HEAD").Single(),
            System.Globalization.CultureInfo.InvariantCulture);

        public string[] Lines(params string[] arguments) => Run(arguments)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        public void RunGit(params string[] arguments) => _ = Run(arguments);

        private string Run(params string[] arguments)
        {
            ProcessStartInfo start = new()
            {
                FileName = "git",
                WorkingDirectory = Path,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using Process process = Process.Start(start)!;
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException(error);
            return output;
        }

        public void Dispose()
        {
            if (File.Exists(RegistryPath)) File.Delete(RegistryPath);
            if (!Directory.Exists(Path)) return;
            foreach (string item in Directory.EnumerateFileSystemEntries(Path, "*", SearchOption.AllDirectories))
                File.SetAttributes(item, FileAttributes.Normal);
            Directory.Delete(Path, recursive: true);
        }
    }
}

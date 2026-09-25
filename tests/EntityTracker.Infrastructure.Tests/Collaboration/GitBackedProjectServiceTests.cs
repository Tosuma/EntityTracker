using System.Diagnostics;

using EntityTracker.Application.Collaboration;
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
        ProjectRepositoryStatus linked = await system.Service.GetStatusAsync(project.Id);
        Assert.Equal(ProjectRepositoryStatusKind.GitClean, linked.Kind);

        ProjectMutationCoordinator coordinator = new(system.Service, system.Trackers);
        await coordinator.RenameProjectAsync(project.Id, "Git Project");

        Assert.Equal(2, repository.CommitCount());
        Assert.Equal("Git Project", (await system.Projects.GetAsync(project.Id))!.Name);
        string commitMessage = string.Join("\n", repository.Lines("log", "-1", "--format=%B"));
        Assert.Contains("Rename Project", commitMessage, StringComparison.Ordinal);
        Assert.Contains("EntityTracker-Operation-Id:", commitMessage, StringComparison.Ordinal);

        await system.SqliteCatalog.RenameProjectAsync(project.Id, "Corrupt cache value");
        await system.Service.RebuildCacheAsync(project.Id);

        Assert.Equal("Git Project", (await system.Projects.GetAsync(project.Id))!.Name);
        Assert.Equal(2, repository.CommitCount());
    }

    [Fact]
    public async Task DirtyManagedFile_BlocksMutationWithoutCommitOrCacheChange()
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
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.RenameProjectAsync(project.Id, "Must not persist"));

        Assert.Contains("clean", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, repository.CommitCount());
        Assert.Equal(project.Name, (await system.Projects.GetAsync(project.Id))!.Name);
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

            await File.WriteAllTextAsync(path, "{\"version\":2,\"repositories\":[]}");
            await Assert.ThrowsAsync<InvalidDataException>(() => registry.GetAllAsync());
            Assert.Equal("{\"version\":2,\"repositories\":[]}", await File.ReadAllTextAsync(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static TestSystem CreateSystem(SqliteDatabase database, string registryPath)
    {
        SqliteProjectRepository projects = new(database);
        SqliteTrackerRepository trackers = new(database);
        SqliteProjectTrackerStore catalog = new(database);
        SqliteTrackedStateStore tracked = new(database);
        ProjectRepositoryCodec codec = new();
        GitCommandClient git = new();
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

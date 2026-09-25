using System.Collections.Concurrent;

using EntityTracker.Application.Collaboration;
using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Synchronization;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Git;
using EntityTracker.Infrastructure.Persistence;
using EntityTracker.Infrastructure.RepositoryFormat;

namespace EntityTracker.Infrastructure.Collaboration;

public sealed class GitBackedProjectService(
    LocalRepositoryRegistry registry,
    GitRepositoryValidator validator,
    GitCommandClient git,
    ProjectRepositoryStore repositoryStore,
    ProjectRepositoryCodec codec,
    SqliteProjectStateStore projectStateStore,
    ProjectRepositoryStateReducer reducer,
    SqliteProjectTrackerStore sqliteCatalogStore,
    SqliteTrackedStateStore sqliteTrackedStore,
    IProjectRepository projectRepository,
    TimeProvider? timeProvider = null) : IProjectMutationBackend, IProjectRepositoryManager
{
    private readonly ConcurrentDictionary<ProjectId, SemaphoreSlim> _projectGates = [];
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<ProjectMutationResult> ApplyAsync(
        ProjectMutation mutation,
        CancellationToken cancellationToken = default)
    {
        LocalRepositoryRegistration? registration = await registry.GetAsync(mutation.ProjectId, cancellationToken);
        if (registration is null)
            return await ApplySqliteAsync(mutation, cancellationToken);

        SemaphoreSlim gate = _projectGates.GetOrAdd(mutation.ProjectId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            LinkedContext context = await RequireCleanContextAsync(registration, requireProjectedHead: true, cancellationToken);
            ProjectRepositoryState proposed = reducer.Apply(context.State, mutation);
            await CommitAndProjectAsync(registration, context.Head, proposed, mutation, cancellationToken);
            SchemaImportSummary? summary = mutation is ChangeTrackedStateMutation { ImportCompletion: { } completion }
                ? new SchemaImportSummary(mutation.OccurredAtUtc, completion)
                : null;
            return new ProjectMutationResult(summary);
        }
        finally { gate.Release(); }
    }

    public async Task EnsureHistoryBaselineAsync(
        TrackerId trackerId,
        IEnumerable<TrackedEntity> entities,
        ProgressSnapshotState snapshot,
        CancellationToken cancellationToken = default)
    {
        Tracker? tracker = await new SqliteTrackerRepository(sqliteTrackedStore.Database)
            .GetAsync(trackerId, cancellationToken);
        if (tracker is null) throw new InvalidOperationException("The tracker no longer exists.");
        if (await registry.GetAsync(tracker.ProjectId, cancellationToken) is not null) return;
        await sqliteTrackedStore.EnsureHistoryBaselineAsync(trackerId, entities, snapshot, cancellationToken);
    }

    public Task<SchemaImportSummary?> GetLatestImportAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken = default) =>
        sqliteTrackedStore.GetLatestImportAsync(trackerId, cancellationToken);

    public async Task<IReadOnlyList<ProjectRepositoryStatus>> GetStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Project> projects = await projectRepository.GetAllAsync(cancellationToken);
        IReadOnlyList<LocalRepositoryRegistration> registrations = await registry.GetAllAsync(cancellationToken);
        Dictionary<ProjectId, LocalRepositoryRegistration> byProject = registrations.ToDictionary(item => item.ProjectId);
        List<ProjectRepositoryStatus> statuses = [];
        foreach (Project project in projects)
        {
            statuses.Add(byProject.TryGetValue(project.Id, out LocalRepositoryRegistration? registration)
                ? await InspectAsync(registration, cancellationToken)
                : new ProjectRepositoryStatus(project.Id, ProjectRepositoryStatusKind.SQLiteOnly));
        }
        return statuses;
    }

    public async Task<ProjectRepositoryStatus> GetStatusAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        LocalRepositoryRegistration? registration = await registry.GetAsync(projectId, cancellationToken);
        return registration is null
            ? new ProjectRepositoryStatus(projectId, ProjectRepositoryStatusKind.SQLiteOnly)
            : await InspectAsync(registration, cancellationToken);
    }

    public async Task LinkAsync(ProjectId projectId, string repositoryPath, CancellationToken cancellationToken = default)
    {
        Project project = await projectRepository.GetAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("The Project no longer exists.");
        if (await registry.GetAsync(projectId, cancellationToken) is not null)
            throw new InvalidOperationException("The Project is already Git-backed.");

        string path = NormalizePath(repositoryPath);
        await EnsurePathAvailableAsync(projectId, path, cancellationToken);
        GitRepositoryValidationResult validation = await validator.ValidateAsync(path, cancellationToken);
        EnsureValid(validation);
        GitResult<IReadOnlyList<string>> tracked = await git.GetTrackedPathsAsync(path, cancellationToken);
        EnsureSuccess(tracked);
        if (tracked.Value!.Count != 0)
            throw new InvalidOperationException("Link repository requires a repository with no tracked content.");
        GitResult<string> branch = await git.GetCurrentBranchAsync(path, cancellationToken);
        EnsureSuccess(branch);
        GitResult<GitHead> beforeHeadResult = await git.GetHeadAsync(path, cancellationToken);
        EnsureSuccess(beforeHeadResult);
        GitHead beforeHead = beforeHeadResult.Value!;

        ProjectRepositoryState exported = await projectStateStore.ExportAsync(projectId, cancellationToken);
        OperationId operationId = OperationId.New();
        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();
        RepositoryOperation linkOperation = new(operationId, RepositoryOperationKind.RepositoryLinked,
            now, [project.Id], exported.Trackers.Select(item => item.Tracker.Id).ToArray(),
            exported.Trackers.SelectMany(item => item.Entities).Select(item => item.Entity.Id).ToArray(), [], null, []);
        ProjectRepositoryState linked = exported with
        {
            Operations = exported.Operations.Append(linkOperation).ToArray()
        };
        LocalRepositoryRegistration pending = new(projectId, path, branch.Value!, beforeHead.CommitId);
        await registry.UpsertAsync(pending, cancellationToken);
        try
        {
            ProjectMutation marker = new LinkMarkerMutation(projectId, operationId, now);
            await CommitAndProjectAsync(pending, beforeHead, linked, marker, cancellationToken);
        }
        catch
        {
            GitResult<GitHead> observed = await git.GetHeadAsync(path, CancellationToken.None);
            if (observed.IsSuccess && observed.Value?.CommitId == beforeHead.CommitId)
                await registry.RemoveAsync(projectId, CancellationToken.None);
            throw;
        }
    }

    public async Task<ProjectId> OpenAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        string path = NormalizePath(repositoryPath);
        GitRepositoryValidationResult validation = await validator.ValidateAsync(path, cancellationToken);
        EnsureValid(validation);
        await EnsureOnlyManagedTrackedPathsAsync(path, cancellationToken);
        GitResult<string> branch = await git.GetCurrentBranchAsync(path, cancellationToken);
        GitResult<GitHead> head = await git.GetHeadAsync(path, cancellationToken);
        EnsureSuccess(branch); EnsureSuccess(head);
        if (head.Value is not { Exists: true, CommitId: not null })
            throw new InvalidOperationException("An EntityTracker repository must have a committed HEAD.");
        ProjectRepositoryState state = await repositoryStore.LoadAsync(path, cancellationToken);
        if (state.Tombstones.Any(item => item.Kind == RepositoryTombstoneKind.Project))
            throw new InvalidOperationException("This EntityTracker Project has been permanently deleted.");
        IReadOnlyList<LocalRepositoryRegistration> registrations = await registry.GetAllAsync(cancellationToken);
        if (registrations.Any(item => item.ProjectId == state.Project.Id))
            throw new InvalidOperationException("The repository Project ID is already registered.");
        if (registrations.Any(item => string.Equals(item.RepositoryPath, path, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The repository folder is already registered.");
        if (await projectRepository.GetAsync(state.Project.Id, cancellationToken) is not null)
            throw new InvalidOperationException("The repository Project ID already exists in SQLite.");
        if (await projectRepository.IsNameReservedAsync(state.Project.Name, cancellationToken: cancellationToken))
            throw new InvalidOperationException("The repository Project name is already reserved.");

        LocalRepositoryRegistration pending = new(state.Project.Id, path, branch.Value!, null);
        await registry.UpsertAsync(pending, cancellationToken);
        try
        {
            await projectStateStore.ReplaceAsync(state, cancellationToken);
            await registry.UpsertAsync(pending with { LastProjectedCommit = head.Value.CommitId }, cancellationToken);
            return state.Project.Id;
        }
        catch
        {
            // Replacement is transactional. If no cache row exists, leave Open retryable instead
            // of retaining a registration that Portfolio cannot surface.
            if (await projectRepository.GetAsync(state.Project.Id, CancellationToken.None) is null)
                await registry.RemoveAsync(state.Project.Id, CancellationToken.None);
            throw;
        }
    }

    public async Task LocateAsync(ProjectId projectId, string repositoryPath, CancellationToken cancellationToken = default)
    {
        LocalRepositoryRegistration registration = await registry.GetAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("The Project is not registered as Git-backed.");
        string path = NormalizePath(repositoryPath);
        await EnsurePathAvailableAsync(projectId, path, cancellationToken);
        GitRepositoryValidationResult validation = await validator.ValidateAsync(path, cancellationToken);
        EnsureValid(validation);
        await EnsureOnlyManagedTrackedPathsAsync(path, cancellationToken);
        GitResult<string> branch = await git.GetCurrentBranchAsync(path, cancellationToken);
        GitResult<GitHead> head = await git.GetHeadAsync(path, cancellationToken);
        EnsureSuccess(branch); EnsureSuccess(head);
        if (!string.Equals(branch.Value, registration.ManagedBranch, StringComparison.Ordinal) ||
            head.Value?.CommitId != registration.LastProjectedCommit)
            throw new InvalidOperationException("The located repository is not on the registered branch and projected commit.");
        ProjectRepositoryState state = await repositoryStore.LoadAsync(path, cancellationToken);
        if (state.Project.Id != projectId)
            throw new InvalidOperationException("The located repository belongs to another Project.");
        await registry.UpsertAsync(registration with { RepositoryPath = path }, cancellationToken);
    }

    public async Task RebuildCacheAsync(ProjectId projectId, CancellationToken cancellationToken = default)
    {
        LocalRepositoryRegistration registration = await registry.GetAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("The Project is not registered as Git-backed.");
        SemaphoreSlim gate = _projectGates.GetOrAdd(projectId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            LinkedContext context = await RequireCleanContextAsync(registration, requireProjectedHead: false, cancellationToken);
            await projectStateStore.ReplaceAsync(context.State, cancellationToken);
            await registry.UpsertAsync(registration with { LastProjectedCommit = context.Head.CommitId }, cancellationToken);
        }
        finally { gate.Release(); }
    }

    private async Task<ProjectMutationResult> ApplySqliteAsync(ProjectMutation mutation, CancellationToken cancellationToken)
    {
        switch (mutation)
        {
            case CreateProjectMutation value:
                await sqliteCatalogStore.CreateProjectAsync(value.Project, cancellationToken); break;
            case RenameProjectMutation value:
                await sqliteCatalogStore.RenameProjectAsync(value.ProjectId, value.Name, cancellationToken); break;
            case SetProjectLifecycleMutation value:
                await sqliteCatalogStore.SetProjectLifecycleAsync(value.ProjectId, value.LifecycleState, cancellationToken); break;
            case PurgeProjectMutation value:
                await sqliteCatalogStore.PurgeProjectAsync(value.ProjectId, cancellationToken); break;
            case CreateTrackerMutation value:
                await sqliteCatalogStore.CreateTrackerAsync(value.Creation, cancellationToken); break;
            case RenameTrackerMutation value:
                await sqliteCatalogStore.RenameTrackerAsync(value.TrackerId, value.Name, cancellationToken); break;
            case SetTrackerLifecycleMutation value:
                await sqliteCatalogStore.SetTrackerLifecycleAsync(value.TrackerId, value.LifecycleState, cancellationToken); break;
            case PurgeTrackerMutation value:
                await sqliteCatalogStore.PurgeTrackerAsync(value.TrackerId, cancellationToken); break;
            case ChangeTrackedStateMutation { ImportCompletion: { } completion } value:
                SchemaImportSummary summary = await sqliteTrackedStore.ApplyAsync(value.TrackerId, value.ChangeSet, completion, cancellationToken);
                return new ProjectMutationResult(summary);
            case ChangeTrackedStateMutation value:
                await sqliteTrackedStore.ApplyAsync(value.TrackerId, value.ChangeSet, cancellationToken); break;
            default: throw new InvalidOperationException("The Project mutation is not supported.");
        }
        return new ProjectMutationResult();
    }

    private async Task CommitAndProjectAsync(LocalRepositoryRegistration registration, GitHead beforeHead,
        ProjectRepositoryState proposed, ProjectMutation mutation, CancellationToken cancellationToken)
    {
        RepositoryFileSnapshot snapshot = await repositoryStore.CaptureAsync(registration.RepositoryPath, cancellationToken);
        string[] managedPaths = snapshot.Files.Keys.Concat(codec.Serialize(proposed).Keys)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        bool committed = false;
        try
        {
            await repositoryStore.WriteAsync(registration.RepositoryPath, proposed, cancellationToken);
            GitResult<bool> staged = await git.StageAsync(registration.RepositoryPath, managedPaths, cancellationToken);
            EnsureSuccess(staged);
            GitResult<bool> commit = await git.CommitAsync(registration.RepositoryPath,
                CommitSubject(mutation.Kind), mutation.OperationId, cancellationToken);
            if (!commit.IsSuccess)
            {
                GitResult<GitHead> observed = await git.GetHeadAsync(registration.RepositoryPath, CancellationToken.None);
                if (observed.IsSuccess && observed.Value is { Exists: true, CommitId: not null } &&
                    observed.Value.CommitId != beforeHead.CommitId)
                {
                    GitResult<string> message = await git.GetHeadMessageAsync(registration.RepositoryPath, CancellationToken.None);
                    committed = message.IsSuccess && message.Value!.Contains(
                        mutation.OperationId.Value.ToString("D"), StringComparison.OrdinalIgnoreCase);
                }
                if (!committed) throw new InvalidOperationException(commit.Diagnostic);
            }
            else committed = true;

            GitResult<GitHead> afterHead = await git.GetHeadAsync(registration.RepositoryPath, CancellationToken.None);
            EnsureSuccess(afterHead);
            if (afterHead.Value is not { Exists: true, CommitId: not null })
                throw new InvalidOperationException("Git did not create the expected commit.");
            try
            {
                await projectStateStore.ReplaceAsync(proposed, cancellationToken);
            }
            catch
            {
                try { await projectStateStore.ReplaceAsync(proposed, CancellationToken.None); }
                catch { throw new InvalidOperationException("The authoritative Git commit succeeded, but the SQLite cache is stale. Rebuild the Project cache before continuing."); }
            }
            await registry.UpsertAsync(registration with { LastProjectedCommit = afterHead.Value.CommitId }, CancellationToken.None);
        }
        catch
        {
            if (!committed)
            {
                await git.RestoreManagedPathsAsync(registration.RepositoryPath, managedPaths, beforeHead.Exists, CancellationToken.None);
                await repositoryStore.RestoreAsync(registration.RepositoryPath, snapshot, CancellationToken.None);
            }
            throw;
        }
    }

    private async Task<LinkedContext> RequireCleanContextAsync(LocalRepositoryRegistration registration,
        bool requireProjectedHead, CancellationToken cancellationToken)
    {
        GitRepositoryValidationResult validation = await validator.ValidateAsync(registration.RepositoryPath, cancellationToken);
        EnsureValid(validation);
        await EnsureOnlyManagedTrackedPathsAsync(registration.RepositoryPath, cancellationToken);
        GitResult<string> branch = await git.GetCurrentBranchAsync(registration.RepositoryPath, cancellationToken);
        GitResult<GitHead> head = await git.GetHeadAsync(registration.RepositoryPath, cancellationToken);
        EnsureSuccess(branch); EnsureSuccess(head);
        if (!string.Equals(branch.Value, registration.ManagedBranch, StringComparison.Ordinal))
            throw new InvalidOperationException("The repository is checked out on a different branch.");
        if (head.Value is not { Exists: true, CommitId: not null })
            throw new InvalidOperationException("The linked repository has no committed HEAD.");
        if (requireProjectedHead && head.Value.CommitId != registration.LastProjectedCommit)
            throw new InvalidOperationException("The repository HEAD differs from the projected commit. Rebuild the cache before continuing.");
        ProjectRepositoryState state = await repositoryStore.LoadAsync(registration.RepositoryPath, cancellationToken);
        if (state.Project.Id != registration.ProjectId)
            throw new InvalidOperationException("The repository manifest belongs to another Project.");
        return new LinkedContext(state, head.Value);
    }

    private async Task<ProjectRepositoryStatus> InspectAsync(LocalRepositoryRegistration registration,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(registration.RepositoryPath))
            return Status(ProjectRepositoryStatusKind.Unavailable, "The repository folder is unavailable.");
        try
        {
            GitRepositoryValidationResult validation = await validator.ValidateAsync(registration.RepositoryPath, cancellationToken);
            if (!validation.IsValid) return Status(ProjectRepositoryStatusKind.Blocked, string.Join(" ", validation.Errors));
            await EnsureOnlyManagedTrackedPathsAsync(registration.RepositoryPath, cancellationToken);
            GitResult<string> branch = await git.GetCurrentBranchAsync(registration.RepositoryPath, cancellationToken);
            GitResult<GitHead> head = await git.GetHeadAsync(registration.RepositoryPath, cancellationToken);
            EnsureSuccess(branch); EnsureSuccess(head);
            if (!string.Equals(branch.Value, registration.ManagedBranch, StringComparison.Ordinal))
                return Status(ProjectRepositoryStatusKind.Blocked, "The repository is checked out on a different branch.");
            if (head.Value?.CommitId != registration.LastProjectedCommit)
                return Status(ProjectRepositoryStatusKind.StaleCache, "Repository HEAD has not been projected into SQLite.");
            ProjectRepositoryState state = await repositoryStore.LoadAsync(registration.RepositoryPath, cancellationToken);
            if (state.Project.Id != registration.ProjectId)
                return Status(ProjectRepositoryStatusKind.Blocked, "The repository manifest belongs to another Project.");
            return Status(ProjectRepositoryStatusKind.GitClean, null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return Status(ProjectRepositoryStatusKind.Blocked, exception.Message);
        }

        ProjectRepositoryStatus Status(ProjectRepositoryStatusKind kind, string? diagnostic) =>
            new(registration.ProjectId, kind, registration.RepositoryPath, registration.ManagedBranch, diagnostic);
    }

    private async Task EnsureOnlyManagedTrackedPathsAsync(string path, CancellationToken cancellationToken)
    {
        GitResult<IReadOnlyList<string>> tracked = await git.GetTrackedPathsAsync(path, cancellationToken);
        EnsureSuccess(tracked);
        if (tracked.Value!.Any(item => !IsManagedPath(item.Replace('\\', '/'))))
            throw new InvalidOperationException("The repository contains tracked content not owned by EntityTracker.");
    }

    private async Task EnsurePathAvailableAsync(ProjectId projectId, string path, CancellationToken cancellationToken)
    {
        if ((await registry.GetAllAsync(cancellationToken)).Any(item => item.ProjectId != projectId &&
            string.Equals(item.RepositoryPath, path, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The repository folder is already registered to another Project.");
    }

    private static bool IsManagedPath(string path) => path == ProjectRepositoryCodec.ManifestPath ||
        path.StartsWith("trackers/", StringComparison.Ordinal) ||
        path.StartsWith("operations/", StringComparison.Ordinal) ||
        path.StartsWith("tombstones/", StringComparison.Ordinal);

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static void EnsureValid(GitRepositoryValidationResult result)
    {
        if (!result.IsValid) throw new InvalidOperationException(string.Join(Environment.NewLine, result.Errors));
    }

    private static void EnsureSuccess<T>(GitResult<T> result)
    {
        if (!result.IsSuccess || result.Value is null) throw new InvalidOperationException(result.Diagnostic);
    }

    private static string CommitSubject(RepositoryOperationKind kind) => kind switch
    {
        RepositoryOperationKind.RepositoryLinked => "Link EntityTracker Project repository",
        RepositoryOperationKind.ProjectRenamed => "Rename Project",
        RepositoryOperationKind.ProjectRecycled => "Recycle Project",
        RepositoryOperationKind.ProjectRestored => "Restore Project",
        RepositoryOperationKind.ProjectPurged => "Permanently delete Project",
        RepositoryOperationKind.TrackerCreated => "Create Tracker",
        RepositoryOperationKind.TrackerCopied => "Copy Tracker",
        RepositoryOperationKind.TrackerRenamed => "Rename Tracker",
        RepositoryOperationKind.TrackerRecycled => "Recycle Tracker",
        RepositoryOperationKind.TrackerRestored => "Restore Tracker",
        RepositoryOperationKind.TrackerPurged => "Permanently delete Tracker",
        RepositoryOperationKind.SchemaImported => "Import schema",
        RepositoryOperationKind.EntityCreated => "Create entity",
        RepositoryOperationKind.EntityArchived => "Archive entity",
        RepositoryOperationKind.EntityRestored => "Restore entity",
        RepositoryOperationKind.DependencyEdited => "Edit dependencies",
        RepositoryOperationKind.StatusUpdated => "Update entity status",
        RepositoryOperationKind.BulkStatusUpdated => "Update entity statuses",
        _ => "Edit entity"
    };

    private sealed record LinkedContext(ProjectRepositoryState State, GitHead Head);
    private sealed record LinkMarkerMutation(ProjectId ProjectId, OperationId OperationId, DateTimeOffset OccurredAtUtc)
        : ProjectMutation(ProjectId, OperationId, RepositoryOperationKind.RepositoryLinked, OccurredAtUtc);
}

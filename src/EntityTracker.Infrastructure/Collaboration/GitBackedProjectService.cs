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
    TimeProvider? timeProvider = null) : IProjectMutationBackend, IProjectRepositoryManager, IProjectSynchronizationService
{
    private readonly ConcurrentDictionary<ProjectId, SemaphoreSlim> _projectGates = [];
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly SqliteProjectOperationOutbox _outbox = new(sqliteTrackedStore.Database);

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
            return await ApplySqliteAsync(mutation, enqueueForGit: true, cancellationToken: cancellationToken);
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

    public async Task<ProjectRepositoryStatus> GetCachedStatusAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        LocalRepositoryRegistration? registration = await registry.GetAsync(projectId, cancellationToken);
        int pendingCount = registration is null
            ? 0
            : await _outbox.CountPendingAsync(projectId, cancellationToken);
        return registration is null
            ? new ProjectRepositoryStatus(projectId, ProjectRepositoryStatusKind.SQLiteOnly)
            : Status(
                registration,
                ProjectRepositoryStatusKind.GitRegistered,
                pendingCount == 0 ? ProjectSyncState.UpToDate : ProjectSyncState.NeedsSync,
                pendingCount == 0
                    ? "Using the SQLite working copy. Git is accessed only by explicit repository actions."
                    : $"{pendingCount} local change{(pendingCount == 1 ? " is" : "s are")} waiting for Sync.",
                pendingCount: pendingCount);
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
            if (await _outbox.CountPendingAsync(projectId, cancellationToken) > 0)
                throw new InvalidOperationException("Sync pending local changes before rebuilding the SQLite working copy.");
            LinkedContext context = await RequireCleanContextAsync(registration, requireProjectedHead: false, cancellationToken);
            await projectStateStore.ReplaceAsync(context.State, cancellationToken);
            await registry.UpsertAsync(registration with { LastProjectedCommit = context.Head.CommitId }, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task<ProjectSyncResult> SyncAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        LocalRepositoryRegistration? registration = await registry.GetAsync(projectId, cancellationToken);
        if (registration is null)
        {
            ProjectRepositoryStatus sqlite = new(projectId, ProjectRepositoryStatusKind.SQLiteOnly);
            return new(ProjectSyncOutcome.Failed, ProjectSyncFailureKind.RepositoryBlocked,
                "SQLite-only Projects do not have a Git upstream.", sqlite);
        }

        SemaphoreSlim gate = _projectGates.GetOrAdd(projectId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            LinkedContext context;
            int localCommitCount = 0;
            try
            {
                context = await RequireCleanContextAsync(registration, requireProjectedHead: true, cancellationToken);
                int pendingCount = await _outbox.CountPendingAsync(projectId, cancellationToken);
                if (pendingCount > 0)
                {
                    GitResult<GitUpstreamDetails> probeResult = await git.GetUpstreamDetailsAsync(
                        registration.RepositoryPath, registration.ManagedBranch, cancellationToken);
                    if (!probeResult.IsSuccess || probeResult.Value is null)
                        return Failure(registration, probeResult.FailureKind, probeResult.Diagnostic);
                    GitUpstreamDetails probe = probeResult.Value;
                    if (probe.IsConfigured)
                    {
                        GitResult<bool> probeFetch = await git.FetchAsync(
                            registration.RepositoryPath,
                            probe.RemoteName!,
                            probe.RemoteBranch!,
                            probe.TrackingReference!,
                            cancellationToken);
                        if (!probeFetch.IsSuccess)
                            return probeFetch.FailureKind == GitFailureKind.Cancelled
                                ? Cancelled(registration, probe)
                                : Failure(registration, probeFetch.FailureKind, probeFetch.Diagnostic, probe);
                        GitResult<GitUpstreamDetails> refreshedProbe = await git.GetUpstreamDetailsAsync(
                            registration.RepositoryPath, registration.ManagedBranch, cancellationToken);
                        if (!refreshedProbe.IsSuccess || refreshedProbe.Value is not
                            { IsConfigured: true, CommitId: not null } remote)
                        {
                            return Failure(
                                registration,
                                refreshedProbe.IsSuccess ? GitFailureKind.InvalidRepository : refreshedProbe.FailureKind,
                                refreshedProbe.IsSuccess
                                    ? "The configured upstream branch does not exist after fetch."
                                    : refreshedProbe.Diagnostic,
                                probe);
                        }
                        GitResult<GitAheadBehind> pendingGraph = await git.GetAheadBehindAsync(
                            registration.RepositoryPath,
                            context.Head.CommitId!,
                            remote.CommitId,
                            cancellationToken);
                        if (!pendingGraph.IsSuccess || pendingGraph.Value is null)
                            return Failure(registration, pendingGraph.FailureKind, pendingGraph.Diagnostic, remote);
                        if (pendingGraph.Value.Behind > 0)
                        {
                            ProjectRepositoryStatus merge = Status(
                                registration,
                                ProjectRepositoryStatusKind.GitClean,
                                ProjectSyncState.MergeRequired,
                                "Upstream changes must be semantically merged with pending SQLite operations.",
                                remote,
                                pendingGraph.Value.Ahead,
                                pendingGraph.Value.Behind,
                                pendingCount);
                            return new ProjectSyncResult(
                                ProjectSyncOutcome.MergeRequired,
                                ProjectSyncFailureKind.None,
                                "Upstream changes and pending local operations require RS-04 semantic merge; no local commits or cache data were changed.",
                                merge);
                        }
                    }
                }
                PendingCommitResult committed = await CommitPendingAsync(
                    registration, context, cancellationToken);
                registration = committed.Registration;
                context = committed.Context;
                localCommitCount = committed.CommitCount;
            }
            catch (OperationCanceledException)
            {
                return Cancelled(registration);
            }
            catch (Exception exception) when (IsRepositoryException(exception))
            {
                ProjectRepositoryStatus blocked = Status(
                    registration,
                    ProjectRepositoryStatusKind.Blocked,
                    ProjectSyncState.NeedsSync,
                    exception.Message);
                return new(ProjectSyncOutcome.Failed, ProjectSyncFailureKind.RepositoryBlocked,
                    exception.Message, blocked);
            }

            GitResult<GitUpstreamDetails> upstreamResult = await git.GetUpstreamDetailsAsync(
                registration.RepositoryPath, registration.ManagedBranch, cancellationToken);
            if (!upstreamResult.IsSuccess || upstreamResult.Value is null)
                return WithLocalCommits(
                    Failure(registration, upstreamResult.FailureKind, upstreamResult.Diagnostic),
                    localCommitCount);
            GitUpstreamDetails upstream = upstreamResult.Value;
            if (!upstream.IsConfigured)
            {
                ProjectRepositoryStatus noUpstream = Status(
                    registration,
                    ProjectRepositoryStatusKind.GitClean,
                    ProjectSyncState.NoUpstream,
                    "No upstream is configured for the managed branch.");
                return localCommitCount > 0
                    ? new(ProjectSyncOutcome.LocalCommitted, ProjectSyncFailureKind.None,
                        localCommitCount == 1
                            ? "Created 1 local Git commit. No upstream is configured."
                            : $"Created {localCommitCount} local Git commits. No upstream is configured.",
                        noUpstream, localCommitCount)
                    : new(ProjectSyncOutcome.UpToDate, ProjectSyncFailureKind.None,
                        "The local Git repository is up to date. No upstream is configured.", noUpstream);
            }

            GitResult<bool> fetch = await git.FetchAsync(
                registration.RepositoryPath,
                upstream.RemoteName!,
                upstream.RemoteBranch!,
                upstream.TrackingReference!,
                cancellationToken);
            if (!fetch.IsSuccess)
                return WithLocalCommits(fetch.FailureKind == GitFailureKind.Cancelled
                    ? Cancelled(registration, upstream)
                    : Failure(registration, fetch.FailureKind, fetch.Diagnostic, upstream),
                    localCommitCount);

            registration = registration with
            {
                LastSuccessfulFetchAtUtc = _timeProvider.GetUtcNow().ToUniversalTime()
            };
            try
            {
                await registry.UpsertAsync(registration, CancellationToken.None);
            }
            catch (Exception exception) when (IsRepositoryException(exception))
            {
                return WithLocalCommits(Failure(registration, GitFailureKind.CommandFailed,
                    $"Fetch succeeded, but local synchronization metadata could not be saved: {exception.Message}",
                    upstream), localCommitCount);
            }

            GitResult<GitUpstreamDetails> refreshedResult = await git.GetUpstreamDetailsAsync(
                registration.RepositoryPath, registration.ManagedBranch, cancellationToken);
            if (!refreshedResult.IsSuccess || refreshedResult.Value is not
                { IsConfigured: true, CommitId: not null } refreshed)
            {
                return WithLocalCommits(Failure(registration,
                    refreshedResult.IsSuccess ? GitFailureKind.InvalidRepository : refreshedResult.FailureKind,
                    refreshedResult.IsSuccess
                        ? "The configured upstream branch does not exist after fetch."
                        : refreshedResult.Diagnostic,
                    upstream), localCommitCount);
            }
            if (!SameUpstream(upstream, refreshed))
                return WithLocalCommits(Failure(registration, GitFailureKind.InvalidRepository,
                    "The configured upstream changed while synchronization was running.", refreshed), localCommitCount);

            string localCommit = context.Head.CommitId!;
            string remoteCommit = refreshed.CommitId;
            GitResult<GitAheadBehind> graph = await git.GetAheadBehindAsync(
                registration.RepositoryPath, localCommit, remoteCommit, cancellationToken);
            if (!graph.IsSuccess || graph.Value is null)
                return WithLocalCommits(Failure(registration, graph.FailureKind, graph.Diagnostic, refreshed), localCommitCount);

            if (graph.Value is { Ahead: 0, Behind: 0 })
            {
                ProjectRepositoryStatus current = Status(registration, ProjectRepositoryStatusKind.GitClean,
                    ProjectSyncState.UpToDate, null, refreshed, 0, 0);
                return new(ProjectSyncOutcome.UpToDate, ProjectSyncFailureKind.None,
                    localCommitCount == 0 ? "The Project is up to date." : "Local changes were committed; the Project is up to date.",
                    current, localCommitCount);
            }

            if (graph.Value is { Ahead: > 0, Behind: 0 })
                return (await PushAsync(registration, context, refreshed, graph.Value.Ahead, cancellationToken)) with
                {
                    LocalCommitCount = localCommitCount
                };

            if (graph.Value is { Ahead: 0, Behind: > 0 })
                return await FastForwardAsync(registration, context, refreshed, graph.Value.Behind, cancellationToken);

            ProjectRepositoryStatus diverged = Status(registration, ProjectRepositoryStatusKind.GitClean,
                ProjectSyncState.MergeRequired,
                "Local and upstream commits have diverged. Semantic merge is required.",
                refreshed, graph.Value.Ahead, graph.Value.Behind);
            return new(ProjectSyncOutcome.MergeRequired, ProjectSyncFailureKind.None,
                "Local and upstream commits have diverged. RS-04 semantic merge is required; no files or cache data were changed.",
                diverged, localCommitCount);
        }
        catch (OperationCanceledException)
        {
            return Cancelled(registration);
        }
        finally { gate.Release(); }
    }

    private async Task<PendingCommitResult> CommitPendingAsync(
        LocalRepositoryRegistration registration,
        LinkedContext context,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PendingProjectMutation> pending = await _outbox.GetPendingAsync(
            registration.ProjectId, cancellationToken);
        int committedCount = 0;
        foreach (PendingProjectMutation item in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context.State.Operations.Any(operation => operation.Id == item.Mutation.OperationId))
            {
                await _outbox.MarkCommittedAsync(item.Sequence, context.Head.CommitId!, CancellationToken.None);
                continue;
            }

            ProjectRepositoryState proposed = reducer.Apply(context.State, item.Mutation);
            RepositoryFileSnapshot snapshot = await repositoryStore.CaptureAsync(
                registration.RepositoryPath, cancellationToken);
            string[] managedPaths = snapshot.Files.Keys.Concat(codec.Serialize(proposed).Keys)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            bool committed = false;
            try
            {
                await repositoryStore.WriteAsync(registration.RepositoryPath, proposed, cancellationToken);
                EnsureSuccess(await git.StageAsync(registration.RepositoryPath, managedPaths, cancellationToken));
                GitResult<bool> commit = await git.CommitAsync(
                    registration.RepositoryPath,
                    CommitSubject(item.Mutation.Kind),
                    item.Mutation.OperationId,
                    cancellationToken);
                EnsureSuccess(commit);
                committed = true;
                GitResult<GitHead> afterHead = await git.GetHeadAsync(
                    registration.RepositoryPath, CancellationToken.None);
                EnsureSuccess(afterHead);
                if (afterHead.Value is not { Exists: true, CommitId: not null } head)
                    throw new InvalidOperationException("Git did not create the expected commit.");

                registration = registration with { LastProjectedCommit = head.CommitId };
                await registry.UpsertAsync(registration, CancellationToken.None);
                await _outbox.MarkCommittedAsync(item.Sequence, head.CommitId, CancellationToken.None);
                context = new LinkedContext(proposed, head);
                committedCount++;
            }
            catch
            {
                if (!committed)
                {
                    await git.RestoreManagedPathsAsync(
                        registration.RepositoryPath, managedPaths, context.Head.Exists, CancellationToken.None);
                    await repositoryStore.RestoreAsync(
                        registration.RepositoryPath, snapshot, CancellationToken.None);
                }
                throw;
            }
        }
        return new PendingCommitResult(registration, context, committedCount);
    }

    private static ProjectSyncResult WithLocalCommits(ProjectSyncResult result, int count) =>
        count == 0
            ? result
            : result with
            {
                LocalCommitCount = count,
                Message = $"Created {count} local Git commit{(count == 1 ? string.Empty : "s")}. {result.Message}"
            };

    private async Task<ProjectSyncResult> PushAsync(
        LocalRepositoryRegistration registration,
        LinkedContext expectedContext,
        GitUpstreamDetails expectedUpstream,
        int ahead,
        CancellationToken cancellationToken)
    {
        LinkedContext current;
        try
        {
            current = await RequireCleanContextAsync(registration, requireProjectedHead: true, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Cancelled(registration, expectedUpstream);
        }
        catch (Exception exception) when (IsRepositoryException(exception))
        {
            return Failure(registration, GitFailureKind.InvalidRepository, exception.Message, expectedUpstream);
        }
        GitResult<GitUpstreamDetails> currentUpstream = await git.GetUpstreamDetailsAsync(
            registration.RepositoryPath, registration.ManagedBranch, cancellationToken);
        if (current.Head.CommitId != expectedContext.Head.CommitId ||
            !currentUpstream.IsSuccess || currentUpstream.Value is null ||
            !SameUpstream(expectedUpstream, currentUpstream.Value) ||
            currentUpstream.Value.CommitId != expectedUpstream.CommitId)
        {
            return Failure(registration, GitFailureKind.InvalidRepository,
                "The repository or upstream changed while synchronization was running.", expectedUpstream);
        }

        GitResult<bool> push = await git.PushAsync(
            registration.RepositoryPath,
            expectedUpstream.RemoteName!,
            registration.ManagedBranch,
            expectedUpstream.RemoteBranch!,
            cancellationToken);
        if (!push.IsSuccess)
        {
            ProjectSyncResult failed = push.FailureKind == GitFailureKind.Cancelled
                ? Cancelled(registration, expectedUpstream)
                : Failure(registration, push.FailureKind, push.Diagnostic, expectedUpstream, ahead, 0);
            if (push.FailureKind == GitFailureKind.PushRejected)
            {
                failed = failed with
                {
                    Outcome = ProjectSyncOutcome.NeedsSync,
                    Message = "The upstream moved after fetch. Local commits are intact; run Sync again.",
                    Status = failed.Status with
                    {
                        SyncState = ProjectSyncState.NeedsSync,
                        Diagnostic = "The push was rejected because the upstream moved."
                    }
                };
            }
            return failed;
        }

        registration = registration with
        {
            LastSuccessfulPushAtUtc = _timeProvider.GetUtcNow().ToUniversalTime()
        };
        try
        {
            await registry.UpsertAsync(registration, CancellationToken.None);
        }
        catch (Exception exception) when (IsRepositoryException(exception))
        {
            ProjectRepositoryStatus completed = Status(registration, ProjectRepositoryStatusKind.GitClean,
                ProjectSyncState.UpToDate,
                "The push succeeded, but its completion time could not be saved locally.",
                expectedUpstream, 0, 0);
            return new(ProjectSyncOutcome.Pushed, ProjectSyncFailureKind.RepositoryBlocked,
                $"The push succeeded, but local synchronization metadata could not be saved: {exception.Message}",
                completed);
        }
        ProjectRepositoryStatus status = Status(registration, ProjectRepositoryStatusKind.GitClean,
            ProjectSyncState.UpToDate, null, expectedUpstream, 0, 0);
        return new(ProjectSyncOutcome.Pushed, ProjectSyncFailureKind.None,
            ahead == 1 ? "Pushed 1 local commit." : $"Pushed {ahead} local commits.", status);
    }

    private async Task<ProjectSyncResult> FastForwardAsync(
        LocalRepositoryRegistration registration,
        LinkedContext expectedContext,
        GitUpstreamDetails upstream,
        int behind,
        CancellationToken cancellationToken)
    {
        GitResult<GitTreeSnapshot> tree = await git.ReadTreeAsync(
            registration.RepositoryPath, upstream.CommitId!, cancellationToken);
        if (!tree.IsSuccess || tree.Value is null)
            return Failure(registration, tree.FailureKind, tree.Diagnostic, upstream, 0, behind);

        ProjectRepositoryState candidate;
        try
        {
            candidate = codec.Deserialize(tree.Value.Files);
            ValidateCanonicalSnapshot(candidate, tree.Value.Files);
            if (candidate.Project.Id != registration.ProjectId)
                throw new InvalidDataException("The fetched repository belongs to another Project.");
            if (!candidate.Tombstones.Any(item => item.Kind == RepositoryTombstoneKind.Project) &&
                await projectRepository.IsNameReservedAsync(
                    candidate.Project.Name,
                    excludingProjectId: candidate.Project.Id,
                    cancellationToken))
            {
                throw new InvalidDataException(
                    "The fetched Project name is already reserved by another local Project.");
            }
            RepositoryFileSnapshot current = await repositoryStore.CaptureAsync(
                registration.RepositoryPath, cancellationToken);
            ValidateAppendOnlySuccessor(current.Files, tree.Value.Files);
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
        {
            ProjectSyncFailureKind kind = exception.Message.Contains("version", StringComparison.OrdinalIgnoreCase)
                ? ProjectSyncFailureKind.UnsupportedSchema
                : ProjectSyncFailureKind.InvalidRemote;
            ProjectRepositoryStatus invalid = Status(registration, ProjectRepositoryStatusKind.GitClean,
                ProjectSyncState.NeedsSync, exception.Message, upstream, 0, behind);
            return new(ProjectSyncOutcome.Failed, kind,
                $"The fetched Project was rejected: {exception.Message}", invalid);
        }

        cancellationToken.ThrowIfCancellationRequested();
        LinkedContext currentContext = await RequireCleanContextAsync(
            registration, requireProjectedHead: true, cancellationToken);
        GitResult<GitUpstreamDetails> currentUpstream = await git.GetUpstreamDetailsAsync(
            registration.RepositoryPath, registration.ManagedBranch, cancellationToken);
        if (currentContext.Head.CommitId != expectedContext.Head.CommitId ||
            !currentUpstream.IsSuccess || currentUpstream.Value is null ||
            !SameUpstream(upstream, currentUpstream.Value) || currentUpstream.Value.CommitId != upstream.CommitId)
        {
            return Failure(registration, GitFailureKind.InvalidRepository,
                "The repository or upstream changed while synchronization was running.", upstream, 0, behind);
        }

        // HEAD is the authority boundary. Once the fast-forward begins, finish local projection
        // and registry bookkeeping even if the caller cancels.
        GitResult<bool> fastForward = await git.FastForwardAsync(
            registration.RepositoryPath, upstream.CommitId!, CancellationToken.None);
        if (!fastForward.IsSuccess)
            return Failure(registration, fastForward.FailureKind, fastForward.Diagnostic, upstream, 0, behind);
        GitResult<GitHead> afterHead = await git.GetHeadAsync(registration.RepositoryPath, CancellationToken.None);
        if (!afterHead.IsSuccess || afterHead.Value?.CommitId != upstream.CommitId)
        {
            ProjectRepositoryStatus uncertain = Status(registration, ProjectRepositoryStatusKind.StaleCache,
                ProjectSyncState.NeedsSync,
                "The fast-forward completed but authoritative HEAD could not be verified.", upstream);
            return new(ProjectSyncOutcome.Failed, ProjectSyncFailureKind.RepositoryBlocked,
                uncertain.Diagnostic!, uncertain);
        }

        try
        {
            await projectStateStore.ReplaceAsync(candidate, CancellationToken.None);
            registration = registration with { LastProjectedCommit = upstream.CommitId };
            await registry.UpsertAsync(registration, CancellationToken.None);
        }
        catch (Exception exception) when (IsRepositoryException(exception))
        {
            ProjectRepositoryStatus stale = Status(registration, ProjectRepositoryStatusKind.StaleCache,
                ProjectSyncState.NeedsSync,
                "The repository fast-forwarded, but the SQLite cache is stale. Rebuild the Project cache.",
                upstream);
            return new(ProjectSyncOutcome.FastForwarded, ProjectSyncFailureKind.RepositoryBlocked,
                $"The authoritative repository was updated, but cache projection failed: {exception.Message}", stale);
        }

        ProjectRepositoryStatus status = Status(registration, ProjectRepositoryStatusKind.GitClean,
            ProjectSyncState.UpToDate, null, upstream, 0, 0);
        return new(ProjectSyncOutcome.FastForwarded, ProjectSyncFailureKind.None,
            behind == 1 ? "Fast-forwarded by 1 upstream commit." : $"Fast-forwarded by {behind} upstream commits.",
            status);
    }

    private static void ValidateAppendOnlySuccessor(
        IReadOnlyDictionary<string, byte[]> current,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> candidate)
    {
        foreach ((string path, byte[] bytes) in current.Where(item =>
                     item.Key.StartsWith("operations/", StringComparison.Ordinal) ||
                     item.Key.StartsWith("tombstones/", StringComparison.Ordinal)))
        {
            if (!candidate.TryGetValue(path, out ReadOnlyMemory<byte> remote) ||
                !remote.Span.SequenceEqual(bytes))
            {
                throw new InvalidDataException(
                    $"Fetched history document '{path}' changed or removed append-only history.");
            }
        }
    }

    private void ValidateCanonicalSnapshot(
        ProjectRepositoryState state,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> fetched)
    {
        IReadOnlyDictionary<string, byte[]> canonical = codec.Serialize(state);
        if (!canonical.Keys.SequenceEqual(fetched.Keys, StringComparer.Ordinal) ||
            canonical.Any(item => !fetched.TryGetValue(item.Key, out ReadOnlyMemory<byte> bytes) ||
                                  !bytes.Span.SequenceEqual(item.Value)))
        {
            throw new InvalidDataException(
                "The fetched Project tree is not in canonical EntityTracker repository format.");
        }
    }

    private ProjectSyncResult Cancelled(
        LocalRepositoryRegistration registration,
        GitUpstreamDetails? upstream = null)
    {
        ProjectRepositoryStatus status = Status(registration, ProjectRepositoryStatusKind.GitClean,
            ProjectSyncState.NeedsSync, "Synchronization was cancelled.", upstream);
        return new(ProjectSyncOutcome.Cancelled, ProjectSyncFailureKind.Cancelled,
            "Synchronization was cancelled. Local commits remain intact.", status);
    }

    private ProjectSyncResult Failure(
        LocalRepositoryRegistration registration,
        GitFailureKind failureKind,
        string diagnostic,
        GitUpstreamDetails? upstream = null,
        int? ahead = null,
        int? behind = null)
    {
        ProjectSyncFailureKind mapped = failureKind switch
        {
            GitFailureKind.Authentication => ProjectSyncFailureKind.Authentication,
            GitFailureKind.Network => ProjectSyncFailureKind.Network,
            GitFailureKind.Cancelled => ProjectSyncFailureKind.Cancelled,
            GitFailureKind.TimedOut => ProjectSyncFailureKind.TimedOut,
            GitFailureKind.InvalidRepository => ProjectSyncFailureKind.InvalidRemote,
            _ => ProjectSyncFailureKind.CommandFailed
        };
        ProjectSyncOutcome outcome = mapped == ProjectSyncFailureKind.Cancelled
            ? ProjectSyncOutcome.Cancelled
            : failureKind == GitFailureKind.PushRejected
                ? ProjectSyncOutcome.NeedsSync
                : ProjectSyncOutcome.Failed;
        ProjectRepositoryStatus status = Status(registration, ProjectRepositoryStatusKind.GitClean,
            ProjectSyncState.NeedsSync, diagnostic, upstream, ahead, behind);
        return new(outcome, mapped,
            string.IsNullOrWhiteSpace(diagnostic) ? "Synchronization could not be completed." : diagnostic,
            status);
    }

    private static bool SameUpstream(GitUpstreamDetails left, GitUpstreamDetails right) =>
        left.IsConfigured == right.IsConfigured &&
        string.Equals(left.RemoteName, right.RemoteName, StringComparison.Ordinal) &&
        string.Equals(left.RemoteBranch, right.RemoteBranch, StringComparison.Ordinal) &&
        string.Equals(left.TrackingReference, right.TrackingReference, StringComparison.Ordinal);

    private static bool IsRepositoryException(Exception exception) =>
        exception is InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException;

    private static ProjectRepositoryStatus Status(
        LocalRepositoryRegistration registration,
        ProjectRepositoryStatusKind kind,
        ProjectSyncState syncState,
        string? diagnostic,
        GitUpstreamDetails? upstream = null,
        int? ahead = null,
        int? behind = null,
        int pendingCount = 0) =>
        new(
            registration.ProjectId,
            kind,
            registration.RepositoryPath,
            registration.ManagedBranch,
            diagnostic,
            syncState,
            upstream is { IsConfigured: true }
                ? $"{upstream.RemoteName}/{upstream.RemoteBranch}"
                : null,
            ahead,
            behind,
            registration.LastSuccessfulFetchAtUtc,
            registration.LastSuccessfulPushAtUtc,
            pendingCount);

    private async Task<ProjectMutationResult> ApplySqliteAsync(
        ProjectMutation mutation,
        CancellationToken cancellationToken) =>
        await ApplySqliteAsync(mutation, enqueueForGit: false, cancellationToken: cancellationToken);

    private async Task<ProjectMutationResult> ApplySqliteAsync(
        ProjectMutation mutation,
        bool enqueueForGit,
        CancellationToken cancellationToken)
    {
        SqliteBeforeCommit? beforeCommit = enqueueForGit
            ? (connection, transaction, token) => _outbox.EnqueueAsync(
                connection, transaction, mutation, token)
            : null;
        switch (mutation)
        {
            case CreateProjectMutation value:
                await sqliteCatalogStore.CreateProjectAsync(value.Project, cancellationToken); break;
            case RenameProjectMutation value:
                if (beforeCommit is null) await sqliteCatalogStore.RenameProjectAsync(value.ProjectId, value.Name, cancellationToken);
                else await sqliteCatalogStore.RenameProjectAsync(value.ProjectId, value.Name, beforeCommit, cancellationToken);
                break;
            case SetProjectLifecycleMutation value:
                if (beforeCommit is null) await sqliteCatalogStore.SetProjectLifecycleAsync(value.ProjectId, value.LifecycleState, cancellationToken);
                else await sqliteCatalogStore.SetProjectLifecycleAsync(value.ProjectId, value.LifecycleState, beforeCommit, cancellationToken);
                break;
            case PurgeProjectMutation value:
                if (beforeCommit is null) await sqliteCatalogStore.PurgeProjectAsync(value.ProjectId, cancellationToken);
                else await sqliteCatalogStore.PurgeProjectAsync(value.ProjectId, beforeCommit, cancellationToken);
                break;
            case CreateTrackerMutation value:
                if (beforeCommit is null) await sqliteCatalogStore.CreateTrackerAsync(value.Creation, cancellationToken);
                else await sqliteCatalogStore.CreateTrackerAsync(value.Creation, beforeCommit, cancellationToken);
                break;
            case RenameTrackerMutation value:
                if (beforeCommit is null) await sqliteCatalogStore.RenameTrackerAsync(value.TrackerId, value.Name, cancellationToken);
                else await sqliteCatalogStore.RenameTrackerAsync(value.TrackerId, value.Name, beforeCommit, cancellationToken);
                break;
            case SetTrackerLifecycleMutation value:
                if (beforeCommit is null) await sqliteCatalogStore.SetTrackerLifecycleAsync(value.TrackerId, value.LifecycleState, cancellationToken);
                else await sqliteCatalogStore.SetTrackerLifecycleAsync(value.TrackerId, value.LifecycleState, beforeCommit, cancellationToken);
                break;
            case PurgeTrackerMutation value:
                if (beforeCommit is null) await sqliteCatalogStore.PurgeTrackerAsync(value.TrackerId, cancellationToken);
                else await sqliteCatalogStore.PurgeTrackerAsync(value.TrackerId, beforeCommit, cancellationToken);
                break;
            case ChangeTrackedStateMutation { ImportCompletion: { } completion } value:
                SchemaImportSummary summary = beforeCommit is null
                    ? await sqliteTrackedStore.ApplyAsync(value.TrackerId, value.ChangeSet, completion, cancellationToken)
                    : await sqliteTrackedStore.ApplyAsync(value.TrackerId, value.ChangeSet, completion, beforeCommit, cancellationToken);
                return new ProjectMutationResult(summary);
            case ChangeTrackedStateMutation value:
                if (beforeCommit is null) await sqliteTrackedStore.ApplyAsync(value.TrackerId, value.ChangeSet, cancellationToken);
                else await sqliteTrackedStore.ApplyAsync(value.TrackerId, value.ChangeSet, beforeCommit, cancellationToken);
                break;
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
            GitResult<GitUpstreamDetails> upstream = await git.GetUpstreamDetailsAsync(
                registration.RepositoryPath, registration.ManagedBranch, cancellationToken);
            if (!upstream.IsSuccess || upstream.Value is null)
                return Status(ProjectRepositoryStatusKind.Blocked, upstream.Diagnostic);
            if (!upstream.Value.IsConfigured)
                return GitBackedProjectService.Status(registration, ProjectRepositoryStatusKind.GitClean,
                    ProjectSyncState.NoUpstream, "No upstream is configured for the managed branch.");
            if (upstream.Value.CommitId is null)
                return GitBackedProjectService.Status(registration, ProjectRepositoryStatusKind.GitClean,
                    ProjectSyncState.NeedsSync, "The configured upstream has not been fetched.", upstream.Value);
            GitResult<GitAheadBehind> graph = await git.GetAheadBehindAsync(
                registration.RepositoryPath, head.Value!.CommitId!, upstream.Value.CommitId, cancellationToken);
            if (!graph.IsSuccess || graph.Value is null)
                return Status(ProjectRepositoryStatusKind.Blocked, graph.Diagnostic);
            ProjectSyncState syncState = graph.Value switch
            {
                { Ahead: 0, Behind: 0 } => ProjectSyncState.UpToDate,
                { Ahead: > 0, Behind: 0 } => ProjectSyncState.Ahead,
                { Ahead: 0, Behind: > 0 } => ProjectSyncState.Behind,
                _ => ProjectSyncState.MergeRequired
            };
            string? diagnostic = syncState == ProjectSyncState.MergeRequired
                ? "Local and upstream commits have diverged. Semantic merge is required."
                : null;
            return GitBackedProjectService.Status(registration, ProjectRepositoryStatusKind.GitClean,
                syncState, diagnostic, upstream.Value, graph.Value.Ahead, graph.Value.Behind);
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return Status(ProjectRepositoryStatusKind.Blocked, exception.Message);
        }

        ProjectRepositoryStatus Status(ProjectRepositoryStatusKind kind, string? diagnostic) =>
            GitBackedProjectService.Status(
                registration,
                kind,
                kind == ProjectRepositoryStatusKind.GitClean
                    ? ProjectSyncState.NeedsSync
                    : ProjectSyncState.NotApplicable,
                diagnostic);
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

    private sealed record PendingCommitResult(
        LocalRepositoryRegistration Registration,
        LinkedContext Context,
        int CommitCount);
    private sealed record LinkMarkerMutation(ProjectId ProjectId, OperationId OperationId, DateTimeOffset OccurredAtUtc)
        : ProjectMutation(ProjectId, OperationId, RepositoryOperationKind.RepositoryLinked, OccurredAtUtc);
}

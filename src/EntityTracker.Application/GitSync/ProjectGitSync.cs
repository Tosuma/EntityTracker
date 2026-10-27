using EntityTracker.Application.Snapshots;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

using System.Diagnostics;
using System.Security.Cryptography;

using EntityTracker.Application.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntityTracker.Application.GitSync;

public sealed record ProjectSyncLink(
    Guid ProjectId, string RepositoryPath, string Branch, string? UpstreamIdentity,
    string? LastCommonCommit, long LastRevision, string? LastSnapshotHash,
    string LastResult, string SyncStatus, ProjectDeletionIntent? PendingDeletion = null,
    // When a sync last ended with the repository current; missing in link files from before it was kept.
    DateTimeOffset? LastSyncedAtUtc = null);

public enum ProjectSyncMode { Manual, Automatic }

public sealed class ProjectSyncEditDeferredException() : Exception(
    "Sync is waiting for the unfinished edit to be saved or closed.");

public sealed class ProjectSyncDeletionApprovalRequiredException() : Exception(
    "Review outbound deletions with Sync now before they are published.");

public sealed record ProjectDeletionIntent(Guid ProjectId, string BaseSnapshotHash,
    DateTimeOffset DeletedAtUtc, long ApprovedRevision);

public sealed record ProjectTombstone(int FormatVersion, Guid ProjectId,
    string BaseSnapshotHash, DateTimeOffset DeletedAtUtc);

public sealed record GitRemoteState(string Head, IReadOnlyDictionary<string, byte[]> SnapshotFiles);

public interface IProjectSyncBackup
{
    Task<string> CreatePreApplyBackupAsync(CancellationToken cancellationToken = default);
}

public sealed class ProjectNameCollisionException(string name) : InvalidOperationException(
    $"A local Project already uses '{name}'. Choose a unique local name to import this Project.")
{
    public string ConflictingName { get; } = name;
}

public sealed record GitWorkingTreeState(
    string RootPath, string Branch, string? UpstreamIdentity, string? Head,
    IReadOnlyDictionary<string, byte[]> SnapshotFiles);

/// <summary>One commit as listed between two commits: its ID, parents, subject and changed paths.</summary>
public sealed record GitCommitSummary(string Id, IReadOnlyList<string> Parents, string Subject,
    IReadOnlyList<string> ChangedPaths);

public interface IProjectSyncLinkStore
{
    Task<IReadOnlyList<ProjectSyncLink>> ReadAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ProjectSyncLink link, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid projectId, CancellationToken cancellationToken = default);
}

/// <summary>Where a Git setting is saved: for every repository of the user, or for one repository.</summary>
public enum GitConfigScope { Global, Local }

public interface ILocalGitTransport
{
    /// <summary>Saves the commit identity Git records with each commit, in the given scope.</summary>
    Task SetCommitIdentityAsync(string path, string name, string email, GitConfigScope scope,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Git configuration is unavailable.");

    Task<IAsyncDisposable> LockAsync(string path, CancellationToken cancellationToken = default);
    Task<GitWorkingTreeState> InspectAsync(string path, CancellationToken cancellationToken = default);
    Task<string> CommitSnapshotAsync(string path, IReadOnlyDictionary<string, byte[]> oldFiles,
        ProjectSnapshotPackage package, CancellationToken cancellationToken = default);

    Task<GitRemoteState> FetchAsync(string path, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Remote Git operations are unavailable.");
    Task<GitRemoteState> FetchAsync(string path, GitRemoteState? reusableSnapshot,
        CancellationToken cancellationToken = default) => FetchAsync(path, cancellationToken);
    Task<GitRemoteState> FetchAsync(string path, GitRemoteState? reusableSnapshot,
        CancellationToken cancellationToken, IProgress<GitFetchTiming>? timing) =>
        FetchAsync(path, reusableSnapshot, cancellationToken);
    Task<bool> IsAncestorAsync(string path, string ancestor, string descendant,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Remote Git operations are unavailable.");
    Task FastForwardAsync(string path, string expectedHead, string remoteHead,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Remote Git operations are unavailable.");
    Task PushAsync(string path, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Remote Git operations are unavailable.");
    Task<string?> MergeBaseAsync(string path, string localHead, string remoteHead,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Git merge-base inspection is unavailable.");
    Task<IReadOnlyDictionary<string, byte[]>> ReadSnapshotAtCommitAsync(string path, string commit,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Historical Git snapshot reads are unavailable.");
    Task ValidateMergeScopeAsync(string path, string localHead, string remoteHead,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Git merge scope validation is unavailable.");
    Task<string> CommitMergeSnapshotAsync(string path, string localHead, string remoteHead,
        IReadOnlyDictionary<string, byte[]> oldFiles, ProjectSnapshotPackage package,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Canonical merge commits are unavailable.");

    /// <summary>
    /// Lists the commits reachable from <paramref name="to"/> but not from <paramref name="from"/>,
    /// oldest first, with the paths each one changed (merges against their first parent).
    /// </summary>
    Task<IReadOnlyList<GitCommitSummary>> ListCommitsAsync(string path, string from, string to,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Git history listing is unavailable.");
}

public interface IOutboundDeletionApproval
{
    Task<bool> ApproveAsync(IReadOnlyList<string> deletedObjects, CancellationToken cancellationToken = default);
}

public interface IProjectUnsavedEditsGate
{
    Task WaitUntilReadyAsync(ProjectId projectId, CancellationToken cancellationToken = default);
    Task<bool> IsReadyAsync(ProjectId projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

/// <summary>Prevents repository writes until the installed app has checked its release.</summary>
public interface IProjectSyncVersionGate
{
    Task EnsureSyncAllowedAsync(CancellationToken cancellationToken);
}

public sealed class ProjectSyncUpdateRequiredException(string version)
    : InvalidOperationException($"EntityTracker {version} is required before Project sync.");

/// <summary>The update check failed; <paramref name="reason"/> says why and what to do, when known.</summary>
public sealed class ProjectSyncVersionCheckUnavailableException(string? reason = null)
    : InvalidOperationException("Project sync is paused until EntityTracker can check for app updates." +
                                (string.IsNullOrWhiteSpace(reason) ? "" : " " + reason));

public sealed partial class ProjectGitSyncService(
    IProjectSyncLinkStore links, ILocalGitTransport git, IProjectSnapshotStore snapshots,
    IProjectSnapshotCodec codec, IOutboundDeletionApproval deletionApproval,
    IProjectRepository? projects = null, IProjectSyncBackup? backup = null,
    IProjectMergeReview? mergeReview = null,
    IProjectUnsavedEditsGate? editGate = null,
    IProjectSyncVersionGate? versionGate = null,
    ILogger<ProjectGitSyncService>? logger = null,
    TimeProvider? clock = null)
{
    private readonly ILogger _log = logger ?? NullLogger<ProjectGitSyncService>.Instance;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>
    /// Runs one Git sync operation in its own log scope ("Sync 3f9a1c"), so its Git commands, phases and
    /// result read as a unit, and logs how it ended. A failure is logged here, once, with the whole
    /// exception; callers only add what they told the user.
    /// </summary>
    private async Task<T> LoggedAsync<T>(string operation, string subject, Func<Task<T>> body,
        Func<T, string> describe)
    {
        string id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(3));
        using IDisposable? scope = _log.BeginScope("{Operation} {OperationId}", operation, id);
        long started = Stopwatch.GetTimestamp();
        _log.LogInformation("{Operation} started: {Subject}.", operation, subject);
        try
        {
            T result = await body();
            _log.LogInformation("{Operation} finished in {ElapsedMs} ms: {Result}.",
                operation, ElapsedMs(started), describe(result));
            return result;
        }
        catch (Exception exception) when (exception is OperationCanceledException or ProjectSyncReviewCancelledException)
        {
            _log.LogInformation("{Operation} was cancelled after {ElapsedMs} ms.", operation, ElapsedMs(started));
            throw;
        }
        catch (ProjectSyncEditDeferredException)
        {
            _log.LogInformation("{Operation} was put off: an entity is being edited.", operation);
            throw;
        }
        catch (Exception exception) when (exception is ProjectSyncUpdateRequiredException
                                              or ProjectSyncIdentityRequiredException
                                              or ProjectSyncVersionCheckUnavailableException
                                              or ProjectSyncReviewRequiredException
                                              or ProjectSyncDeletionApprovalRequiredException)
        {
            _log.LogWarning(exception, "{Operation} stopped after {ElapsedMs} ms and needs attention.",
                operation, ElapsedMs(started));
            AppLogging.MarkLogged(exception);
            throw;
        }
        catch (Exception exception)
        {
            _log.LogError(exception, "{Operation} failed after {ElapsedMs} ms: {Subject}.",
                operation, ElapsedMs(started), subject);
            AppLogging.MarkLogged(exception);
            throw;
        }
    }

    private static long ElapsedMs(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    private static string Describe(ProjectSyncLink link) =>
        $"{link.SyncStatus}, {link.LastResult}, at commit {Short(link.LastCommonCommit)}";

    private static string Short(string? commit) => commit is { Length: >= 7 } ? commit[..7] : commit ?? "none";

    private async Task EnsureEditsReadyAsync(ProjectId projectId, ProjectSyncMode mode,
        CancellationToken token)
    {
        if (editGate is null) return;
        if (mode == ProjectSyncMode.Automatic)
        {
            if (!await editGate.IsReadyAsync(projectId, token))
                throw new ProjectSyncEditDeferredException();
            return;
        }
        await editGate.WaitUntilReadyAsync(projectId, token);
    }

    private Task<bool> ApproveDeletionsAsync(IReadOnlyList<string> deletions,
        ProjectSyncMode mode, CancellationToken token) =>
        mode == ProjectSyncMode.Automatic
            ? throw new ProjectSyncDeletionApprovalRequiredException()
            : deletionApproval.ApproveAsync(deletions, token);

    public async Task<ProjectSyncLink?> GetLinkAsync(ProjectId projectId, CancellationToken token = default) =>
        (await links.ReadAllAsync(token)).SingleOrDefault(link => link.ProjectId == projectId.Value);

    public Task<ProjectSyncLink> LinkAsync(ProjectId projectId, string path, CancellationToken token = default) =>
        LoggedAsync("Link", $"Project {projectId.Value:D} to the checkout {path}",
            () => LinkCoreAsync(projectId, path, token), Describe);

    private async Task<ProjectSyncLink> LinkCoreAsync(ProjectId projectId, string path, CancellationToken token)
    {
        if (versionGate is not null) await versionGate.EnsureSyncAllowedAsync(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using IAsyncDisposable guard = await git.LockAsync(path, token);
        GitWorkingTreeState state = await git.InspectAsync(path, token);
        IReadOnlyList<ProjectSyncLink> existing = await links.ReadAllAsync(token);
        if (existing.Any(link => link.ProjectId == projectId.Value &&
                                 link.SyncStatus != "RemoteDeletedLocalKept"))
            throw new InvalidOperationException("This Project is already linked. Unlink it before selecting another repository.");
        if (existing.Any(link => link.SyncStatus != "RemoteDeletedLocalKept" &&
                                 PathEquals(link.RepositoryPath, state.RootPath)))
            throw new InvalidOperationException("This repository is already linked to another Project.");

        ProjectSnapshotRead read = await snapshots.ReadAsync(projectId, token);
        if (read.Snapshot is null)
            throw new InvalidOperationException("The Project no longer exists.");
        ProjectSnapshotPackage local = codec.Encode(read.Snapshot);
        if (state.SnapshotFiles.Count > 0)
        {
            ProjectSnapshot remote = codec.Decode(state.SnapshotFiles);
            if (remote.Project.Id != projectId.Value)
                throw new InvalidOperationException("This repository contains a different Project. Use Import checkout on the Portfolio to add it separately.");
            if (!PackagesEqual(local.Files, codec.Encode(remote).Files))
            {
                read = await ReviewTwoWayAsync(projectId, read, remote, state, token);
                local = codec.Encode(read.Snapshot!);
            }
        }
        ProjectSyncLink link = NewLink(projectId, state, read.Revision,
            state.SnapshotFiles.Count == 0 ? null : codec.Encode(codec.Decode(state.SnapshotFiles)).Sha256);
        await links.SaveAsync(link, token);
        return link;
    }

    /// <summary>
    /// Loads a Project from an existing Git checkout (Portfolio › Import project), reporting each step to
    /// <paramref name="progress"/>.
    /// </summary>
    public Task<ProjectSyncLink> ImportAsync(string path, string? localName = null,
        CancellationToken token = default, IProgress<ProjectSyncPhase>? progress = null) =>
        LoggedAsync("Import", $"the checkout {path}", () => ImportCoreAsync(path, localName, token, progress), Describe);

    private async Task<ProjectSyncLink> ImportCoreAsync(string path, string? localName, CancellationToken token,
        IProgress<ProjectSyncPhase>? progress)
    {
        progress?.Report(ProjectSyncPhase.Inspecting);
        if (versionGate is not null) await versionGate.EnsureSyncAllowedAsync(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (projects is null || backup is null)
            throw new InvalidOperationException("Project import is unavailable in this application configuration.");
        await using IAsyncDisposable guard = await git.LockAsync(path, token);
        GitWorkingTreeState state = await git.InspectAsync(path, token);
        IReadOnlyList<ProjectSyncLink> existingLinks = await links.ReadAllAsync(token);
        if (existingLinks.Any(link => link.SyncStatus != "RemoteDeletedLocalKept" &&
                                      PathEquals(link.RepositoryPath, state.RootPath)))
            throw new InvalidOperationException("This repository is already linked to a Project.");
        ProjectSnapshot? checkoutSnapshot = state.SnapshotFiles.Count == 0
            ? null : codec.Decode(state.SnapshotFiles);
        if (checkoutSnapshot is not null && existingLinks.Any(link =>
                link.ProjectId == checkoutSnapshot.Project.Id &&
                link.SyncStatus != "RemoteDeletedLocalKept"))
            throw new InvalidOperationException("This Project is already linked. Unlink it before selecting another repository.");
        if (state.UpstreamIdentity is not null)
        {
            progress?.Report(ProjectSyncPhase.Fetching);
            GitRemoteState remote = await git.FetchAsync(path, token);
            if (state.Head != remote.Head)
            {
                if (state.Head is null || !await git.IsAncestorAsync(path, state.Head, remote.Head, token))
                {
                    if (!await git.IsAncestorAsync(path, remote.Head, state.Head ?? string.Empty, token))
                        throw new InvalidOperationException(
                            "The checkout and upstream diverged before import. Reconcile the user-managed checkout with command-line Git, then retry.");
                }
                else
                {
                    if (remote.SnapshotFiles.Count == 0)
                        throw new InvalidOperationException("The upstream has no Project snapshot to import.");
                    ProjectSnapshot remoteSnapshot = codec.Decode(remote.SnapshotFiles);
                    if (checkoutSnapshot is not null && remoteSnapshot.Project.Id != checkoutSnapshot.Project.Id)
                        throw new InvalidOperationException("The upstream belongs to a different Project.");
                    await git.FastForwardAsync(path, state.Head, remote.Head, token);
                    state = await git.InspectAsync(path, token);
                }
            }
        }
        if (state.SnapshotFiles.Count == 0)
            throw new InvalidOperationException("The selected checkout has no Project snapshot to import.");
        progress?.Report(ProjectSyncPhase.Validating);
        ProjectSnapshot incoming = codec.Decode(state.SnapshotFiles);
        ProjectId projectId = new(incoming.Project.Id);
        _log.LogInformation(
            "The checkout on {Branch} at commit {Commit} holds Project {ProjectName} ({ProjectId}): {TrackerCount} Trackers, {EntityCount} entities, snapshot format {Format}.",
            state.Branch, Short(state.Head), incoming.Project.Name, incoming.Project.Id, incoming.Trackers.Count,
            incoming.Trackers.Sum(static tracker => tracker.Entities.Count), incoming.FormatVersion);
        ProjectSnapshotRead current = await snapshots.ReadAsync(projectId, token);
        string hash = codec.Encode(incoming).Sha256;
        if (current.Snapshot is not null)
        {
            _log.LogInformation("Project {ProjectId} already exists here; {Action}.", projectId.Value,
                codec.Encode(current.Snapshot).Sha256 == hash ? "it matches the checkout" : "the differences are reviewed");
            if (existingLinks.Any(link => link.ProjectId == projectId.Value &&
                                          link.SyncStatus != "RemoteDeletedLocalKept"))
                throw new InvalidOperationException("This Project is already linked. Unlink it before selecting another repository.");
            if (codec.Encode(current.Snapshot).Sha256 != hash)
            {
                progress?.Report(ProjectSyncPhase.Reviewing);
                current = await ReviewTwoWayAsync(projectId, current, incoming, state, token);
            }
            ProjectSyncLink matching = NewLink(projectId, state, current.Revision, hash);
            await links.SaveAsync(matching, token);
            return matching;
        }
        string displayName = localName?.Trim() ?? incoming.Project.Name;
        if (displayName.Length == 0)
            throw new ArgumentException("Enter a unique local Project name.", nameof(localName));
        if (await projects.IsNameReservedAsync(displayName, null, token) &&
            !await IsPristineDefaultPlaceholderAsync(displayName, token))
            throw new ProjectNameCollisionException(displayName);
        GitWorkingTreeState recheck = await git.InspectAsync(path, token);
        if (recheck.Head != state.Head || recheck.Branch != state.Branch ||
            recheck.UpstreamIdentity != state.UpstreamIdentity || !PackagesEqual(recheck.SnapshotFiles, state.SnapshotFiles))
            throw new InvalidOperationException("The checkout changed during import. Retry.");
        ProjectSnapshotRead latest = await snapshots.ReadAsync(projectId, token);
        if (latest.Revision != current.Revision || latest.Snapshot is not null)
            throw new InvalidOperationException("The Project changed during import. Retry.");
        progress?.Report(ProjectSyncPhase.Applying);
        await backup.CreatePreApplyBackupAsync(token);
        long revision = await snapshots.ApplyAsync(incoming, current.Revision, displayName, token);
        _log.LogInformation("Imported as the new Project {ProjectName} at revision {Revision}.", displayName, revision);
        ProjectSyncLink imported = NewLink(projectId, state, revision, hash) with
        {
            LastResult = "Imported existing checkout"
        };
        await links.SaveAsync(imported, token);
        return imported;
    }

    private static ProjectSyncLink NewLink(ProjectId projectId, GitWorkingTreeState state,
        long revision, string? hash) => new(projectId.Value, state.RootPath, state.Branch,
        state.UpstreamIdentity, state.Head, hash is null ? 0 : revision, hash,
        hash is null ? "Linked; initial snapshot pending" : "Linked; snapshot matches",
        hash is null ? "Pending" : "Current");

    private async Task<bool> IsPristineDefaultPlaceholderAsync(string name, CancellationToken token)
    {
        if (projects is null || !string.Equals(name, "Default project", StringComparison.OrdinalIgnoreCase))
            return false;
        IReadOnlyList<Project> existing = await projects.GetAllAsync(token);
        if (existing.Count != 1 || !string.Equals(existing[0].Name, name, StringComparison.OrdinalIgnoreCase))
            return false;
        ProjectSnapshotRead read = await snapshots.ReadAsync(existing[0].Id, token);
        return read.Snapshot is { Trackers.Count: 1 } snapshot &&
            snapshot.Project.LifecycleState == "Active" &&
            snapshot.Trackers[0] is { Name: "Default tracker", LifecycleState: "Active",
                CopiedFromTrackerId: null } tracker &&
            tracker.Entities.Count == 0 && tracker.StatusHistory.Count == 0 &&
            tracker.ProgressHistory.Count <= 1 &&
            tracker.ProgressHistory.All(progress => progress.ReadyCount == 0 &&
                progress.BlockedCount == 0 && progress.InProgressCount == 0 &&
                progress.ReworkNeededCount == 0 && progress.ManuallyBlockedCount == 0 &&
                progress.ReworkingCount == 0 && progress.DevelopmentCompletedCount == 0 &&
                progress.ReconciledCount == 0);
    }

    public Task<ProjectSyncLink> SyncNowAsync(ProjectId projectId, CancellationToken token = default,
        IProgress<ProjectSyncPhase>? progress = null, IProgress<ProjectSyncTiming>? timing = null,
        ProjectSyncMode mode = ProjectSyncMode.Manual) =>
        SyncNowAsync(projectId, null, token, progress, timing, mode);

    public async Task<ProjectSyncLink> SyncNowAsync(ProjectId projectId, string? localName,
        CancellationToken token = default, IProgress<ProjectSyncPhase>? progress = null,
        IProgress<ProjectSyncTiming>? timing = null,
        ProjectSyncMode mode = ProjectSyncMode.Manual)
    {
        ProjectSyncTimingCollector collector = new(progress, _log);
        string outcome = "Completed";
        try
        {
            ProjectSyncLink result = await LoggedAsync(mode == ProjectSyncMode.Automatic ? "Auto-sync" : "Sync",
                $"Project {projectId.Value:D}", () => SyncCoreAsync(projectId, localName, token, collector, mode), Describe);
            if (result.SyncStatus is "PendingPush" or "PendingRemote") outcome = "Action needed";
            if (result.SyncStatus == "Current")
            {
                result = result with { LastSyncedAtUtc = _clock.GetUtcNow() };
                await links.SaveAsync(result, token);
            }
            return result;
        }
        catch (OperationCanceledException) { outcome = "Cancelled"; throw; }
        catch (ProjectSyncReviewCancelledException) { outcome = "Cancelled"; throw; }
        catch { outcome = "Failed"; throw; }
        finally { timing?.Report(collector.Finish(outcome)); }
    }

    private async Task<ProjectSyncLink> SyncCoreAsync(ProjectId projectId, string? localName,
        CancellationToken token, IProgress<ProjectSyncPhase>? progress, ProjectSyncMode mode)
    {
        if (versionGate is not null) await versionGate.EnsureSyncAllowedAsync(token);
        progress?.Report(ProjectSyncPhase.CheckingEdits);
        await EnsureEditsReadyAsync(projectId, mode, token);
        progress?.Report(ProjectSyncPhase.Inspecting);
        ProjectSyncLink link = await GetLinkAsync(projectId, token) ??
            throw new InvalidOperationException("This Project is not linked to a repository.");
        if (link.SyncStatus == "RemoteDeletedLocalKept")
            throw new InvalidOperationException("The shared Project was deleted; this local Project is unlinked.");
        await using IAsyncDisposable guard = await git.LockAsync(link.RepositoryPath, token);
        GitWorkingTreeState state = await git.InspectAsync(link.RepositoryPath, token);
        if (!PathEquals(state.RootPath, link.RepositoryPath) || state.Branch != link.Branch ||
            state.UpstreamIdentity != link.UpstreamIdentity)
            throw new ProjectSyncConfigurationException("Repository root, branch, or upstream changed. Restore the linked configuration externally or unlink and relink.");
        progress?.Report(ProjectSyncPhase.Exporting);
        ProjectSnapshotRead read = await snapshots.ReadAsync(projectId, token);
        if (read.Snapshot is not null && link.PendingDeletion is not null)
            throw new InvalidOperationException(
                "Project deletion is pending but the Project still exists. Retry permanent deletion from the recycle bin.");
        if (read.Snapshot is null)
        {
            if (link.PendingDeletion is null)
            {
                if (state.UpstreamIdentity is not null)
                {
                    GitRemoteState published = await git.FetchAsync(link.RepositoryPath,
                        state.Head is null ? null : new GitRemoteState(state.Head, state.SnapshotFiles),
                        token, progress as IProgress<GitFetchTiming>);
                    if (codec.TryDecodeTombstone(published.SnapshotFiles, out ProjectTombstone? received) &&
                        received!.ProjectId == projectId.Value)
                    {
                        if (state.Head != published.Head)
                        {
                            if (state.Head is null)
                                throw new InvalidOperationException("The checkout has no committed Project base.");
                            if (await git.IsAncestorAsync(link.RepositoryPath, state.Head,
                                    published.Head, token))
                                await git.FastForwardAsync(link.RepositoryPath, state.Head,
                                    published.Head, token);
                            else
                            {
                                progress?.Report(ProjectSyncPhase.Committing);
                                await git.CommitMergeSnapshotAsync(link.RepositoryPath,
                                    state.Head, published.Head, state.SnapshotFiles,
                                    codec.EncodeTombstone(received), token);
                                progress?.Report(ProjectSyncPhase.Pushing);
                                await git.PushAsync(link.RepositoryPath, token);
                            }
                        }
                        await links.RemoveAsync(projectId.Value, token);
                        return link with { SyncStatus = "Deleted",
                            LastResult = "Published deletion received" };
                    }
                }
                throw new InvalidOperationException("The Project was purged without a pending tombstone.");
            }
            return await PublishTombstoneAsync(link, state, token, progress: progress, mode: mode);
        }
        ProjectSnapshotPackage local = codec.Encode(read.Snapshot);
        if (localName is not null &&
            (string.IsNullOrWhiteSpace(localName) ||
             (projects is not null && await projects.IsNameReservedAsync(localName, projectId, token))))
            throw new ProjectNameCollisionException(localName);
        GitRemoteState? remote = null;
        if (state.UpstreamIdentity is not null)
        {
            progress?.Report(ProjectSyncPhase.Fetching);
            remote = await git.FetchAsync(link.RepositoryPath,
                state.Head is null ? null : new GitRemoteState(state.Head, state.SnapshotFiles),
                token, progress as IProgress<GitFetchTiming>);
            if (codec.TryDecodeTombstone(remote.SnapshotFiles, out ProjectTombstone? tombstone))
                return await ReceiveTombstoneAsync(projectId, link, state, remote,
                    tombstone!, read, token, progress, mode);
            if (state.Head is not null && state.Head != remote.Head)
            {
                bool localAncestor = await git.IsAncestorAsync(
                    link.RepositoryPath, state.Head, remote.Head, token);
                bool remoteAncestor = await git.IsAncestorAsync(
                    link.RepositoryPath, remote.Head, state.Head, token);
                bool localChanges = state.SnapshotFiles.Count == 0 ||
                    !PackagesEqual(state.SnapshotFiles, local.Files);
                bool remoteChanges = !PackagesEqual(state.SnapshotFiles, remote.SnapshotFiles);
                if ((!localAncestor && !remoteAncestor) ||
                    (localAncestor && localChanges && remoteChanges))
                    return await SyncConcurrentAsync(projectId, link, state, remote,
                        read, localName, token, progress, mode: mode);
            }
            if (state.Head != link.LastCommonCommit && state.Head != remote.Head &&
                state.Head is not null && link.LastCommonCommit is not null &&
                await git.IsAncestorAsync(link.RepositoryPath, link.LastCommonCommit, state.Head, token) &&
                await git.IsAncestorAsync(link.RepositoryPath, remote.Head, state.Head, token) &&
                PackagesEqual(state.SnapshotFiles, local.Files))
                link = link with { LastCommonCommit = state.Head, LastRevision = read.Revision,
                    LastSnapshotHash = local.Sha256, SyncStatus = "PendingPush" };
            if (state.Head != link.LastCommonCommit && state.Head == remote.Head &&
                link.LastCommonCommit is not null &&
                await git.IsAncestorAsync(link.RepositoryPath, link.LastCommonCommit, state.Head!, token))
            {
                if (remote.SnapshotFiles.Count == 0)
                    throw new InvalidOperationException("The upstream Project snapshot is missing.");
                ProjectSnapshot resumed = codec.Decode(remote.SnapshotFiles);
                if (resumed.Project.Id != projectId.Value)
                    throw new InvalidOperationException("The upstream belongs to a different Project.");
                string resumedHash = codec.Encode(resumed).Sha256;
                if (read.Revision == link.LastRevision && local.Sha256 == link.LastSnapshotHash &&
                    resumedHash != local.Sha256)
                {
                    await EnsureEditsReadyAsync(projectId, mode, token);
                    if (backup is null)
                        throw new InvalidOperationException("A pre-apply backup service is required for inbound sync.");
                    progress?.Report(ProjectSyncPhase.Applying);
                    await backup.CreatePreApplyBackupAsync(token);
                    long applied = await snapshots.ApplyAsync(resumed, read.Revision, localName, token);
                    read = new ProjectSnapshotRead(resumed, applied);
                    local = codec.Encode(resumed);
                }
                else if (resumedHash != local.Sha256)
                    return await SyncConcurrentAsync(projectId, link, state, remote,
                        read, localName, token, progress, mode: mode);
                link = link with { LastCommonCommit = state.Head, LastRevision = read.Revision,
                    LastSnapshotHash = resumedHash };
            }
            if (state.Head != remote.Head && state.Head is not null &&
                await git.IsAncestorAsync(link.RepositoryPath, state.Head, remote.Head, token))
            {
                if (state.Head != link.LastCommonCommit)
                    throw new InvalidOperationException("The checkout changed since the last sync. Review it outside EntityTracker.");
                ProjectSnapshot? inbound = remote.SnapshotFiles.Count == 0 ? null : codec.Decode(remote.SnapshotFiles);
                if (inbound is not null && inbound.Project.Id != projectId.Value)
                    throw new InvalidOperationException("The upstream belongs to a different Project.");
                string? inboundHash = inbound is null ? null : codec.Encode(inbound).Sha256;
                bool remoteChanged = inboundHash != link.LastSnapshotHash;
                bool localChanged = local.Sha256 != link.LastSnapshotHash;
                if (remoteChanged && localChanged && inboundHash != local.Sha256)
                    return await SyncConcurrentAsync(projectId, link, state, remote,
                        read, localName, token, progress, mode: mode);
                if (inbound is null && link.LastSnapshotHash is not null)
                    throw new InvalidOperationException("The upstream Project snapshot is missing.");
                if (remoteChanged && !localChanged && inbound is not null)
                {
                    Project? displayProject = projects is null ? null : await projects.GetAsync(projectId, token);
                    bool hasAlias = displayProject is not null &&
                        !string.Equals(displayProject.Name, read.Snapshot!.Project.Name, StringComparison.Ordinal);
                    if (localName is null && !hasAlias && projects is not null &&
                        await projects.IsNameReservedAsync(inbound.Project.Name, projectId, token) &&
                        !string.Equals(inbound.Project.Name, read.Snapshot!.Project.Name, StringComparison.Ordinal))
                        throw new ProjectNameCollisionException(inbound.Project.Name);
                }
                await EnsureEditsReadyAsync(projectId, mode, token);
                ProjectSnapshotRead beforeFastForward = await snapshots.ReadAsync(projectId, token);
                if (beforeFastForward.Revision != read.Revision)
                    throw new InvalidOperationException("The Project changed while waiting to sync. Retry.");
                await git.FastForwardAsync(link.RepositoryPath, state.Head, remote.Head, token);
                state = await git.InspectAsync(link.RepositoryPath, token);
                if (inbound is not null && remoteChanged && !localChanged)
                {
                    if (backup is null)
                        throw new InvalidOperationException("A pre-apply backup service is required for inbound sync.");
                    progress?.Report(ProjectSyncPhase.Applying);
                    await backup.CreatePreApplyBackupAsync(token);
                    long applied = await snapshots.ApplyAsync(inbound, read.Revision, localName, token);
                    read = new ProjectSnapshotRead(inbound, applied);
                    local = codec.Encode(inbound);
                }
                link = link with { LastCommonCommit = remote.Head, LastRevision = read.Revision,
                    LastSnapshotHash = inboundHash };
            }
            else if (state.Head != remote.Head && (state.Head is null ||
                     !await git.IsAncestorAsync(link.RepositoryPath, remote.Head, state.Head, token)))
            {
                if (state.Head is null)
                    throw new InvalidOperationException("The checkout has no committed Project base for merge.");
                return await SyncConcurrentAsync(projectId, link, state, remote,
                    read, localName, token, progress, mode: mode);
            }
        }
        if (state.Head != link.LastCommonCommit &&
            await AdoptUnrecordedSnapshotCommitsAsync(projectId, link, state, remote, token) is { } adopted)
            link = adopted;
        if (state.Head != link.LastCommonCommit)
            throw new InvalidOperationException("Repository HEAD changed since the last sync. Review it outside EntityTracker before relinking.");
        progress?.Report(ProjectSyncPhase.Validating);
        if (state.SnapshotFiles.Count > 0)
        {
            ProjectSnapshot repositorySnapshot = codec.Decode(state.SnapshotFiles);
            if (repositorySnapshot.Project.Id != projectId.Value)
                throw new InvalidOperationException("The repository belongs to another Project.");
            ProjectSnapshotPackage basePackage = codec.Encode(repositorySnapshot);
            // HEAD is the commit of the last sync and the checkout is clean, so the files are the ones
            // that sync wrote. In an older format, a different hash only means this version encodes
            // them differently (for example without the provenance versions before 2.0.0 wrote);
            // the sync then rewrites them in the current format.
            if (link.LastSnapshotHash != basePackage.Sha256 &&
                repositorySnapshot.FormatVersion == ProjectSnapshot.CurrentFormatVersion)
                throw new InvalidOperationException("The repository snapshot changed since linking. Conflict review is required.");
            IReadOnlyList<string> deletions = FindDeletions(repositorySnapshot, read.Snapshot!);
            if (deletions.Count > 0)
            {
                progress?.Report(ProjectSyncPhase.Reviewing);
                if (!await ApproveDeletionsAsync(deletions, mode, token))
                    throw new InvalidOperationException("Outbound deletions were not approved; no files were changed.");
                ProjectSnapshotRead recheck = await snapshots.ReadAsync(projectId, token);
                if (recheck.Revision != read.Revision || recheck.Snapshot is null ||
                    codec.Encode(recheck.Snapshot).Sha256 != local.Sha256)
                    throw new InvalidOperationException("The Project changed during deletion review. Retry sync.");
                GitWorkingTreeState gitRecheck = await git.InspectAsync(link.RepositoryPath, token);
                if (gitRecheck.Head != state.Head || !PackagesEqual(gitRecheck.SnapshotFiles, state.SnapshotFiles))
                    throw new InvalidOperationException("The repository changed during deletion review. Retry sync.");
            }
        }
        bool unchanged = PackagesEqual(local.Files, state.SnapshotFiles);
        string head;
        if (unchanged) head = state.Head ?? string.Empty;
        else
        {
            ProjectSnapshotRead latest = await snapshots.ReadAsync(projectId, token);
            if (latest.Revision != read.Revision || latest.Snapshot is null ||
                codec.Encode(latest.Snapshot).Sha256 != local.Sha256)
                throw new InvalidOperationException("The Project changed while preparing sync. Retry.");
            GitWorkingTreeState latestGit = await git.InspectAsync(link.RepositoryPath, token);
            if (latestGit.Head != state.Head || latestGit.Branch != state.Branch ||
                latestGit.UpstreamIdentity != state.UpstreamIdentity ||
                !PackagesEqual(latestGit.SnapshotFiles, state.SnapshotFiles))
                throw new InvalidOperationException("The repository changed while preparing sync. Retry.");
            progress?.Report(ProjectSyncPhase.Committing);
            head = await git.CommitSnapshotAsync(link.RepositoryPath, state.SnapshotFiles, local, token);
            // Record the commit before anything else can fail. If the checks below stop this
            // sync (for example because the Project was edited meanwhile), the next sync still
            // knows this commit as its base, commits the newer edits on top and pushes both,
            // instead of finding an unknown HEAD and refusing to continue.
            link = link with
            {
                LastCommonCommit = head,
                LastRevision = read.Revision,
                LastSnapshotHash = local.Sha256,
                LastResult = "Local snapshot committed",
                SyncStatus = remote is null ? "Current" : "PendingPush"
            };
            await links.SaveAsync(link, token);
        }
        progress?.Report(ProjectSyncPhase.Validating);
        ProjectSnapshotRead finalRead = await snapshots.ReadAsync(projectId, token);
        if (finalRead.Snapshot is null || finalRead.Revision != read.Revision ||
            codec.Encode(finalRead.Snapshot).Sha256 != local.Sha256)
            throw new InvalidOperationException("The Project changed while syncing. Retry.");
        GitWorkingTreeState finalGit = await git.InspectAsync(link.RepositoryPath, token);
        if (finalGit.Head != head || finalGit.Branch != link.Branch ||
            finalGit.UpstreamIdentity != link.UpstreamIdentity ||
            !PackagesEqual(finalGit.SnapshotFiles, local.Files))
            throw new InvalidOperationException("The checkout changed while syncing. Review it before retrying.");
        ProjectSyncLink updated = link with
        {
            LastCommonCommit = string.IsNullOrEmpty(head) ? null : head,
            LastRevision = read.Revision,
            LastSnapshotHash = local.Sha256,
            LastResult = unchanged ? "Already current" : "Local snapshot committed",
            SyncStatus = remote is null ? "Current" : "PendingPush"
        };
        await links.SaveAsync(updated, token);
        if (remote is not null)
        {
            if (remote.Head != head)
            {
                progress?.Report(ProjectSyncPhase.Pushing);
                try { await git.PushAsync(link.RepositoryPath, token); }
                catch (InvalidOperationException exception)
                {
                    _log.LogWarning(exception, "Push was refused; fetching to see whether the remote moved on.");
                    progress?.Report(ProjectSyncPhase.Rechecking);
                    GitRemoteState refreshed = await git.FetchAsync(link.RepositoryPath, remote,
                        token, progress as IProgress<GitFetchTiming>);
                    if (refreshed.Head != head &&
                        await git.IsAncestorAsync(link.RepositoryPath, refreshed.Head, head, token))
                        await git.PushAsync(link.RepositoryPath, token);
                    else if (refreshed.Head != head &&
                             await git.IsAncestorAsync(link.RepositoryPath, head, refreshed.Head, token))
                    {
                        if (refreshed.SnapshotFiles.Count == 0)
                            throw new InvalidOperationException("The upstream Project snapshot is missing.");
                        ProjectSnapshot advanced = codec.Decode(refreshed.SnapshotFiles);
                        if (advanced.Project.Id != projectId.Value)
                            throw new InvalidOperationException("The upstream belongs to a different Project.");
                        string advancedHash = codec.Encode(advanced).Sha256;
                        if (advancedHash != local.Sha256 && localName is null && projects is not null &&
                            await projects.IsNameReservedAsync(advanced.Project.Name, projectId, token))
                        {
                            Project? displayProject = await projects.GetAsync(projectId, token);
                            if (displayProject is not null &&
                                string.Equals(displayProject.Name, read.Snapshot!.Project.Name, StringComparison.Ordinal))
                                throw new ProjectNameCollisionException(advanced.Project.Name);
                        }
                        await git.FastForwardAsync(link.RepositoryPath, head, refreshed.Head, token);
                        if (advancedHash != local.Sha256)
                        {
                            await EnsureEditsReadyAsync(projectId, mode, token);
                            if (backup is null)
                                throw new InvalidOperationException("A pre-apply backup service is required for inbound sync.");
                            progress?.Report(ProjectSyncPhase.Applying);
                            await backup.CreatePreApplyBackupAsync(token);
                            long applied = await snapshots.ApplyAsync(advanced, read.Revision, localName, token);
                            read = new ProjectSnapshotRead(advanced, applied);
                        }
                        updated = updated with { LastCommonCommit = refreshed.Head,
                            LastRevision = read.Revision, LastSnapshotHash = advancedHash,
                            SyncStatus = "PendingRemote",
                            LastResult = "Upstream advanced after push; validating latest state." };
                        await links.SaveAsync(updated, token);
                        progress?.Report(ProjectSyncPhase.Rechecking);
                        GitRemoteState rechecked = await git.FetchAsync(link.RepositoryPath, refreshed,
                            token, progress as IProgress<GitFetchTiming>);
                        ProjectSnapshotRead latestRead = await snapshots.ReadAsync(projectId, token);
                        if (rechecked.Head != refreshed.Head || latestRead.Revision != read.Revision)
                            return updated;
                        updated = updated with { SyncStatus = "Current",
                            LastResult = "Upstream advanced after push; fast-forward applied" };
                        await links.SaveAsync(updated, token);
                        return updated;
                    }
                    else if (refreshed.Head != head)
                    {
                        GitWorkingTreeState pendingState = await git.InspectAsync(link.RepositoryPath, token);
                        return await SyncConcurrentAsync(projectId, updated, pendingState,
                            refreshed, await snapshots.ReadAsync(projectId, token), localName, token, progress,
                            mode: mode);
                    }
                }
            }
            else
            {
                progress?.Report(ProjectSyncPhase.Rechecking);
                GitRemoteState rechecked = await git.FetchAsync(link.RepositoryPath,
                    new GitRemoteState(head, local.Files), token, progress as IProgress<GitFetchTiming>);
                if (rechecked.Head != head)
                {
                    updated = updated with { SyncStatus = "PendingRemote",
                        LastResult = "Upstream advanced during sync. Sync again to validate its changes." };
                    await links.SaveAsync(updated, token);
                    return updated;
                }
            }
            updated = updated with { SyncStatus = "Current", LastResult = unchanged ? "Remote already current" : "Snapshot pushed" };
            await links.SaveAsync(updated, token);
        }
        return updated;
    }

    /// <summary>
    /// Saves the Git commit identity that import and sync need, for every repository
    /// (<see cref="GitConfigScope.Global"/>) or only the one at <paramref name="path"/>. Portfolio ›
    /// Import project asks for it when Git has none.
    /// </summary>
    public Task SetCommitIdentityAsync(string path, string name, string email, GitConfigScope scope,
        CancellationToken token = default) =>
        LoggedAsync("Set Git identity", $"the checkout {path}, {scope.ToString().ToLowerInvariant()} configuration", async () =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            string trimmedName = RequireIdentityValue(name, "name");
            string trimmedEmail = RequireIdentityValue(email, "email");
            await git.SetCommitIdentityAsync(path, trimmedName, trimmedEmail, scope, token);
            return true;
        }, static _ => "user.name and user.email were saved");

    private static string RequireIdentityValue(string value, string what)
    {
        string trimmed = value?.Trim() ?? "";
        if (trimmed.Length == 0 || trimmed.StartsWith('-') || trimmed.Any(char.IsControl))
            throw new ArgumentException($"Enter a valid Git {what}.", what);
        return trimmed;
    }

    public Task UnlinkAsync(ProjectId projectId, CancellationToken token = default) =>
        LoggedAsync("Unlink", $"Project {projectId.Value:D}", async () =>
        {
            await links.RemoveAsync(projectId.Value, token);
            return true;
        }, static _ => "the link was removed");

    private static bool PathEquals(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>
    /// Recovers a checkout whose HEAD moved past the recorded base only through EntityTracker's own
    /// snapshot commits, for example when an earlier sync committed and then stopped before it
    /// could record the commit. Those commits were made from this Project's own data, which has
    /// only moved forward since, so HEAD can safely become the base; the current Project is then
    /// committed on top as usual. Anything else, such as a hand-made commit or one touching other
    /// files, is left for the user to review.
    /// </summary>
    private async Task<ProjectSyncLink?> AdoptUnrecordedSnapshotCommitsAsync(ProjectId projectId,
        ProjectSyncLink link, GitWorkingTreeState state, GitRemoteState? remote, CancellationToken token)
    {
        if (state.Head is null || link.LastCommonCommit is null || state.SnapshotFiles.Count == 0 ||
            !await git.IsAncestorAsync(link.RepositoryPath, link.LastCommonCommit, state.Head, token) ||
            (remote is not null && remote.Head != state.Head &&
             !await git.IsAncestorAsync(link.RepositoryPath, remote.Head, state.Head, token)))
            return null;

        IReadOnlyList<GitCommitSummary> commits;
        try { commits = await git.ListCommitsAsync(link.RepositoryPath, link.LastCommonCommit, state.Head, token); }
        catch (NotSupportedException exception)
        {
            _log.LogDebug(exception, "Commit history can't be listed here; unrecorded snapshot commits are left for review.");
            return null;
        }
        bool onlySnapshotCommits = commits.Count > 0 && commits.All(commit =>
            commit.Subject is SnapshotCommitSubject or MergeCommitSubject &&
            commit.ChangedPaths.All(path => path.StartsWith(".entitytracker/", StringComparison.Ordinal)));
        if (!onlySnapshotCommits) return null;

        ProjectSnapshot committed = codec.Decode(state.SnapshotFiles);
        if (committed.Project.Id != projectId.Value) return null;
        ProjectSyncLink adopted = link with
        {
            LastCommonCommit = state.Head,
            LastSnapshotHash = codec.Encode(committed).Sha256,
            SyncStatus = remote is not null && remote.Head != state.Head ? "PendingPush" : link.SyncStatus,
            LastResult = "Recovered an unrecorded EntityTracker snapshot commit"
        };
        await links.SaveAsync(adopted, token);
        return adopted;
    }

    private const string SnapshotCommitSubject = "Update EntityTracker project snapshot";
    private const string MergeCommitSubject = "Merge EntityTracker project snapshots";

    private static bool PackagesEqual(IReadOnlyDictionary<string, byte[]> a, IReadOnlyDictionary<string, byte[]> b) =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out byte[]? bytes) && pair.Value.AsSpan().SequenceEqual(bytes));

    /// <summary>
    /// Describes, by name, what the repository loses when sync writes <paramref name="current"/> over
    /// <paramref name="previous"/>: one line per removed developer, Tracker, entity or dependency, with
    /// the history that goes with it counted rather than listed.
    /// </summary>
    private static IReadOnlyList<string> FindDeletions(ProjectSnapshot previous, ProjectSnapshot current)
    {
        List<string> deleted = [];
        HashSet<Guid> developerIds = (current.Developers ?? []).Select(d => d.Id).ToHashSet();
        deleted.AddRange((previous.Developers ?? []).Where(d => !developerIds.Contains(d.Id))
            .Select(d => $"Developer {Quote(string.IsNullOrWhiteSpace(d.DisplayName) ? d.Initials : d.DisplayName)}"));
        HashSet<Guid> trackerIds = current.Trackers.Select(t => t.Id).ToHashSet();
        // Both sides in the current format, so a format upgrade is not a deletion.
        foreach (SnapshotTracker tracker in previous.Trackers.Select(ProjectSnapshotUpgrade.ToCurrentDependencies))
        {
            if (!trackerIds.Contains(tracker.Id))
            {
                deleted.Add(TrackerDeletion(tracker));
                continue;
            }
            string trackerName = $"Tracker {Quote(tracker.Name)}";
            SnapshotTracker next = ProjectSnapshotUpgrade.ToCurrentDependencies(
                current.Trackers.Single(t => t.Id == tracker.Id));
            Dictionary<Guid, string> names = tracker.Entities.ToDictionary(e => e.Id, e => e.SourceName);
            HashSet<Guid> remainingEntities = next.Entities.Select(e => e.Id).ToHashSet();
            HashSet<Guid> remainingEvents = next.StatusHistory.Select(e => e.EventId).ToHashSet();
            List<SnapshotStatusEvent> removedEvents = tracker.StatusHistory
                .Where(e => !remainingEvents.Contains(e.EventId)).ToList();
            foreach (SnapshotEntity removed in tracker.Entities.Where(e => !remainingEntities.Contains(e.Id)))
            {
                string owned = Counts(
                    (removed.Dependencies.Count, "dependency", "dependencies"),
                    (removedEvents.Count(e => e.EntityId == removed.Id), "status change", "status changes"));
                deleted.Add($"Entity {Quote(removed.SourceName)} in {trackerName}" +
                            (owned.Length == 0 ? "" : $", with its {owned}"));
            }
            foreach (SnapshotEntity entity in tracker.Entities)
            {
                SnapshotEntity? updated = next.Entities.SingleOrDefault(e => e.Id == entity.Id);
                if (updated is null) continue;
                deleted.AddRange(entity.Dependencies
                    .Where(d => updated.Dependencies.All(n => n.DependencyEntityId != d.DependencyEntityId))
                    .Select(d => $"Dependency in {trackerName}: {Quote(entity.SourceName)} depends on " +
                                 (names.TryGetValue(d.DependencyEntityId, out string? name) ? Quote(name) : "an unknown entity")));
            }
            string history = Counts(
                (removedEvents.Count(e => remainingEntities.Contains(e.EntityId) || !names.ContainsKey(e.EntityId)),
                    "status change", "status changes"),
                (tracker.ProgressHistory.Select(e => e.SnapshotId)
                    .Except(next.ProgressHistory.Select(e => e.SnapshotId)).Count(), "progress record", "progress records"));
            if (history.Length > 0) deleted.Add($"History in {trackerName}: {history}");
        }
        return deleted;
    }

    private static string TrackerDeletion(SnapshotTracker tracker)
    {
        string contents = Counts(
            (tracker.Entities.Count, "entity", "entities"),
            (tracker.Entities.Sum(e => e.Dependencies.Count), "dependency", "dependencies"),
            (tracker.StatusHistory.Count, "status change", "status changes"),
            (tracker.ProgressHistory.Count, "progress record", "progress records"));
        string name = $"Tracker {Quote(tracker.Name)}";
        return contents.Length == 0 ? name : $"{name} and everything in it: {contents}";
    }

    private static string Quote(string name) => $"“{name}”";

    private static string Counts(params (int Count, string One, string Many)[] parts) =>
        string.Join(", ", parts.Where(p => p.Count > 0).Select(p => $"{p.Count} {(p.Count == 1 ? p.One : p.Many)}"));
}

using EntityTracker.Application.Snapshots;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.GitSync;

public sealed record ProjectSyncLink(
    Guid ProjectId, string RepositoryPath, string Branch, string? UpstreamIdentity,
    string? LastCommonCommit, long LastRevision, string? LastSnapshotHash,
    string LastResult, string SyncStatus);

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

public interface IProjectSyncLinkStore
{
    Task<IReadOnlyList<ProjectSyncLink>> ReadAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ProjectSyncLink link, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid projectId, CancellationToken cancellationToken = default);
}

public interface ILocalGitTransport
{
    Task<IAsyncDisposable> LockAsync(string path, CancellationToken cancellationToken = default);
    Task<GitWorkingTreeState> InspectAsync(string path, CancellationToken cancellationToken = default);
    Task<string> CommitSnapshotAsync(string path, IReadOnlyDictionary<string, byte[]> oldFiles,
        ProjectSnapshotPackage package, CancellationToken cancellationToken = default);

    Task<GitRemoteState> FetchAsync(string path, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Remote Git operations are unavailable.");
    Task<bool> IsAncestorAsync(string path, string ancestor, string descendant,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Remote Git operations are unavailable.");
    Task FastForwardAsync(string path, string expectedHead, string remoteHead,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Remote Git operations are unavailable.");
    Task PushAsync(string path, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Remote Git operations are unavailable.");
}

public interface IOutboundDeletionApproval
{
    Task<bool> ApproveAsync(IReadOnlyList<string> deletedObjects, CancellationToken cancellationToken = default);
}

public sealed class ProjectGitSyncService(
    IProjectSyncLinkStore links, ILocalGitTransport git, IProjectSnapshotStore snapshots,
    IProjectSnapshotCodec codec, IOutboundDeletionApproval deletionApproval,
    IProjectRepository? projects = null, IProjectSyncBackup? backup = null)
{
    public async Task<ProjectSyncLink?> GetLinkAsync(ProjectId projectId, CancellationToken token = default) =>
        (await links.ReadAllAsync(token)).SingleOrDefault(link => link.ProjectId == projectId.Value);

    public async Task<ProjectSyncLink> LinkAsync(ProjectId projectId, string path, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using IAsyncDisposable guard = await git.LockAsync(path, token);
        GitWorkingTreeState state = await git.InspectAsync(path, token);
        IReadOnlyList<ProjectSyncLink> existing = await links.ReadAllAsync(token);
        if (existing.Any(link => link.ProjectId == projectId.Value))
            throw new InvalidOperationException("This Project is already linked. Unlink it before selecting another repository.");
        if (existing.Any(link => PathEquals(link.RepositoryPath, state.RootPath)))
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
                throw new InvalidOperationException("The repository snapshot differs from this Project. Conflict review is required before linking.");
        }
        ProjectSyncLink link = NewLink(projectId, state, read.Revision,
            state.SnapshotFiles.Count == 0 ? null : local.Sha256);
        await links.SaveAsync(link, token);
        return link;
    }

    public async Task<ProjectSyncLink> ImportAsync(string path, string? localName = null,
        CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (projects is null || backup is null)
            throw new InvalidOperationException("Project import is unavailable in this application configuration.");
        await using IAsyncDisposable guard = await git.LockAsync(path, token);
        GitWorkingTreeState state = await git.InspectAsync(path, token);
        IReadOnlyList<ProjectSyncLink> existingLinks = await links.ReadAllAsync(token);
        if (existingLinks.Any(link => PathEquals(link.RepositoryPath, state.RootPath)))
            throw new InvalidOperationException("This repository is already linked to a Project.");
        ProjectSnapshot? checkoutSnapshot = state.SnapshotFiles.Count == 0
            ? null : codec.Decode(state.SnapshotFiles);
        if (state.UpstreamIdentity is not null)
        {
            GitRemoteState remote = await git.FetchAsync(path, token);
            if (state.Head != remote.Head)
            {
                if (state.Head is null || !await git.IsAncestorAsync(path, state.Head, remote.Head, token))
                {
                    if (!await git.IsAncestorAsync(path, remote.Head, state.Head ?? string.Empty, token))
                        throw new InvalidOperationException("The checkout and upstream diverged. GS-04 review is required.");
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
        ProjectSnapshot incoming = codec.Decode(state.SnapshotFiles);
        ProjectId projectId = new(incoming.Project.Id);
        ProjectSnapshotRead current = await snapshots.ReadAsync(projectId, token);
        string hash = codec.Encode(incoming).Sha256;
        if (current.Snapshot is not null)
        {
            if (codec.Encode(current.Snapshot).Sha256 != hash)
                throw new InvalidOperationException("This Project ID already exists with different data. GS-04 reconciliation is required.");
            if (existingLinks.Any(link => link.ProjectId == projectId.Value))
                throw new InvalidOperationException("This Project is already linked. Unlink it before selecting another repository.");
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
        await backup.CreatePreApplyBackupAsync(token);
        long revision = await snapshots.ApplyAsync(incoming, current.Revision, displayName, token);
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
                CopiedFromTrackerId: null, ImportSummary: null } tracker &&
            tracker.Entities.Count == 0 && tracker.StatusHistory.Count == 0 &&
            tracker.ProgressHistory.Count <= 1 &&
            tracker.ProgressHistory.All(progress => progress.ReadyCount == 0 &&
                progress.BlockedCount == 0 && progress.InProgressCount == 0 &&
                progress.ReworkNeededCount == 0 && progress.DevelopmentCompletedCount == 0 &&
                progress.ReconciledCount == 0);
    }

    public Task<ProjectSyncLink> SyncNowAsync(ProjectId projectId, CancellationToken token = default) =>
        SyncNowAsync(projectId, null, token);

    public async Task<ProjectSyncLink> SyncNowAsync(ProjectId projectId, string? localName,
        CancellationToken token = default)
    {
        ProjectSyncLink link = await GetLinkAsync(projectId, token) ??
            throw new InvalidOperationException("This Project is not linked to a repository.");
        await using IAsyncDisposable guard = await git.LockAsync(link.RepositoryPath, token);
        GitWorkingTreeState state = await git.InspectAsync(link.RepositoryPath, token);
        if (!PathEquals(state.RootPath, link.RepositoryPath) || state.Branch != link.Branch ||
            state.UpstreamIdentity != link.UpstreamIdentity)
            throw new InvalidOperationException("Repository root, branch, or upstream changed. Restore the linked configuration externally or unlink and relink.");
        ProjectSnapshotRead read = await snapshots.ReadAsync(projectId, token);
        if (read.Snapshot is null)
            throw new InvalidOperationException("The Project was purged. Publishing a deletion requires a later milestone.");
        ProjectSnapshotPackage local = codec.Encode(read.Snapshot);
        if (localName is not null &&
            (string.IsNullOrWhiteSpace(localName) ||
             (projects is not null && await projects.IsNameReservedAsync(localName, projectId, token))))
            throw new ProjectNameCollisionException(localName);
        GitRemoteState? remote = null;
        if (state.UpstreamIdentity is not null)
        {
            remote = await git.FetchAsync(link.RepositoryPath, token);
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
                    if (backup is null)
                        throw new InvalidOperationException("A pre-apply backup service is required for inbound sync.");
                    await backup.CreatePreApplyBackupAsync(token);
                    long applied = await snapshots.ApplyAsync(resumed, read.Revision, localName, token);
                    read = new ProjectSnapshotRead(resumed, applied);
                    local = codec.Encode(resumed);
                }
                else if (resumedHash != local.Sha256)
                    throw new InvalidOperationException("Local and upstream Project changes need GS-04 review.");
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
                    throw new InvalidOperationException("Local and upstream Project changes need GS-04 review.");
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
                await git.FastForwardAsync(link.RepositoryPath, state.Head, remote.Head, token);
                state = await git.InspectAsync(link.RepositoryPath, token);
                if (inbound is not null && remoteChanged && !localChanged)
                {
                    if (backup is null)
                        throw new InvalidOperationException("A pre-apply backup service is required for inbound sync.");
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
                throw new InvalidOperationException("The checkout and upstream diverged. GS-04 review is required.");
        }
        if (state.Head != link.LastCommonCommit)
            throw new InvalidOperationException("Repository HEAD changed since the last sync. Review it outside EntityTracker before relinking.");
        if (state.SnapshotFiles.Count > 0)
        {
            ProjectSnapshot repositorySnapshot = codec.Decode(state.SnapshotFiles);
            if (repositorySnapshot.Project.Id != projectId.Value)
                throw new InvalidOperationException("The repository belongs to another Project.");
            ProjectSnapshotPackage basePackage = codec.Encode(repositorySnapshot);
            if (link.LastSnapshotHash != basePackage.Sha256)
                throw new InvalidOperationException("The repository snapshot changed since linking. Conflict review is required.");
            IReadOnlyList<string> deletions = FindDeletions(repositorySnapshot, read.Snapshot!);
            if (deletions.Count > 0)
            {
                if (!await deletionApproval.ApproveAsync(deletions, token))
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
            head = await git.CommitSnapshotAsync(link.RepositoryPath, state.SnapshotFiles, local, token);
        }
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
                try { await git.PushAsync(link.RepositoryPath, token); }
                catch (InvalidOperationException)
                {
                    GitRemoteState refreshed = await git.FetchAsync(link.RepositoryPath, token);
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
                            if (backup is null)
                                throw new InvalidOperationException("A pre-apply backup service is required for inbound sync.");
                            await backup.CreatePreApplyBackupAsync(token);
                            long applied = await snapshots.ApplyAsync(advanced, read.Revision, localName, token);
                            read = new ProjectSnapshotRead(advanced, applied);
                        }
                        updated = updated with { LastCommonCommit = refreshed.Head,
                            LastRevision = read.Revision, LastSnapshotHash = advancedHash,
                            SyncStatus = "PendingRemote",
                            LastResult = "Upstream advanced after push; validating latest state." };
                        await links.SaveAsync(updated, token);
                        GitRemoteState rechecked = await git.FetchAsync(link.RepositoryPath, token);
                        ProjectSnapshotRead latestRead = await snapshots.ReadAsync(projectId, token);
                        if (rechecked.Head != refreshed.Head || latestRead.Revision != read.Revision)
                            return updated;
                        updated = updated with { SyncStatus = "Current",
                            LastResult = "Upstream advanced after push; fast-forward applied" };
                        await links.SaveAsync(updated, token);
                        return updated;
                    }
                    else if (refreshed.Head != head)
                        throw new InvalidOperationException("The upstream advanced. GS-04 review is required; the local commit remains pending push.");
                }
            }
            else
            {
                GitRemoteState rechecked = await git.FetchAsync(link.RepositoryPath, token);
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

    public Task UnlinkAsync(ProjectId projectId, CancellationToken token = default) =>
        links.RemoveAsync(projectId.Value, token);

    private static bool PathEquals(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool PackagesEqual(IReadOnlyDictionary<string, byte[]> a, IReadOnlyDictionary<string, byte[]> b) =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out byte[]? bytes) && pair.Value.AsSpan().SequenceEqual(bytes));

    private static IReadOnlyList<string> FindDeletions(ProjectSnapshot previous, ProjectSnapshot current)
    {
        List<string> deleted = [];
        HashSet<Guid> trackerIds = current.Trackers.Select(t => t.Id).ToHashSet();
        foreach (SnapshotTracker tracker in previous.Trackers)
        {
            if (!trackerIds.Contains(tracker.Id)) { deleted.Add($"Tracker {tracker.Id:D}"); continue; }
            SnapshotTracker next = current.Trackers.Single(t => t.Id == tracker.Id);
            deleted.AddRange(tracker.Entities.Select(e => e.Id).Except(next.Entities.Select(e => e.Id)).Select(id => $"Entity {id:D}"));
            deleted.AddRange(tracker.StatusHistory.Select(e => e.EventId).Except(next.StatusHistory.Select(e => e.EventId)).Select(id => $"Status event {id:D}"));
            deleted.AddRange(tracker.ProgressHistory.Select(e => e.SnapshotId).Except(next.ProgressHistory.Select(e => e.SnapshotId)).Select(id => $"Progress record {id:D}"));
            foreach (SnapshotEntity entity in tracker.Entities)
            {
                SnapshotEntity? updated = next.Entities.SingleOrDefault(e => e.Id == entity.Id);
                if (updated is null) continue;
                deleted.AddRange(entity.Dependencies.Where(d => !updated.Dependencies.Any(n => n.DependencyEntityId == d.DependencyEntityId))
                    .Select(d => $"Dependency {d.DependentEntityId:D} → {d.DependencyEntityId:D}"));
                deleted.AddRange(entity.UnresolvedDependencies.Where(d => !updated.UnresolvedDependencies.Any(n =>
                        string.Equals(n.DependencySourceName, d.DependencySourceName, StringComparison.OrdinalIgnoreCase)))
                    .Select(d => $"Unresolved dependency {d.DependentEntityId:D} → {d.DependencySourceName}"));
                deleted.AddRange(entity.ManualOverrides.Where(d => !updated.ManualOverrides.Any(n =>
                        string.Equals(n.DependencySourceName, d.DependencySourceName, StringComparison.OrdinalIgnoreCase)))
                    .Select(d => $"Manual override {d.DependentEntityId:D} → {d.DependencySourceName}"));
            }
        }
        return deleted.Order(StringComparer.Ordinal).ToArray();
    }
}

using EntityTracker.Application.Snapshots;
using EntityTracker.Domain;

namespace EntityTracker.Application.GitSync;

public sealed record ProjectSyncLink(
    Guid ProjectId, string RepositoryPath, string Branch, string? UpstreamIdentity,
    string? LastCommonCommit, long LastRevision, string? LastSnapshotHash,
    string LastResult, string SyncStatus);

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
}

public interface IOutboundDeletionApproval
{
    Task<bool> ApproveAsync(IReadOnlyList<string> deletedObjects, CancellationToken cancellationToken = default);
}

public sealed class ProjectGitSyncService(
    IProjectSyncLinkStore links, ILocalGitTransport git, IProjectSnapshotStore snapshots,
    IProjectSnapshotCodec codec, IOutboundDeletionApproval deletionApproval)
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
            if (remote.Project.Id != projectId.Value || !PackagesEqual(local.Files, codec.Encode(remote).Files))
                throw new InvalidOperationException("The repository snapshot differs from this Project. Conflict review is required before linking.");
        }
        ProjectSyncLink link = new(projectId.Value, state.RootPath, state.Branch, state.UpstreamIdentity,
            state.Head, state.SnapshotFiles.Count == 0 ? 0 : read.Revision,
            state.SnapshotFiles.Count == 0 ? null : local.Sha256,
            state.SnapshotFiles.Count == 0 ? "Linked; initial snapshot pending" : "Linked; snapshot matches",
            state.SnapshotFiles.Count == 0 ? "Pending" : "Current");
        await links.SaveAsync(link, token);
        return link;
    }

    public async Task<ProjectSyncLink> SyncNowAsync(ProjectId projectId, CancellationToken token = default)
    {
        ProjectSyncLink link = await GetLinkAsync(projectId, token) ??
            throw new InvalidOperationException("This Project is not linked to a repository.");
        await using IAsyncDisposable guard = await git.LockAsync(link.RepositoryPath, token);
        GitWorkingTreeState state = await git.InspectAsync(link.RepositoryPath, token);
        if (!PathEquals(state.RootPath, link.RepositoryPath) || state.Branch != link.Branch ||
            state.UpstreamIdentity != link.UpstreamIdentity)
            throw new InvalidOperationException("Repository root, branch, or upstream changed. Restore the linked configuration externally or unlink and relink.");
        if (state.Head != link.LastCommonCommit)
            throw new InvalidOperationException("Repository HEAD changed since the last sync. Review the new commit before relinking.");
        ProjectSnapshotRead read = await snapshots.ReadAsync(projectId, token);
        if (read.Snapshot is null)
            throw new InvalidOperationException("The Project was purged. Publishing a deletion requires a later milestone.");
        ProjectSnapshotPackage local = codec.Encode(read.Snapshot);
        if (state.SnapshotFiles.Count > 0)
        {
            ProjectSnapshot remote = codec.Decode(state.SnapshotFiles);
            if (remote.Project.Id != projectId.Value)
                throw new InvalidOperationException("The repository belongs to another Project.");
            ProjectSnapshotPackage basePackage = codec.Encode(remote);
            if (link.LastSnapshotHash != basePackage.Sha256)
                throw new InvalidOperationException("The repository snapshot changed since linking. Conflict review is required.");
            IReadOnlyList<string> deletions = FindDeletions(remote, read.Snapshot);
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
        ProjectSyncLink updated = link with
        {
            LastCommonCommit = string.IsNullOrEmpty(head) ? null : head,
            LastRevision = read.Revision,
            LastSnapshotHash = local.Sha256,
            LastResult = unchanged ? "Already current" : "Local snapshot committed",
            SyncStatus = "Current"
        };
        await links.SaveAsync(updated, token);
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

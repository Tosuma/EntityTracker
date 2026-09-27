using System.Security.Cryptography;
using System.Text;
using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Snapshots;
using EntityTracker.Domain;
using EntityTracker.Domain.Collaboration;

namespace EntityTracker.Application.GitSync;

public interface IProjectMergeReview
{
    Task<IReadOnlyDictionary<string, MergeSide>?> ReviewAsync(
        IReadOnlyList<ProjectMergeConflict> conflicts, CancellationToken cancellationToken = default);
}

public sealed partial class ProjectGitSyncService
{
    private async Task<ProjectSnapshotRead> ReviewTwoWayAsync(ProjectId projectId,
        ProjectSnapshotRead read, ProjectSnapshot incoming, GitWorkingTreeState state,
        CancellationToken token)
    {
        if (read.Snapshot is null || mergeReview is null)
            throw new InvalidOperationException("This Project ID already exists with different data. Conflict review is required.");
        ProjectMergeResult proposal = new ProjectSnapshotMerger().Merge(null, read.Snapshot, incoming);
        IReadOnlyDictionary<string, MergeSide>? decisions = await mergeReview.ReviewAsync(proposal.Conflicts, token);
        if (decisions is null || proposal.Conflicts.Any(c => !decisions.ContainsKey(c.Path)))
            throw new ProjectSyncReviewCancelledException("Two-way reconciliation was cancelled.");
        ProjectSnapshot merged = new ProjectSnapshotMerger(decisions)
            .Merge(null, read.Snapshot, incoming).Snapshot;
        ProjectSnapshotValidator.Validate(merged);
        ProjectSnapshotRead current = await snapshots.ReadAsync(projectId, token);
        GitWorkingTreeState checkout = await git.InspectAsync(state.RootPath, token);
        if (current.Revision != read.Revision || checkout.Head != state.Head ||
            !PackagesEqual(checkout.SnapshotFiles, state.SnapshotFiles))
            throw new InvalidOperationException("The Project or checkout changed during reconciliation.");
        IReadOnlyList<string> deleted = FindDeletions(incoming, merged);
        if (deleted.Count > 0)
        {
            if (!await deletionApproval.ApproveAsync(deleted, token))
                throw new InvalidOperationException("Outbound deletions were not approved.");
            current = await snapshots.ReadAsync(projectId, token);
            checkout = await git.InspectAsync(state.RootPath, token);
            if (current.Revision != read.Revision || checkout.Head != state.Head)
                throw new InvalidOperationException("Deletion approval became stale.");
        }
        if (codec.Encode(merged).Sha256 == codec.Encode(read.Snapshot).Sha256) return read;
        if (backup is null) throw new InvalidOperationException("A backup is required for reconciliation.");
        await backup.CreatePreApplyBackupAsync(token);
        long revision = await snapshots.ApplyAsync(merged, read.Revision, token);
        return new ProjectSnapshotRead(merged, revision);
    }

    public async Task<IReadOnlyList<ProjectSyncLink>> ListPendingDeletionsAsync(
        CancellationToken token = default) =>
        (await links.ReadAllAsync(token)).Where(link => link.PendingDeletion is not null)
            .OrderBy(link => link.ProjectId).ToArray();

    public async Task<bool> PurgeLinkedProjectAsync(ProjectId projectId,
        CancellationToken token = default)
    {
        ProjectSyncLink? link = await GetLinkAsync(projectId, token);
        if (link is null || link.SyncStatus == "RemoteDeletedLocalKept") return false;
        await using IAsyncDisposable guard = await git.LockAsync(link.RepositoryPath, token);
        GitWorkingTreeState state = await git.InspectAsync(link.RepositoryPath, token);
        if (state.Branch != link.Branch || state.UpstreamIdentity != link.UpstreamIdentity ||
            state.Head != link.LastCommonCommit)
            throw new InvalidOperationException("The linked repository changed. Sync before deleting this Project.");
        ProjectSnapshotRead read = await snapshots.ReadAsync(projectId, token);
        if (read.Snapshot is null)
            throw new InvalidOperationException("The Project has already been deleted.");
        if (read.Snapshot.Project.LifecycleState != "Recycled")
            throw new InvalidOperationException("Recycle the Project before permanently deleting it.");
        if (state.SnapshotFiles.Count == 0)
            throw new InvalidOperationException("Sync the Project snapshot before publishing its deletion.");
        ProjectSnapshot shared = codec.Decode(state.SnapshotFiles);
        if (shared.Project.Id != projectId.Value)
            throw new InvalidOperationException("The repository belongs to another Project.");
        if (!await deletionApproval.ApproveAsync(FullDeletionSet(read.Snapshot), token))
            throw new InvalidOperationException("Project deletion publication was cancelled.");
        ProjectSnapshotRead recheck = await snapshots.ReadAsync(projectId, token);
        GitWorkingTreeState gitRecheck = await git.InspectAsync(link.RepositoryPath, token);
        if (recheck.Revision != read.Revision || gitRecheck.Head != state.Head ||
            !PackagesEqual(gitRecheck.SnapshotFiles, state.SnapshotFiles))
            throw new InvalidOperationException("The deletion approval became stale. Retry.");
        ProjectDeletionIntent intent = new(projectId.Value, codec.Encode(shared).Sha256,
            DateTimeOffset.UtcNow, read.Revision);
        await links.SaveAsync(link with { PendingDeletion = intent,
            LastResult = "Project deleted locally; tombstone publication pending",
            SyncStatus = "PendingDeletion" }, token);
        try { await snapshots.PurgeAsync(projectId, read.Revision, token); }
        catch
        {
            await links.SaveAsync(link, CancellationToken.None);
            throw;
        }
        return true;
    }

    private async Task<ProjectSyncLink> PublishTombstoneAsync(ProjectSyncLink link,
        GitWorkingTreeState state, CancellationToken token, int attempt = 0,
        IProgress<ProjectSyncPhase>? progress = null)
    {
        if (attempt >= 4)
            throw new InvalidOperationException("The upstream kept advancing during deletion publication. Retry sync.");
        ProjectDeletionIntent intent = link.PendingDeletion ??
            throw new InvalidOperationException("No Project deletion is pending.");
        ProjectTombstone tombstone = new(ProjectSnapshot.CurrentFormatVersion,
            intent.ProjectId, intent.BaseSnapshotHash, intent.DeletedAtUtc);
        ProjectSnapshotPackage package = codec.EncodeTombstone(tombstone);
        bool committed = codec.TryDecodeTombstone(state.SnapshotFiles,
            out ProjectTombstone? existing);
        if (committed && existing != tombstone)
            throw new InvalidOperationException("The checkout has a different Project tombstone.");
        if (!committed && state.Head != link.LastCommonCommit)
            throw new InvalidOperationException("The checkout advanced before deletion publication.");
        string head = state.Head ?? throw new InvalidOperationException("A committed Project base is required.");
        if (state.UpstreamIdentity is not null) progress?.Report(ProjectSyncPhase.Fetching);
        GitRemoteState? remote = state.UpstreamIdentity is null ? null :
            await git.FetchAsync(link.RepositoryPath, token);
        if (remote is not null && remote.Head != head)
        {
            await git.ValidateMergeScopeAsync(link.RepositoryPath, head, remote.Head, token);
            if (codec.TryDecodeTombstone(remote.SnapshotFiles,
                    out ProjectTombstone? remoteTombstone))
            {
                if (remoteTombstone != tombstone)
                    throw new InvalidOperationException("The upstream published a different Project tombstone.");
                if (await git.IsAncestorAsync(link.RepositoryPath, head, remote.Head, token))
                {
                    await git.FastForwardAsync(link.RepositoryPath, head, remote.Head, token);
                    head = remote.Head;
                    committed = true;
                }
            }
            else
            {
                progress?.Report(ProjectSyncPhase.Reviewing);
                ProjectSnapshot remoteSnapshot = codec.Decode(remote.SnapshotFiles);
                if (remoteSnapshot.Project.Id != intent.ProjectId)
                    throw new InvalidDataException("The upstream belongs to another Project.");
                if (!await deletionApproval.ApproveAsync(FullDeletionSet(remoteSnapshot), token))
                    throw new InvalidOperationException("Updated upstream deletion was not approved.");
                GitRemoteState recheck = await git.FetchAsync(link.RepositoryPath, token);
                GitWorkingTreeState checkout = await git.InspectAsync(link.RepositoryPath, token);
                if (recheck.Head != remote.Head || checkout.Head != state.Head ||
                    !PackagesEqual(checkout.SnapshotFiles, state.SnapshotFiles))
                    throw new InvalidOperationException("Deletion approval became stale. Retry sync.");
            }
            if (head != remote.Head)
            {
                progress?.Report(ProjectSyncPhase.Committing);
                head = await git.CommitMergeSnapshotAsync(link.RepositoryPath, head,
                    remote.Head, state.SnapshotFiles, package, token);
                committed = true;
            }
        }
        if (!committed)
        {
            progress?.Report(ProjectSyncPhase.Committing);
            head = await git.CommitSnapshotAsync(link.RepositoryPath, state.SnapshotFiles, package, token);
        }
        link = link with { LastCommonCommit = head, LastSnapshotHash = package.Sha256,
            LastResult = "Project tombstone committed; push pending", SyncStatus = "PendingPush" };
        await links.SaveAsync(link, token);
        if (remote is not null && remote.Head != head)
        {
            progress?.Report(ProjectSyncPhase.Pushing);
            try { await git.PushAsync(link.RepositoryPath, token); }
            catch (InvalidOperationException)
            {
                GitWorkingTreeState next = await git.InspectAsync(link.RepositoryPath, token);
                return await PublishTombstoneAsync(link, next, token, attempt + 1, progress);
            }
        }
        await links.RemoveAsync(link.ProjectId, token);
        return link with { LastCommonCommit = head, LastSnapshotHash = package.Sha256,
            LastResult = "Project deletion published", SyncStatus = "Deleted", PendingDeletion = null };
    }

    private async Task<ProjectSyncLink> ReceiveTombstoneAsync(ProjectId projectId,
        ProjectSyncLink link, GitWorkingTreeState state, GitRemoteState remote,
        ProjectTombstone tombstone, ProjectSnapshotRead read, CancellationToken token,
        IProgress<ProjectSyncPhase>? progress = null)
    {
        if (tombstone.ProjectId != projectId.Value)
            throw new InvalidDataException("The upstream deleted a different Project.");
        if (read.Snapshot is null)
        {
            await links.RemoveAsync(projectId.Value, token);
            return link with { SyncStatus = "Deleted", LastResult = "Published deletion received" };
        }
        if (state.Head is not null && state.Head != remote.Head &&
            !await git.IsAncestorAsync(link.RepositoryPath, state.Head, remote.Head, token))
            await git.ValidateMergeScopeAsync(link.RepositoryPath, state.Head, remote.Head, token);
        string localHash = codec.Encode(read.Snapshot).Sha256;
        bool localChanged = read.Revision != link.LastRevision ||
            localHash != link.LastSnapshotHash;
        if (localChanged)
        {
            string path = "Project/Deletion";
            ProjectMergeConflict conflict = new(path, ProjectConflictKind.Deletion,
                tombstone.BaseSnapshotHash, localHash, "Project deleted from the shared repository");
            progress?.Report(ProjectSyncPhase.Reviewing);
            IReadOnlyDictionary<string, MergeSide>? decision = mergeReview is null
                ? null : await mergeReview.ReviewAsync([conflict], token);
            if (decision is null || !decision.TryGetValue(path, out MergeSide side))
                throw new ProjectSyncReviewCancelledException("Project deletion review was cancelled.");
            if (side == MergeSide.Local)
            {
                ProjectSyncLink kept = link with { SyncStatus = "RemoteDeletedLocalKept",
                    LastResult = "Shared repository Project was deleted; local changes kept and link removed" };
                await links.SaveAsync(kept, token);
                return kept;
            }
        }
        ProjectSnapshotRead current = await snapshots.ReadAsync(projectId, token);
        GitRemoteState head = await git.FetchAsync(link.RepositoryPath, token);
        if (current.Revision != read.Revision || head.Head != remote.Head)
            throw new InvalidOperationException("The deletion review became stale. Retry sync.");
        if (editGate is not null) await editGate.WaitUntilReadyAsync(projectId, token);
        if (backup is null) throw new InvalidOperationException("A backup service is required for inbound deletion.");
        progress?.Report(ProjectSyncPhase.Applying);
        await backup.CreatePreApplyBackupAsync(token);
        await snapshots.ApplyTombstoneAsync(projectId, read.Revision, token);
        if (state.Head != remote.Head)
        {
            if (state.Head is null)
                throw new InvalidOperationException("The checkout has no committed Project base.");
            if (await git.IsAncestorAsync(link.RepositoryPath, state.Head, remote.Head, token))
                await git.FastForwardAsync(link.RepositoryPath, state.Head, remote.Head, token);
            else
            {
                ProjectSnapshotPackage terminal = codec.EncodeTombstone(tombstone);
                progress?.Report(ProjectSyncPhase.Committing);
                await git.CommitMergeSnapshotAsync(link.RepositoryPath, state.Head,
                    remote.Head, state.SnapshotFiles, terminal, token);
                progress?.Report(ProjectSyncPhase.Pushing);
                await git.PushAsync(link.RepositoryPath, token);
            }
        }
        await links.RemoveAsync(projectId.Value, token);
        return link with { SyncStatus = "Deleted", LastResult = "Published deletion received" };
    }

    private async Task<ProjectSyncLink> SyncConcurrentAsync(ProjectId projectId,
        ProjectSyncLink link, GitWorkingTreeState state, GitRemoteState remote,
        ProjectSnapshotRead read, string? localName, CancellationToken token,
        IProgress<ProjectSyncPhase>? progress = null, int attempt = 0)
    {
        progress?.Report(ProjectSyncPhase.Reviewing);
        if (attempt >= 4) throw new InvalidOperationException("The upstream kept advancing. Retry sync.");
        if (state.Head is null || read.Snapshot is null)
            throw new InvalidOperationException("A committed local Project and SQLite snapshot are required for merge.");
        if (remote.SnapshotFiles.Count == 0)
            throw new InvalidOperationException("The upstream Project snapshot is missing.");
        await git.ValidateMergeScopeAsync(link.RepositoryPath, state.Head, remote.Head, token);
        ProjectSnapshot remoteSnapshot = codec.Decode(remote.SnapshotFiles);
        if (remoteSnapshot.Project.Id != projectId.Value)
            throw new InvalidOperationException("The upstream belongs to another Project.");
        bool sameHead = state.Head == remote.Head;
        string? baseCommit = sameHead ? link.LastCommonCommit :
            await git.MergeBaseAsync(link.RepositoryPath, state.Head, remote.Head, token);
        ProjectSnapshot? basis = null;
        if (baseCommit is not null)
        {
            IReadOnlyDictionary<string, byte[]> baseFiles = await git.ReadSnapshotAtCommitAsync(
                link.RepositoryPath, baseCommit, token);
            if (baseFiles.Count > 0) basis = codec.Decode(baseFiles);
        }
        ProjectMergeResult proposal = new ProjectSnapshotMerger().Merge(
            basis, read.Snapshot, remoteSnapshot);
        IReadOnlyDictionary<string, MergeSide>? decisions = proposal.Conflicts.Count == 0
            ? new Dictionary<string, MergeSide>()
            : mergeReview is null ? null : await mergeReview.ReviewAsync(proposal.Conflicts, token);
        if (decisions is null)
            throw new ProjectSyncReviewCancelledException("Merge review was cancelled; no Project data was changed.");
        if (proposal.Conflicts.Any(c => !decisions.ContainsKey(c.Path)))
            throw new ProjectSyncReviewRequiredException("Resolve every merge conflict before syncing.");
        ProjectSnapshot merged = new ProjectSnapshotMerger(decisions)
            .Merge(basis, read.Snapshot, remoteSnapshot).Snapshot;
        merged = AppendMergedProgress(merged, state.Head, remote.Head);
        ProjectSnapshotValidator.Validate(merged);
        ProjectSnapshotRead latest = await snapshots.ReadAsync(projectId, token);
        if (latest.Revision != read.Revision || latest.Snapshot is null ||
            codec.Encode(latest.Snapshot).Sha256 != codec.Encode(read.Snapshot).Sha256)
            throw new InvalidOperationException("The SQLite Project changed during review. Retry sync.");
        GitRemoteState recheck = await git.FetchAsync(link.RepositoryPath, token);
        GitWorkingTreeState checkout = await git.InspectAsync(link.RepositoryPath, token);
        if (recheck.Head != remote.Head || checkout.Head != state.Head ||
            checkout.Branch != link.Branch || checkout.UpstreamIdentity != link.UpstreamIdentity ||
            !PackagesEqual(checkout.SnapshotFiles, state.SnapshotFiles))
            throw new InvalidOperationException("The repository changed during review. Retry sync.");
        IReadOnlyList<string> deletions = FindDeletions(remoteSnapshot, merged);
        if (deletions.Count > 0)
        {
            if (!await deletionApproval.ApproveAsync(deletions, token))
                throw new InvalidOperationException("Outbound deletions were not approved.");
            latest = await snapshots.ReadAsync(projectId, token);
            recheck = await git.FetchAsync(link.RepositoryPath, token);
            if (latest.Revision != read.Revision || recheck.Head != remote.Head)
                throw new InvalidOperationException("Deletion approval became stale. Retry sync.");
        }
        string mergedHash = codec.Encode(merged).Sha256;
        if (mergedHash != codec.Encode(read.Snapshot).Sha256)
        {
            if (editGate is not null) await editGate.WaitUntilReadyAsync(projectId, token);
            if (backup is null) throw new InvalidOperationException("A pre-apply backup service is required for merge.");
            progress?.Report(ProjectSyncPhase.Applying);
            await backup.CreatePreApplyBackupAsync(token);
            long revision = await snapshots.ApplyAsync(merged, read.Revision, localName, token);
            read = await snapshots.ReadAsync(projectId, token);
            if (read.Revision != revision || read.Snapshot is null)
                throw new InvalidOperationException("The merged Project could not be re-exported.");
        }
        ProjectSnapshotPackage canonical = codec.Encode(read.Snapshot!);
        progress?.Report(ProjectSyncPhase.Committing);
        string head = sameHead
            ? await git.CommitSnapshotAsync(link.RepositoryPath, state.SnapshotFiles, canonical, token)
            : await git.CommitMergeSnapshotAsync(link.RepositoryPath,
                state.Head, remote.Head, state.SnapshotFiles, canonical, token);
        ProjectSyncLink pending = link with { LastCommonCommit = head,
            LastRevision = read.Revision, LastSnapshotHash = canonical.Sha256,
            LastResult = "Merged snapshot committed; push pending", SyncStatus = "PendingPush" };
        await links.SaveAsync(pending, token);
        progress?.Report(ProjectSyncPhase.Pushing);
        try { await git.PushAsync(link.RepositoryPath, token); }
        catch (InvalidOperationException)
        {
            GitRemoteState advanced = await git.FetchAsync(link.RepositoryPath, token);
            if (advanced.Head == head) return await CompleteMergeAsync(pending, token);
            GitWorkingTreeState next = await git.InspectAsync(link.RepositoryPath, token);
            return await SyncConcurrentAsync(projectId, pending, next, advanced,
                await snapshots.ReadAsync(projectId, token), localName, token, progress, attempt + 1);
        }
        return await CompleteMergeAsync(pending, token);
    }

    private async Task<ProjectSyncLink> CompleteMergeAsync(ProjectSyncLink link, CancellationToken token)
    {
        ProjectSyncLink current = link with { LastResult = "Collaborative merge pushed",
            SyncStatus = "Current" };
        await links.SaveAsync(current, token);
        return current;
    }

    private static IReadOnlyList<string> FullDeletionSet(ProjectSnapshot snapshot)
    {
        List<string> deleted = [$"Project {snapshot.Project.Id:D}"];
        foreach (SnapshotTracker tracker in snapshot.Trackers)
        {
            deleted.Add($"Tracker {tracker.Id:D}");
            deleted.AddRange(tracker.Entities.Select(e => $"Entity {e.Id:D}"));
            deleted.AddRange(tracker.StatusHistory.Select(e => $"Status event {e.EventId:D}"));
            deleted.AddRange(tracker.ProgressHistory.Select(e => $"Progress record {e.SnapshotId:D}"));
            if (tracker.ImportSummary is not null)
                deleted.Add($"Import summary {tracker.Id:D}");
            foreach (SnapshotEntity entity in tracker.Entities)
                deleted.AddRange(RelationshipDeletions(entity));
        }
        return deleted.Order(StringComparer.Ordinal).ToArray();
    }

    private static ProjectSnapshot AppendMergedProgress(ProjectSnapshot snapshot,
        string localHead, string remoteHead)
    {
        SnapshotTracker[] trackers = snapshot.Trackers.Select(tracker =>
        {
            SnapshotProgress? last = tracker.ProgressHistory.OrderBy(p => p.RecordedAtUtc)
                .ThenBy(p => p.SnapshotId).LastOrDefault();
            DateTimeOffset time = last?.RecordedAtUtc.AddTicks(1) ??
                (tracker.UpdatedAtUtc > snapshot.Project.UpdatedAtUtc ?
                    tracker.UpdatedAtUtc : snapshot.Project.UpdatedAtUtc).AddTicks(1);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
                string.Join(":", new[] { localHead, remoteHead }.Order(StringComparer.Ordinal)) +
                ":" + tracker.Id.ToString("D")));
            byte[] idBytes = hash[..16];
            idBytes[7] = (byte)((idBytes[7] & 0x0f) | 0x50);
            idBytes[8] = (byte)((idBytes[8] & 0x3f) | 0x80);
            Guid id = new(idBytes);
            if (tracker.ProgressHistory.Any(p => p.SnapshotId == id)) return tracker;
            TrackedEntity[] entities = tracker.Entities.Select(e => new TrackedEntity(
                new EntityId(e.Id), new TrackerId(tracker.Id), e.SourceName,
                Enum.Parse<DevelopmentStatus>(e.DevelopmentStatus), e.Notes,
                Enum.Parse<EntityLifecycleState>(e.LifecycleState),
                Enum.Parse<EntityProvenance>(e.Provenance), e.RequestedPriority,
                e.ResponsibleDeveloper, e.GroupName)).ToArray();
            EffectiveDependencyState dependencies = new EffectiveDependencyResolver().Resolve(
                entities,
                tracker.Entities.SelectMany(e => e.Dependencies).Select(d =>
                    new PersistedDependency(new DependencyEdge(new EntityId(d.DependentEntityId),
                        new EntityId(d.DependencyEntityId)), Enum.Parse<ImportedDependencyKind>(d.Kind))),
                tracker.Entities.SelectMany(e => e.UnresolvedDependencies).Select(d =>
                    new PersistedUnresolvedDependency(new UnresolvedDependency(
                        new EntityId(d.DependentEntityId), d.DependencySourceName),
                        Enum.Parse<ImportedDependencyKind>(d.Kind))),
                tracker.Entities.SelectMany(e => e.ManualOverrides).Select(d =>
                    new ManualDependencyOverride(new EntityId(d.DependentEntityId),
                        d.DependencySourceName, Enum.Parse<ManualDependencyOverrideAction>(d.Action))));
            ProgressSnapshotState counts = new ProgressSnapshotCalculator().Calculate(entities, dependencies);
            SnapshotProgress next = new(id, time,
                counts.ReadyCount, counts.BlockedCount, counts.InProgressCount,
                counts.ReworkNeededCount, counts.DevelopmentCompletedCount,
                counts.ReconciledCount, tracker.ProgressHistory.Count);
            return tracker with { ProgressHistory = tracker.ProgressHistory.Append(next).ToArray() };
        }).ToArray();
        return snapshot with { Trackers = trackers };
    }
}

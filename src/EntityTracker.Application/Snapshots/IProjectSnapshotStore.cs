using EntityTracker.Domain;

namespace EntityTracker.Application.Snapshots;

public sealed record ProjectSnapshotRead(ProjectSnapshot? Snapshot, long Revision);

public interface IProjectSnapshotStore
{
    Task<ProjectSnapshotRead> ReadAsync(ProjectId projectId, CancellationToken cancellationToken = default);

    Task<long> ApplyAsync(
        ProjectSnapshot snapshot, long expectedRevision,
        CancellationToken cancellationToken = default);

    Task<long> ApplyAsync(ProjectSnapshot snapshot, long expectedRevision, string? localName,
        CancellationToken cancellationToken = default) => localName is null
            ? ApplyAsync(snapshot, expectedRevision, cancellationToken)
            : throw new NotSupportedException("This snapshot store does not support local Project names.");

    Task PurgeAsync(ProjectId projectId, long expectedRevision,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Revision-checked Project purge is unavailable.");

    Task ApplyTombstoneAsync(ProjectId projectId, long expectedRevision,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Transactional inbound Project deletion is unavailable.");

}

public sealed record ProjectSnapshotPackage(
    IReadOnlyDictionary<string, byte[]> Files,
    string Sha256);

public interface IProjectSnapshotCodec
{
    ProjectSnapshotPackage Encode(ProjectSnapshot snapshot);

    ProjectSnapshot Decode(IReadOnlyDictionary<string, byte[]> files);

    ProjectSnapshotPackage EncodeTombstone(EntityTracker.Application.GitSync.ProjectTombstone tombstone) =>
        throw new NotSupportedException("Project tombstones are unavailable.");

    bool TryDecodeTombstone(IReadOnlyDictionary<string, byte[]> files,
        out EntityTracker.Application.GitSync.ProjectTombstone? tombstone)
    {
        tombstone = null;
        return false;
    }
}

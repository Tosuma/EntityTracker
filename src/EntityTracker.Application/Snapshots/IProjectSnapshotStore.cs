using EntityTracker.Domain;

namespace EntityTracker.Application.Snapshots;

public sealed record ProjectSnapshotRead(ProjectSnapshot? Snapshot, long Revision);

public interface IProjectSnapshotStore
{
    Task<ProjectSnapshotRead> ReadAsync(ProjectId projectId, CancellationToken cancellationToken = default);

    Task<long> ApplyAsync(
        ProjectSnapshot snapshot, long expectedRevision,
        CancellationToken cancellationToken = default);
}

public sealed record ProjectSnapshotPackage(
    IReadOnlyDictionary<string, byte[]> Files,
    string Sha256);

public interface IProjectSnapshotCodec
{
    ProjectSnapshotPackage Encode(ProjectSnapshot snapshot);

    ProjectSnapshot Decode(IReadOnlyDictionary<string, byte[]> files);
}

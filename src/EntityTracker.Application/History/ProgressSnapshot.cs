namespace EntityTracker.Application.History;

public sealed record ProgressSnapshot
{
    public ProgressSnapshot(DateTimeOffset recordedAtUtc, ProgressSnapshotState state, Guid snapshotId = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (recordedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Progress snapshot timestamps must be UTC.", nameof(recordedAtUtc));
        }

        RecordedAtUtc = recordedAtUtc;
        State = state;
        SnapshotId = snapshotId == Guid.Empty ? Guid.NewGuid() : snapshotId;
    }

    public DateTimeOffset RecordedAtUtc { get; }
    public Guid SnapshotId { get; }
    public ProgressSnapshotState State { get; }
}

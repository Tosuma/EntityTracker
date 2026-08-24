namespace EntityTracker.Application.History;

public sealed record ProgressSnapshot
{
    public ProgressSnapshot(DateTimeOffset recordedAtUtc, ProgressSnapshotState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (recordedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Progress snapshot timestamps must be UTC.", nameof(recordedAtUtc));
        }

        RecordedAtUtc = recordedAtUtc;
        State = state;
    }

    public DateTimeOffset RecordedAtUtc { get; }
    public ProgressSnapshotState State { get; }
}

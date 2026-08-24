namespace EntityTracker.Domain.Tests;

public sealed class EntityStatusHistoryEntryTests
{
    [Fact]
    public void Constructor_AcceptsBaselineAndTransitionShapes()
    {
        EntityId id = EntityId.New();
        DateTimeOffset timestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        EntityStatusHistoryEntry baseline = new(
            id,
            null,
            DevelopmentStatus.ReworkNeeded,
            timestamp,
            StatusHistoryEntryKind.Baseline);
        EntityStatusHistoryEntry transition = new(
            id,
            DevelopmentStatus.DevelopmentCompleted,
            DevelopmentStatus.ReworkNeeded,
            timestamp,
            StatusHistoryEntryKind.Transition);

        Assert.Null(baseline.PreviousStatus);
        Assert.Equal(DevelopmentStatus.ReworkNeeded, transition.NewStatus);
    }

    [Fact]
    public void Constructor_RejectsNonUtcAndInvalidTransitionShape()
    {
        EntityId id = EntityId.New();

        Assert.Throws<ArgumentException>(() => new EntityStatusHistoryEntry(
            id,
            null,
            DevelopmentStatus.NotStarted,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(1)),
            StatusHistoryEntryKind.Baseline));
        Assert.Throws<ArgumentException>(() => new EntityStatusHistoryEntry(
            id,
            null,
            DevelopmentStatus.InProgress,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            StatusHistoryEntryKind.Transition));
    }
}

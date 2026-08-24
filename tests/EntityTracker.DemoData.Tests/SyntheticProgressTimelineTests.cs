using EntityTracker.Domain;

namespace EntityTracker.DemoData.Tests;

public sealed class SyntheticProgressTimelineTests
{
    [Fact]
    public void Create_IsDeterministicSpansRangeAndIncludesEveryStatus()
    {
        TrackedEntity[] entities = Enumerable.Range(1, 12)
            .Select(index => new TrackedEntity(
                new EntityId(Guid.Parse($"00000000-0000-0000-0000-{index:D12}")),
                $"Entity {index:D2}"))
            .ToArray();
        DateOnly start = new(2026, 1, 1);
        DateOnly end = new(2026, 3, 31);

        SyntheticProgressTimeline first = SyntheticProgressTimeline.Create(
            entities,
            start,
            end,
            TimeZoneInfo.Utc,
            seed: 42);
        SyntheticProgressTimeline second = SyntheticProgressTimeline.Create(
            entities,
            start,
            end,
            TimeZoneInfo.Utc,
            seed: 42);

        Assert.Equal(first.BaselineAtUtc, second.BaselineAtUtc);
        Assert.Equal(first.Changes, second.Changes);
        Assert.Equal(
            first.FinalStatuses.OrderBy(static item => item.Key.Value),
            second.FinalStatuses.OrderBy(static item => item.Key.Value));
        Assert.Equal(
            new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
            first.BaselineAtUtc);
        Assert.Equal(
            new DateTimeOffset(2026, 3, 31, 12, 0, 0, TimeSpan.Zero),
            first.Changes[^1].OccurredAtUtc);
        Assert.True(first.Changes.Select(static change => change.OccurredAtUtc.Date).Distinct().Count() > 10);
        Assert.All(entities, entity =>
            Assert.Contains(first.Changes, change => change.EntityId == entity.Id));
        Assert.Equal(
            Enum.GetValues<DevelopmentStatus>().Order(),
            first.FinalStatuses.Values.Distinct().Order());

        foreach (TrackedEntity entity in entities)
        {
            DevelopmentStatus lastStatus = first.Changes
                .Where(change => change.EntityId == entity.Id)
                .Last()
                .NewStatus;
            Assert.Equal(first.FinalStatuses[entity.Id], lastStatus);
        }
    }

    [Fact]
    public void Create_RejectsArchivedEntitiesAndShortRanges()
    {
        TrackedEntity archived = new(
            EntityId.New(),
            "Archived",
            lifecycleState: EntityLifecycleState.Archived);

        Assert.Throws<ArgumentException>(() => SyntheticProgressTimeline.Create(
            [archived],
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            TimeZoneInfo.Utc,
            seed: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SyntheticProgressTimeline.Create(
            [new TrackedEntity(EntityId.New(), "Active")],
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 6),
            TimeZoneInfo.Utc,
            seed: 1));
    }
}

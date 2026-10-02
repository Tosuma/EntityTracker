using EntityTracker.Domain;

namespace EntityTracker.Domain.Tests;

public sealed class ResponsibilityPeriodTests
{
    [Fact]
    public void PeriodRequiresUtcAndOrderedDatesAndPreservesIdentityWhenEnded()
    {
        EntityId entity = EntityId.New();
        DeveloperId developer = DeveloperId.New();
        DateTimeOffset start = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        Guid id = Guid.NewGuid();
        ResponsibilityPeriod open = new(id, entity, developer, start);
        Assert.True(open.IsCurrent);
        ResponsibilityPeriod closed = open.End(start.AddHours(1));
        Assert.Equal(id, closed.Id);
        Assert.Equal(start.AddHours(1), closed.EndedAtUtc);
        Assert.False(closed.IsCurrent);
        Assert.Throws<ArgumentException>(() => new ResponsibilityPeriod(Guid.NewGuid(),
            entity, developer, start.ToOffset(TimeSpan.FromHours(1))));
        Assert.Throws<ArgumentException>(() => open.End(start.AddSeconds(-1)));
    }
}

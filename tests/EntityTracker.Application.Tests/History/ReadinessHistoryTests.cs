using EntityTracker.Application.History;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests.History;

/// <summary>#179: a past day's ready and waiting entities, from the status and dependency histories.</summary>
public sealed class ReadinessHistoryTests
{
    private static readonly DateOnly Day1 = new(2026, 3, 2);
    private static readonly EntityId Customer = new(Guid.Parse("00000000-0000-0000-0000-000000000001"));
    private static readonly EntityId Invoice = new(Guid.Parse("00000000-0000-0000-0000-000000000002"));
    private static readonly EntityId Order = new(Guid.Parse("00000000-0000-0000-0000-000000000003"));

    [Fact]
    public void APastDayIsCountedFromHowThingsWereThatDay()
    {
        // Day 1: three entities, invoice and order wait on customer. Day 2: customer is done, so both
        // become ready. Day 3: order also depends on an archived entity, so it waits again. Day 4: the
        // dependency is removed.
        EntityId archived = new(Guid.Parse("00000000-0000-0000-0000-000000000004"));
        List<EntityStatusHistoryEntry> statuses =
        [
            Created(Customer, Day1), Created(Invoice, Day1), Created(Order, Day1),
            Changed(Customer, DevelopmentStatus.NotStarted, DevelopmentStatus.InProgress, Day1),
            Changed(Customer, DevelopmentStatus.InProgress, DevelopmentStatus.DevelopmentCompleted, Day1.AddDays(1))
        ];
        List<DependencyHistoryEntry> structure =
        [
            Lifecycle(Customer, EntityLifecycleState.Active, Day1), Lifecycle(Invoice, EntityLifecycleState.Active, Day1),
            Lifecycle(Order, EntityLifecycleState.Active, Day1),
            Added(Invoice, Customer, Day1), Added(Order, Customer, Day1),
            Lifecycle(archived, EntityLifecycleState.Archived, Day1.AddDays(2)), Added(Order, archived, Day1.AddDays(2)),
            Removed(Order, archived, Day1.AddDays(3))
        ];

        IReadOnlyList<ReadinessCount> days = ReadinessHistory.Of(statuses, structure)
            .Daily(Day1, Day1.AddDays(3), TimeZoneInfo.Utc);

        Assert.Equal([(0, 2), (2, 0), (1, 1), (2, 0)], days.Select(day => (day.ReadyCount, day.WaitingCount)));
    }

    [Fact]
    public void DependenciesEnteredWhileSettingUpAreItsStartingState()
    {
        // Entities on day 1, dependencies entered on day 2, the first status change on day 4.
        List<EntityStatusHistoryEntry> statuses =
        [
            Created(Customer, Day1), Created(Invoice, Day1),
            Changed(Customer, DevelopmentStatus.NotStarted, DevelopmentStatus.InProgress, Day1.AddDays(3))
        ];
        List<DependencyHistoryEntry> structure =
        [
            Lifecycle(Customer, EntityLifecycleState.Active, Day1), Lifecycle(Invoice, EntityLifecycleState.Active, Day1),
            Added(Invoice, Customer, Day1.AddDays(1))
        ];

        IReadOnlyList<ReadinessCount> days = ReadinessHistory.Of(statuses, structure)
            .Daily(Day1, Day1.AddDays(3), TimeZoneInfo.Utc);

        // Before the first status change, every day shows the state just before it: one ready, one waiting.
        Assert.Equal([(1, 1), (1, 1), (1, 1), (0, 1)], days.Select(day => (day.ReadyCount, day.WaitingCount)));
    }

    [Fact]
    public void APlaceholderThatIsGoneStillMadeItsDependentsWait()
    {
        EntityId placeholder = new(Guid.Parse("00000000-0000-0000-0000-000000000005"));
        List<EntityStatusHistoryEntry> statuses =
        [
            Created(Customer, Day1), Created(Invoice, Day1),
            Changed(Customer, DevelopmentStatus.NotStarted, DevelopmentStatus.InProgress, Day1)
        ];
        List<DependencyHistoryEntry> structure =
        [
            Lifecycle(Customer, EntityLifecycleState.Active, Day1), Lifecycle(Invoice, EntityLifecycleState.Active, Day1),
            Lifecycle(placeholder, EntityLifecycleState.Placeholder, Day1), Added(Invoice, placeholder, Day1),
            Removed(Invoice, placeholder, Day1.AddDays(1)),
            Entry(placeholder, DependencyHistoryChange.Removed, Day1.AddDays(1))
        ];

        IReadOnlyList<ReadinessCount> days = ReadinessHistory.Of(statuses, structure)
            .Daily(Day1, Day1.AddDays(1), TimeZoneInfo.Utc);

        Assert.Equal([(0, 1), (1, 0)], days.Select(day => (day.ReadyCount, day.WaitingCount)));
    }

    [Fact]
    public void DaysBeforeTheBaselineAreLeftOutAndTrackersAddUp()
    {
        DateTimeOffset baselineAt = At(Day1.AddDays(2));
        ReadinessHistory recordedSinceTheUpdate = ReadinessHistory.Of(
            [Created(Customer, Day1), Changed(Customer, DevelopmentStatus.NotStarted, DevelopmentStatus.InProgress, Day1)],
            [new DependencyHistoryEntry(Guid.NewGuid(), Invoice, DependencyHistoryChange.Lifecycle, baselineAt,
                lifecycleState: EntityLifecycleState.Active, isBaseline: true)]);
        ReadinessHistory recordedFromItsStart = ReadinessHistory.Of(
            [Created(Order, Day1), Changed(Customer, DevelopmentStatus.NotStarted, DevelopmentStatus.InProgress, Day1)],
            [Lifecycle(Order, EntityLifecycleState.Active, Day1)]);
        ReadinessHistory unrecorded = ReadinessHistory.Of([Created(Customer, Day1)], []);

        Assert.Equal(baselineAt, recordedSinceTheUpdate.KnownFromUtc);
        Assert.Equal(DateTimeOffset.MinValue, recordedFromItsStart.KnownFromUtc);
        Assert.Null(unrecorded.KnownFromUtc);
        Assert.Empty(unrecorded.Daily(Day1, Day1.AddDays(3), TimeZoneInfo.Utc));

        IReadOnlyList<ReadinessCount> combined = ReadinessHistory
            .Combine([recordedSinceTheUpdate, recordedFromItsStart, unrecorded])
            .Daily(Day1, Day1.AddDays(3), TimeZoneInfo.Utc);
        Assert.Equal([Day1.AddDays(2), Day1.AddDays(3)], combined.Select(day => day.Date));
        Assert.All(combined, day => Assert.Equal(1, day.ReadyCount));
    }

    private static DateTimeOffset At(DateOnly date) => new(date.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);

    private static EntityStatusHistoryEntry Created(EntityId entity, DateOnly date) =>
        new(entity, null, DevelopmentStatus.NotStarted, At(date), StatusHistoryEntryKind.Created);

    private static EntityStatusHistoryEntry Changed(EntityId entity, DevelopmentStatus from, DevelopmentStatus to, DateOnly date) =>
        new(entity, from, to, At(date).AddHours(1), StatusHistoryEntryKind.Transition);

    private static DependencyHistoryEntry Lifecycle(EntityId entity, EntityLifecycleState state, DateOnly date) =>
        new(Guid.NewGuid(), entity, DependencyHistoryChange.Lifecycle, At(date), lifecycleState: state);

    private static DependencyHistoryEntry Added(EntityId entity, EntityId dependency, DateOnly date) =>
        new(Guid.NewGuid(), entity, DependencyHistoryChange.DependencyAdded, At(date), dependency);

    private static DependencyHistoryEntry Removed(EntityId entity, EntityId dependency, DateOnly date) =>
        new(Guid.NewGuid(), entity, DependencyHistoryChange.DependencyRemoved, At(date), dependency);

    private static DependencyHistoryEntry Entry(EntityId entity, DependencyHistoryChange change, DateOnly date) =>
        new(Guid.NewGuid(), entity, change, At(date));
}

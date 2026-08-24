using EntityTracker.Domain;

namespace EntityTracker.DemoData;

internal sealed record ScheduledStatusChange(
    DateTimeOffset OccurredAtUtc,
    EntityId EntityId,
    DevelopmentStatus NewStatus);

internal sealed class SyntheticProgressTimeline
{
    private static readonly DevelopmentStatus[] GuaranteedStatuses =
        Enum.GetValues<DevelopmentStatus>();

    private SyntheticProgressTimeline(
        DateTimeOffset baselineAtUtc,
        IReadOnlyList<ScheduledStatusChange> changes,
        IReadOnlyDictionary<EntityId, DevelopmentStatus> finalStatuses)
    {
        BaselineAtUtc = baselineAtUtc;
        Changes = changes;
        FinalStatuses = finalStatuses;
    }

    public DateTimeOffset BaselineAtUtc { get; }

    public IReadOnlyList<ScheduledStatusChange> Changes { get; }

    public IReadOnlyDictionary<EntityId, DevelopmentStatus> FinalStatuses { get; }

    public static SyntheticProgressTimeline Create(
        IEnumerable<TrackedEntity> activeEntities,
        DateOnly startDate,
        DateOnly endDate,
        TimeZoneInfo timeZone,
        int seed)
    {
        ArgumentNullException.ThrowIfNull(activeEntities);
        ArgumentNullException.ThrowIfNull(timeZone);

        int dayCount = endDate.DayNumber - startDate.DayNumber + 1;
        if (dayCount < 7)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startDate),
                "Synthetic progress history must span at least seven days.");
        }

        TrackedEntity[] entities = activeEntities
            .OrderBy(static entity => entity.SourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entity => entity.SourceName, StringComparer.Ordinal)
            .ThenBy(static entity => entity.Id.Value)
            .ToArray();
        if (entities.Length == 0)
        {
            throw new ArgumentException(
                "Synthetic progress history requires at least one active entity.",
                nameof(activeEntities));
        }

        if (entities.Any(static entity => entity.LifecycleState != EntityLifecycleState.Active))
        {
            throw new ArgumentException(
                "Only active entities can receive synthetic progress transitions.",
                nameof(activeEntities));
        }

        Random random = new(seed);
        Shuffle(entities, random);

        List<ScheduledStatusChange> changes = [];
        Dictionary<EntityId, DevelopmentStatus> finalStatuses = [];
        for (int entityIndex = 0; entityIndex < entities.Length; entityIndex++)
        {
            TrackedEntity entity = entities[entityIndex];
            DevelopmentStatus target = SelectTargetStatus(entityIndex, random);
            DevelopmentStatus[] path = CreatePath(target);
            int finalDayOffset = CalculateFinalDayOffset(
                entityIndex,
                entities.Length,
                dayCount,
                path.Length,
                random);

            int previousOffset = 0;
            for (int pathIndex = 0; pathIndex < path.Length; pathIndex++)
            {
                int remainingSteps = path.Length - pathIndex - 1;
                int proposedOffset = (int)Math.Round(
                    finalDayOffset * ((pathIndex + 1d) / path.Length),
                    MidpointRounding.AwayFromZero);
                int dayOffset = Math.Clamp(
                    proposedOffset,
                    previousOffset + 1,
                    finalDayOffset - remainingSteps);
                previousOffset = dayOffset;

                changes.Add(new ScheduledStatusChange(
                    AtLocalNoonUtc(startDate.AddDays(dayOffset), timeZone),
                    entity.Id,
                    path[pathIndex]));
            }

            finalStatuses.Add(entity.Id, target);
        }

        return new SyntheticProgressTimeline(
            AtLocalNoonUtc(startDate, timeZone),
            changes
                .OrderBy(static change => change.OccurredAtUtc)
                .ThenBy(static change => change.EntityId.Value)
                .ToArray(),
            finalStatuses);
    }

    private static DevelopmentStatus SelectTargetStatus(int entityIndex, Random random)
    {
        if (entityIndex < GuaranteedStatuses.Length)
        {
            return GuaranteedStatuses[entityIndex];
        }

        return random.Next(100) switch
        {
            < 20 => DevelopmentStatus.NotStarted,
            < 45 => DevelopmentStatus.InProgress,
            < 55 => DevelopmentStatus.ReworkNeeded,
            < 80 => DevelopmentStatus.DevelopmentCompleted,
            _ => DevelopmentStatus.Reconciled
        };
    }

    private static DevelopmentStatus[] CreatePath(DevelopmentStatus target) => target switch
    {
        DevelopmentStatus.NotStarted =>
            [DevelopmentStatus.InProgress, DevelopmentStatus.NotStarted],
        DevelopmentStatus.InProgress => [DevelopmentStatus.InProgress],
        DevelopmentStatus.ReworkNeeded =>
        [
            DevelopmentStatus.InProgress,
            DevelopmentStatus.DevelopmentCompleted,
            DevelopmentStatus.ReworkNeeded
        ],
        DevelopmentStatus.DevelopmentCompleted =>
            [DevelopmentStatus.InProgress, DevelopmentStatus.DevelopmentCompleted],
        DevelopmentStatus.Reconciled =>
        [
            DevelopmentStatus.InProgress,
            DevelopmentStatus.DevelopmentCompleted,
            DevelopmentStatus.Reconciled
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null)
    };

    private static int CalculateFinalDayOffset(
        int entityIndex,
        int entityCount,
        int dayCount,
        int transitionCount,
        Random random)
    {
        int lastOffset = dayCount - 1;
        int distributedOffset = entityCount == 1
            ? lastOffset
            : 1 + (int)Math.Round(
                entityIndex * ((lastOffset - 1d) / (entityCount - 1)),
                MidpointRounding.AwayFromZero);
        if (entityIndex > 0 && entityIndex < entityCount - 1)
        {
            distributedOffset += random.Next(-2, 3);
        }

        return Math.Clamp(distributedOffset, transitionCount, lastOffset);
    }

    private static DateTimeOffset AtLocalNoonUtc(DateOnly date, TimeZoneInfo timeZone)
    {
        DateTime localNoon = DateTime.SpecifyKind(
            date.ToDateTime(new TimeOnly(12, 0)),
            DateTimeKind.Unspecified);
        return new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(localNoon, timeZone),
            TimeSpan.Zero);
    }

    private static void Shuffle<T>(T[] items, Random random)
    {
        for (int index = items.Length - 1; index > 0; index--)
        {
            int selectedIndex = random.Next(index + 1);
            (items[index], items[selectedIndex]) = (items[selectedIndex], items[index]);
        }
    }
}

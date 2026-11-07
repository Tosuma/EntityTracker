using EntityTracker.Application.Dependencies;
using EntityTracker.Domain;

namespace EntityTracker.Application.History;

/// <summary>A day's not started entities: ready to start, or waiting on dependencies.</summary>
public sealed record ReadinessCount(DateOnly Date, int ReadyCount, int WaitingCount);

/// <summary>
/// Works out, for any past day, how many entities were ready to start and how many waited on
/// dependencies, from the status history and the dependency history: the statuses, dependencies and
/// lifecycle states as they were that day, with the same rules the app uses for today (#179).
/// <para>
/// Setting up a Tracker isn't progress. Until its first status change, everything recorded counts as
/// its starting state, so dependencies entered while setting it up don't show as movement.
/// </para>
/// <para>
/// The history can't be filled in for the time before it was recorded: a Tracker whose history starts
/// with a baseline is only known from that baseline on (<see cref="KnownFromUtc"/>).
/// </para>
/// </summary>
public sealed class ReadinessHistory
{
    private static readonly ProgressSnapshotCalculator Calculator = new();
    private static readonly EffectiveDependencyResolver Resolver = new();

    private readonly IReadOnlyList<Part> _parts;

    private ReadinessHistory(IReadOnlyList<Part> parts)
    {
        _parts = parts;
        // A Tracker without any history (no entities yet) has nothing to count, so it doesn't hold the
        // others back.
        DateTimeOffset[] known = parts.Select(static part => part.KnownFromUtc).OfType<DateTimeOffset>().ToArray();
        KnownFromUtc = known.Length == 0 ? null : known.Max();
    }

    /// <summary>
    /// Gets when the counts are known from: the latest baseline of the Trackers it covers, or
    /// <see cref="DateTimeOffset.MinValue"/> when every Tracker was recorded from its start. Null when
    /// there is no dependency history at all, so nothing can be said.
    /// </summary>
    public DateTimeOffset? KnownFromUtc { get; }

    /// <summary>One Tracker's history.</summary>
    public static ReadinessHistory Of(
        IEnumerable<EntityStatusHistoryEntry> statusHistory,
        IEnumerable<DependencyHistoryEntry> dependencyHistory)
    {
        ArgumentNullException.ThrowIfNull(statusHistory);
        ArgumentNullException.ThrowIfNull(dependencyHistory);
        return new ReadinessHistory([new Part(statusHistory.ToArray(), dependencyHistory.ToArray())]);
    }

    /// <summary>Several Trackers together: each day's counts are their sums.</summary>
    public static ReadinessHistory Combine(IEnumerable<ReadinessHistory> histories)
    {
        ArgumentNullException.ThrowIfNull(histories);
        return new ReadinessHistory(histories.SelectMany(static history => history._parts).ToArray());
    }

    /// <summary>
    /// Gets each day's counts from <paramref name="from"/> to <paramref name="to"/>, as they were at the
    /// end of that day in <paramref name="timeZone"/>. Days before <see cref="KnownFromUtc"/> are left out.
    /// </summary>
    public IReadOnlyList<ReadinessCount> Daily(DateOnly from, DateOnly to, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        if (KnownFromUtc is not { } knownFrom) return [];
        if (knownFrom > DateTimeOffset.MinValue)
        {
            DateOnly firstKnown = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(knownFrom, timeZone).DateTime);
            if (firstKnown > from) from = firstKnown;
        }

        List<ReadinessCount> days = [];
        for (DateOnly date = from; date <= to; date = date.AddDays(1))
        {
            DateTimeOffset until = new(TimeZoneInfo.ConvertTimeToUtc(
                date.AddDays(1).ToDateTime(TimeOnly.MinValue), timeZone), TimeSpan.Zero);
            int ready = 0;
            int waiting = 0;
            foreach (Part part in _parts)
            {
                ProgressSnapshotState state = part.At(until);
                ready += state.ReadyCount;
                waiting += state.WaitingOnDependenciesCount;
            }

            days.Add(new ReadinessCount(date, ready, waiting));
        }

        return days;
    }

    /// <summary>One Tracker: its events in order, and when it left setup.</summary>
    private sealed class Part
    {
        // The entities are only counted, so any Tracker ID will do.
        private static readonly TrackerId Owner = new(Guid.Parse("00000000-0000-0000-0000-000000000001"));

        private readonly EntityStatusHistoryEntry[] _statuses;
        private readonly DependencyHistoryEntry[] _structure;
        private readonly DateTimeOffset? _firstStatusChange;

        public Part(EntityStatusHistoryEntry[] statuses, DependencyHistoryEntry[] structure)
        {
            // A stable sort: entries at the same moment keep the order they were recorded in.
            _statuses = statuses.OrderBy(static entry => entry.OccurredAtUtc).ToArray();
            _structure = structure.OrderBy(static entry => entry.OccurredAtUtc).ToArray();
            _firstStatusChange = _statuses
                .Where(static entry => entry.Kind == StatusHistoryEntryKind.Transition)
                .Select(static entry => (DateTimeOffset?)entry.OccurredAtUtc)
                .FirstOrDefault();
            DependencyHistoryEntry[] baselines = _structure.Where(static entry => entry.IsBaseline).ToArray();
            KnownFromUtc = _structure.Length == 0 ? null
                : baselines.Length > 0 ? baselines.Min(static entry => entry.OccurredAtUtc)
                : DateTimeOffset.MinValue;
        }

        public DateTimeOffset? KnownFromUtc { get; }

        /// <summary>The counts from everything recorded before <paramref name="until"/>.</summary>
        public ProgressSnapshotState At(DateTimeOffset until)
        {
            // Before the first status change the Tracker is being set up: its starting state is everything
            // recorded until then. With no status change at all, it is still being set up.
            if (_firstStatusChange is not { } started) until = DateTimeOffset.MaxValue;
            else if (until < started) until = started;

            Dictionary<EntityId, DevelopmentStatus> statuses = [];
            foreach (EntityStatusHistoryEntry entry in _statuses)
            {
                if (entry.OccurredAtUtc >= until) break;
                statuses[entry.EntityId] = entry.NewStatus;
            }

            Dictionary<EntityId, EntityLifecycleState> lifecycle = [];
            HashSet<EntityDependency> dependencies = [];
            foreach (DependencyHistoryEntry entry in _structure)
            {
                if (entry.OccurredAtUtc >= until) break;
                switch (entry.Change)
                {
                    case DependencyHistoryChange.DependencyAdded:
                        dependencies.Add(new EntityDependency(entry.EntityId, entry.DependencyEntityId!));
                        break;
                    case DependencyHistoryChange.DependencyRemoved:
                        dependencies.Remove(new EntityDependency(entry.EntityId, entry.DependencyEntityId!));
                        break;
                    case DependencyHistoryChange.Lifecycle:
                        lifecycle[entry.EntityId] = entry.LifecycleState!.Value;
                        break;
                    case DependencyHistoryChange.Removed:
                        lifecycle.Remove(entry.EntityId);
                        break;
                }
            }

            // Only the readiness rules look at these entities, so a name isn't needed: the ID stands in.
            TrackedEntity[] entities = lifecycle
                .Where(item => item.Value != EntityLifecycleState.Active || statuses.ContainsKey(item.Key))
                .Select(item => new TrackedEntity(item.Key, Owner, item.Key.Value.ToString("N"),
                    statuses.GetValueOrDefault(item.Key), lifecycleState: item.Value))
                .ToArray();
            return Calculator.Calculate(entities, Resolver.Resolve(entities, dependencies));
        }
    }
}

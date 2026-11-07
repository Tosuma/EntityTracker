using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Snapshots;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Persistence;
using EntityTracker.Infrastructure.Snapshots;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Tests.Persistence;

/// <summary>#179: the dependency history is recorded with every change, and travels with the Project.</summary>
public sealed class DependencyHistoryTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Changes_AreRecordedWhenTheyHappenAndNothingIsRecordedWithoutAChange()
    {
        await using TemporarySqliteFile file = new();
        MutableTimeProvider time = new(Start);
        SqliteDatabase database = new(file.DatabasePath, time);
        await database.InitializeAsync();
        SqliteTrackedStateStore store = new(database);
        TrackerId tracker = database.GetTrackerId();
        TrackedEntity customer = new(EntityId.New(), tracker, "Customer");
        TrackedEntity invoice = new(EntityId.New(), tracker, "Invoice");
        EntityDependency dependency = new(invoice.Id, customer.Id);

        // A new entity with a dependency, then a notes-only change, then the dependency removed and the
        // customer archived.
        await store.ApplyAsync(new TrackedStateChangeSet([customer, invoice], [], [], [invoice.Id], [dependency]));
        time.Advance(TimeSpan.FromHours(1));
        await store.ApplyAsync(new TrackedStateChangeSet([], [], [],
            entitiesWithProgressToUpdate: [new TrackedEntity(invoice.Id, tracker, "Invoice", notes: "Notes only")]));
        time.Advance(TimeSpan.FromHours(1));
        await store.ApplyAsync(new TrackedStateChangeSet([], [], [customer.Id], [invoice.Id], []));

        IReadOnlyList<DependencyHistoryEntry> history =
            await new SqliteProgressHistoryRepository(database).GetDependencyHistoryAsync(tracker);
        Assert.All(history, entry => Assert.False(entry.IsBaseline));
        Assert.Equal(
            [
                (Start, DependencyHistoryChange.Lifecycle, customer.Id, (EntityId?)null, (EntityLifecycleState?)EntityLifecycleState.Active),
                (Start, DependencyHistoryChange.Lifecycle, invoice.Id, null, EntityLifecycleState.Active),
                (Start, DependencyHistoryChange.DependencyAdded, invoice.Id, customer.Id, null),
                (Start.AddHours(2), DependencyHistoryChange.Lifecycle, customer.Id, null, EntityLifecycleState.Archived),
                (Start.AddHours(2), DependencyHistoryChange.DependencyRemoved, invoice.Id, customer.Id, null)
            ],
            history
                .OrderBy(entry => entry.OccurredAtUtc)
                .ThenBy(entry => entry.Change == DependencyHistoryChange.Lifecycle ? 0 : 1)
                .ThenBy(entry => entry.EntityId == customer.Id ? 0 : 1)
                .Select(entry => (entry.OccurredAtUtc, entry.Change, entry.EntityId, entry.DependencyEntityId, entry.LifecycleState)));
    }

    [Fact]
    public async Task Migration_GivesEveryTrackerABaselineOfItsDependencies()
    {
        await using TemporarySqliteFile file = new();
        MutableTimeProvider time = new(Start);
        SqliteDatabase database = new(file.DatabasePath, time);
        await database.InitializeAsync();
        TrackerId tracker = database.GetTrackerId();
        TrackedEntity customer = new(EntityId.New(), tracker, "Customer");
        TrackedEntity invoice = new(EntityId.New(), tracker, "Invoice");
        await new SqliteTrackedStateStore(database).ApplyAsync(
            new TrackedStateChangeSet([customer, invoice], [], [], [invoice.Id], [new EntityDependency(invoice.Id, customer.Id)]));
        // As before version 29: no dependency history.
        await using (SqliteConnection connection = await database.OpenConnectionAsync(CancellationToken.None))
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "DROP TABLE entity_dependency_history; PRAGMA user_version = 28;";
            await command.ExecuteNonQueryAsync();
        }

        time.Advance(TimeSpan.FromDays(10));
        SqliteDatabase upgraded = new(file.DatabasePath, time);
        await upgraded.InitializeAsync();

        IReadOnlyList<DependencyHistoryEntry> history =
            await new SqliteProgressHistoryRepository(upgraded).GetDependencyHistoryAsync(tracker);
        Assert.Equal(3, history.Count);
        Assert.All(history, entry =>
        {
            Assert.True(entry.IsBaseline);
            Assert.Equal(Start.AddDays(10), entry.OccurredAtUtc);
        });
        DependencyHistoryEntry added = Assert.Single(history, entry => entry.Change == DependencyHistoryChange.DependencyAdded);
        Assert.Equal((invoice.Id, customer.Id), (added.EntityId, added.DependencyEntityId));
        // So the readiness chart starts at the update.
        Assert.Equal(Start.AddDays(10), ReadinessHistory.Of([], history).KnownFromUtc);
    }

    [Fact]
    public async Task History_TravelsWithTheProjectSnapshot()
    {
        await using TemporarySqliteFile file = new();
        MutableTimeProvider time = new(Start);
        SqliteDatabase database = new(file.DatabasePath, time);
        await database.InitializeAsync();
        TrackerId tracker = database.GetTrackerId();
        TrackedEntity customer = new(EntityId.New(), tracker, "Customer");
        TrackedEntity invoice = new(EntityId.New(), tracker, "Invoice");
        await new SqliteTrackedStateStore(database).ApplyAsync(
            new TrackedStateChangeSet([customer, invoice], [], [], [invoice.Id], [new EntityDependency(invoice.Id, customer.Id)]));
        SqliteProjectSnapshotStore store = new(database);
        ProjectId project = await ProjectOfAsync(database, tracker);

        ProjectSnapshot read = Assert.IsType<ProjectSnapshot>((await store.ReadAsync(project)).Snapshot);
        SnapshotTracker withHistory = read.Trackers.Single(item => item.Id == tracker.Value);
        Assert.Equal(3, withHistory.DependencyHistory!.Count);

        // Through Git: encoded one file per entry, and decoded the same.
        ProjectSnapshotJsonCodec codec = new();
        ProjectSnapshotPackage package = codec.Encode(read);
        Assert.Equal(3, package.Files.Keys.Count(path => path.Contains("/dependency-history/", StringComparison.Ordinal)));
        ProjectSnapshot decoded = codec.Decode(package.Files);
        Assert.Equal(withHistory.DependencyHistory,
            decoded.Trackers.Single(item => item.Id == tracker.Value).DependencyHistory);

        // Applied to another database, and read back unchanged.
        await using TemporarySqliteFile other = new();
        SqliteDatabase otherDatabase = new(other.DatabasePath, time) { StartNewDatabaseWithoutProjects = true };
        await otherDatabase.InitializeAsync();
        SqliteProjectSnapshotStore otherStore = new(otherDatabase);
        await otherStore.ApplyAsync(decoded, 0);
        ProjectSnapshot again = Assert.IsType<ProjectSnapshot>((await otherStore.ReadAsync(project)).Snapshot);
        Assert.Equal(withHistory.DependencyHistory, again.Trackers.Single(item => item.Id == tracker.Value).DependencyHistory);

        // An older format can't carry it.
        Assert.ThrowsAny<InvalidDataException>(() => codec.Encode(read with { FormatVersion = 12 }));
    }

    private static async Task<ProjectId> ProjectOfAsync(SqliteDatabase database, TrackerId tracker)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(CancellationToken.None);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT project_id FROM trackers WHERE id = $id;";
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(tracker));
        return new ProjectId(Guid.Parse((string)(await command.ExecuteScalarAsync())!));
    }
}

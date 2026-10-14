using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Snapshots;
using EntityTracker.Application.Tracking;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Persistence;
using EntityTracker.Infrastructure.Snapshots;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Tests.Persistence;

/// <summary>
/// The schema is in Boyce-Codd normal form: no column is derived from another column of its row,
/// names are compared with the app's own rule, the status is the status history, and every value is
/// atomic.
/// </summary>
public sealed class NormalFormTests
{
    [Fact]
    public async Task Schema_HasNoDerivedColumnsAndIdentifiesHistoryByItsIds()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        await using SqliteConnection connection = await database.OpenConnectionAsync(CancellationToken.None);

        Dictionary<string, List<(string Name, int Key)>> tables = [];
        foreach (string table in await StringsAsync(connection,
                     "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';"))
        {
            List<(string, int)> columns = [];
            using SqliteCommand info = connection.CreateCommand();
            info.CommandText = $"SELECT name, pk FROM pragma_table_info('{table}');";
            await using SqliteDataReader reader = await info.ExecuteReaderAsync();
            while (await reader.ReadAsync()) columns.Add((reader.GetString(0), reader.GetInt32(1)));
            tables[table] = columns;
        }

        Assert.DoesNotContain(tables.SelectMany(table => table.Value), column => column.Name.EndsWith("_key", StringComparison.Ordinal));
        Assert.DoesNotContain(tables.SelectMany(table => table.Value), column =>
            column.Name is "development_status" or "previous_status" or "baseline_json");
        Assert.DoesNotContain(tables["projects"], column => column.Name == "lifecycle_state");
        Assert.DoesNotContain(tables["trackers"], column => column.Name == "lifecycle_state");
        Assert.Equal(["event_id"], tables["entity_status_history"].Where(c => c.Key > 0).Select(c => c.Name));
        Assert.Equal(["snapshot_id"], tables["progress_snapshots"].Where(c => c.Key > 0).Select(c => c.Name));
        Assert.Contains("entity_current_status", await StringsAsync(connection,
            "SELECT name FROM sqlite_master WHERE type = 'view';"));
    }

    [Theory]
    [InlineData("customer", "CUSTOMER")]
    [InlineData("Øre", "øRE")]
    [InlineData("Straße", "STRASSE ")]
    public async Task EntityNames_AreUniqueInATrackerIgnoringCaseLikeTheApp(string first, string second)
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        TrackerId tracker = database.GetTrackerId();
        SqliteTrackedStateStore store = new(database);
        await store.ApplyAsync(tracker, new TrackedStateChangeSet([new TrackedEntity(EntityId.New(), tracker, first)], [], []));

        TrackedStateChangeSet duplicate = new([new TrackedEntity(EntityId.New(), tracker, second)], [], []);
        bool sameForTheApp = first.Trim().ToUpperInvariant() == second.Trim().ToUpperInvariant();
        if (sameForTheApp)
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAsync(tracker, duplicate));
        else
            await store.ApplyAsync(tracker, duplicate);

        Assert.Equal(sameForTheApp ? 1 : 2, (await new SqliteEntityRepository(database).GetAllAsync(tracker)).Count);
    }

    [Fact]
    public async Task CatalogNames_AreReservedIgnoringCase()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        Tracker tracker = Assert.Single(await new SqliteTrackerRepository(database).GetAllAsync());

        Assert.True(await new SqliteTrackerRepository(database).IsNameReservedAsync(
            tracker.ProjectId, " DEFAULT tracker "));
        Assert.True(await new SqliteProjectRepository(database).IsNameReservedAsync("default PROJECT"));
        Assert.False(await new SqliteProjectRepository(database).IsNameReservedAsync("Another project"));
    }

    [Fact]
    public async Task Status_IsTheNewestStatusEvent()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        TrackerId tracker = database.GetTrackerId();
        SqliteTrackedStateStore store = new(database);
        SqliteEntityRepository entities = new(database);
        SqliteProgressHistoryRepository history = new(database);
        TrackedEntity entity = new(EntityId.New(), tracker, "Orders");
        TrackedEntity placeholder = TrackedEntity.Placeholder(tracker, "Future");
        await store.ApplyAsync(tracker, new TrackedStateChangeSet([entity, placeholder], [], [],
            [entity.Id], [new EntityDependency(entity.Id, placeholder.Id)]));

        entity.ChangeStatus(DevelopmentStatus.InProgress);
        await store.ApplyAsync(tracker, new TrackedStateChangeSet([], [], [], entitiesWithProgressToUpdate: [entity]));
        entity.ChangeNotes("Only the notes change");
        await store.ApplyAsync(tracker, new TrackedStateChangeSet([], [], [], entitiesWithProgressToUpdate: [entity]));

        Assert.Equal(DevelopmentStatus.InProgress, (await entities.GetAsync(tracker, entity.Id))!.Status);
        Assert.Equal(DevelopmentStatus.NotStarted, (await entities.GetAsync(tracker, placeholder.Id))!.Status);
        EntityStatusHistoryEntry[] events = (await history.GetStatusHistoryAsync(tracker)).ToArray();
        Assert.Equal(
            [(StatusHistoryEntryKind.Created, (DevelopmentStatus?)null, DevelopmentStatus.NotStarted),
             (StatusHistoryEntryKind.Transition, DevelopmentStatus.NotStarted, DevelopmentStatus.InProgress)],
            events.Select(item => (item.Kind, item.PreviousStatus, item.NewStatus)));
        Assert.Equal(events[0].EventId, events[1].PreviousEventId);
    }

    [Fact]
    public async Task SyncBaseline_RoundTripsThroughItsRows()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        string tracker = SqlitePersistenceValues.Format(database.GetTrackerId());
        TrackerSyncBaseline baseline = new(
            new TrackerSyncStructure(
            [
                new TrackerSyncEntity("customer", true, 2, "Core",
                    [new TrackerSyncDependency("address"), new TrackerSyncDependency("Future")], "Active only"),
                new TrackerSyncEntity("legacy", false, null, "", [], null)
            ]),
            new TrackerSyncStructure([new TrackerSyncEntity("customer", true, null, "", [], "")]));
        await using SqliteConnection connection = await database.OpenConnectionAsync(CancellationToken.None);
        await using SqliteTransaction transaction = connection.BeginTransaction();

        await SqliteSyncBaselineRows.WriteAsync(connection, transaction, tracker, baseline, CancellationToken.None);
        TrackerSyncBaseline? read = await SqliteSyncBaselineRows.ReadAsync(connection, transaction, tracker, CancellationToken.None);

        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(baseline), System.Text.Json.JsonSerializer.Serialize(read));
        TrackerSyncBaseline empty = new(new TrackerSyncStructure([]), new TrackerSyncStructure([]));
        await SqliteSyncBaselineRows.WriteAsync(connection, transaction, tracker, empty, CancellationToken.None);
        Assert.NotNull(await SqliteSyncBaselineRows.ReadAsync(connection, transaction, tracker, CancellationToken.None));
    }

    [Fact]
    public async Task MigrationFromVersionTwenty_KeepsTheProjectSnapshotByteForByte()
    {
        await using TemporarySqliteFile file = new();
        MutableTimeProvider time = new(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));
        SqliteDatabase database = new(file.DatabasePath, time);
        await database.InitializeAsync();
        TrackerId tracker = database.GetTrackerId();
        Tracker defaultTracker = Assert.Single(await new SqliteTrackerRepository(database).GetAllAsync());
        SqliteTrackedStateStore store = new(database);
        TrackedEntity customer = new(EntityId.New(), tracker, "customer", requestedPriority: 2, groupName: "Core");
        TrackedEntity order = new(EntityId.New(), tracker, "Order", notes: "Internal", sharedNotes: "Shared");
        TrackedEntity archived = new(EntityId.New(), tracker, "Legacy", lifecycleState: EntityLifecycleState.Archived);
        await store.ApplyAsync(tracker, new TrackedStateChangeSet([customer, order, archived], [], [],
            [order.Id], [new EntityDependency(order.Id, customer.Id)],
            progressSnapshotAfterChanges: new ProgressSnapshotState(1, 1, 0, 0, 0, 0)));
        foreach (DevelopmentStatus status in new[] { DevelopmentStatus.InProgress, DevelopmentStatus.Reconciled })
        {
            time.Advance(TimeSpan.FromHours(1));
            customer.ChangeStatus(status);
            await store.ApplyAsync(tracker, new TrackedStateChangeSet([], [], [], entitiesWithProgressToUpdate: [customer],
                progressSnapshotAfterChanges: new ProgressSnapshotState(1, 0, 1, 0, 0, status == DevelopmentStatus.Reconciled ? 1 : 0)));
        }
        await new SqliteProjectDeveloperStore(database).CreateAsync(
            new ProjectDeveloper(DeveloperId.New(), defaultTracker.ProjectId, "AB", "Ada B", false));
        await using (SqliteConnection connection = await database.OpenConnectionAsync(CancellationToken.None))
        await using (SqliteTransaction transaction = connection.BeginTransaction())
        {
            await SqliteSyncBaselineRows.WriteAsync(connection, transaction, SqlitePersistenceValues.Format(tracker),
                new TrackerSyncBaseline(
                    new TrackerSyncStructure([new TrackerSyncEntity("customer", true, 2, "Core", [], "")]),
                    new TrackerSyncStructure([new TrackerSyncEntity("Order", true, null, "",
                        [new TrackerSyncDependency("customer")], "")])),
                CancellationToken.None);
            await transaction.CommitAsync();
        }
        SqliteProjectSnapshotStore snapshots = new(database);
        ProjectSnapshotJsonCodec codec = new();
        ProjectId project = defaultTracker.ProjectId;
        string before = codec.Encode(Assert.IsType<ProjectSnapshot>((await snapshots.ReadAsync(project)).Snapshot)).Sha256;

        await VersionTwentySchema.RevertAsync(file.DatabasePath);
        await new SqliteDatabase(file.DatabasePath, time).InitializeAsync();

        ProjectSnapshot after = Assert.IsType<ProjectSnapshot>((await snapshots.ReadAsync(project)).Snapshot);
        Assert.Equal(before, codec.Encode(after).Sha256);
        Assert.Equal("Reconciled", after.Trackers[0].Entities.Single(e => e.Id == customer.Id.Value).DevelopmentStatus);
    }

    private static async Task<List<string>> StringsAsync(SqliteConnection connection, string sql)
    {
        List<string> values = [];
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) values.Add(reader.GetString(0));
        return values;
    }
}

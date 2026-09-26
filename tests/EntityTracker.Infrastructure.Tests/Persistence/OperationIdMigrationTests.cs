using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Synchronization;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Persistence;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Tests.Persistence;

public sealed class OperationIdMigrationTests
{
    [Fact]
    public async Task VersionTwelveHistoryReceivesStableDistinctOperationIds()
    {
        await using TemporarySqliteFile file = new();
        await CreateVersionTwelveAsync(file.DatabasePath);
        SqliteDatabase database = new(file.DatabasePath);

        await database.InitializeAsync();
        string[] first = await ReadOperationIdsAsync(file.DatabasePath);
        await database.InitializeAsync();
        string[] second = await ReadOperationIdsAsync(file.DatabasePath);

        Assert.Equal(2, first.Length);
        Assert.Equal(2, first.Distinct().Count());
        Assert.All(first, value => Assert.Equal(value.ToLowerInvariant(), value));
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task OneChangeSetGroupsCreatedHistoryUnderOneOperationId()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        TrackerId trackerId = database.GetTrackerId();
        OperationId operationId = OperationId.New();
        TrackedEntity first = new(EntityId.New(), trackerId, "first");
        TrackedEntity second = new(EntityId.New(), trackerId, "second");
        TrackedStateChangeSet changes = new(
            [first, second], [], [], [], [], [],
            progressSnapshotAfterChanges: new ProgressSnapshotState(2, 0, 0, 0, 0, 0),
            operationId: operationId);

        await new SqliteTrackedStateStore(database).ApplyAsync(trackerId, changes);
        IReadOnlyList<EntityStatusHistoryEntry> history =
            await new SqliteProgressHistoryRepository(database).GetStatusHistoryAsync(trackerId);

        Assert.Equal(2, history.Count);
        Assert.All(history, entry => Assert.Equal(operationId, entry.OperationId));
    }

    [Fact]
    public async Task VersionThirteenProjectionHistoryReusesTheCreatingOperationId()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        TrackerId trackerId = database.GetTrackerId();
        OperationId operationId = OperationId.New();
        TrackedEntity entity = new(EntityId.New(), trackerId, "customer");
        TrackedStateChangeSet changes = new(
            [entity], [], [], [], [], [],
            progressSnapshotAfterChanges: new ProgressSnapshotState(1, 0, 0, 0, 0, 0),
            operationId: operationId);
        await new SqliteTrackedStateStore(database).ApplyAsync(
            trackerId,
            changes,
            new SchemaImportCompletion("schema.csv", SchemaImportMode.Complete, 1, 0, 0, 0, 0));

        await using (SqliteConnection connection = new($"Data Source={file.DatabasePath}"))
        {
            await connection.OpenAsync();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                DROP INDEX ix_progress_snapshots_tracker_time;
                DROP INDEX ix_progress_snapshots_operation;
                ALTER TABLE progress_snapshots DROP COLUMN operation_id;
                CREATE INDEX ix_progress_snapshots_tracker_time
                    ON progress_snapshots (tracker_id, recorded_at_utc, id);
                ALTER TABLE schema_import_summary DROP COLUMN operation_id;
                PRAGMA user_version = 13;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await database.InitializeAsync();

        await using SqliteConnection migrated = new($"Data Source={file.DatabasePath}");
        await migrated.OpenAsync();
        using SqliteCommand read = migrated.CreateCommand();
        read.CommandText = """
            SELECT history.operation_id, snapshot.operation_id, summary.operation_id
            FROM entity_status_history history
            INNER JOIN tracked_entities entity ON entity.id = history.entity_id
            INNER JOIN progress_snapshots snapshot
                ON snapshot.tracker_id = entity.tracker_id
               AND snapshot.recorded_at_utc = history.occurred_at_utc
            INNER JOIN schema_import_summary summary
                ON summary.tracker_id = entity.tracker_id
               AND summary.applied_at_utc = history.occurred_at_utc
            LIMIT 1;
            """;
        await using SqliteDataReader reader = await read.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(operationId.Value.ToString("D"), reader.GetString(0));
        Assert.Equal(reader.GetString(0), reader.GetString(1));
        Assert.Equal(reader.GetString(0), reader.GetString(2));
    }

    private static async Task CreateVersionTwelveAsync(string path)
    {
        await using SqliteConnection connection = new($"Data Source={path}");
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            CREATE TABLE projects (id TEXT PRIMARY KEY);
            CREATE TABLE trackers (id TEXT PRIMARY KEY, project_id TEXT NOT NULL);
            CREATE TABLE tracked_entities (id TEXT PRIMARY KEY, tracker_id TEXT NOT NULL);
            CREATE TABLE entity_status_history
            (
                id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                entity_id TEXT NOT NULL,
                previous_status TEXT NULL,
                new_status TEXT NOT NULL,
                entry_kind TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                FOREIGN KEY (entity_id) REFERENCES tracked_entities (id) ON DELETE RESTRICT
            );
            CREATE INDEX ix_entity_status_history_entity_time ON entity_status_history (entity_id, occurred_at_utc, id);
            CREATE INDEX ix_entity_status_history_time ON entity_status_history (occurred_at_utc, id);
            INSERT INTO projects VALUES ('11111111-1111-1111-1111-111111111111');
            INSERT INTO trackers VALUES ('22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111');
            INSERT INTO tracked_entities VALUES ('33333333-3333-3333-3333-333333333333', '22222222-2222-2222-2222-222222222222');
            INSERT INTO entity_status_history (entity_id, previous_status, new_status, entry_kind, occurred_at_utc) VALUES
                ('33333333-3333-3333-3333-333333333333', NULL, 'NotStarted', 'Created', '2026-01-01T00:00:00.0000000+00:00'),
                ('33333333-3333-3333-3333-333333333333', 'NotStarted', 'InProgress', 'Transition', '2026-01-02T00:00:00.0000000+00:00');
            PRAGMA user_version = 12;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string[]> ReadOperationIdsAsync(string path)
    {
        await using SqliteConnection connection = new($"Data Source={path}");
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT operation_id FROM entity_status_history ORDER BY id;";
        List<string> values = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) values.Add(reader.GetString(0));
        return values.ToArray();
    }
}

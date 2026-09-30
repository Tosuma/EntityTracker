using System.Text;
using EntityTracker.Application.Snapshots;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Persistence;
using EntityTracker.Infrastructure.Snapshots;
using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Tests.Persistence;

public sealed class ProjectSnapshotTests
{
    private static readonly DateTimeOffset T0 = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddDays(1);
    private static readonly DateTimeOffset T2 = T0.AddDays(2);

    [Fact]
    public async Task CompleteProject_RoundTripsAcrossCatalogsWithCanonicalBytes()
    {
        await using TemporarySqliteFile sourceFile = new();
        await using TemporarySqliteFile targetFile = new();
        SqliteDatabase sourceDb = new(sourceFile.DatabasePath);
        SqliteDatabase targetDb = new(targetFile.DatabasePath);
        await sourceDb.InitializeAsync();
        await targetDb.InitializeAsync();
        ProjectSnapshot seed = CompleteSnapshot();
        SqliteProjectSnapshotStore source = new(sourceDb);
        SqliteProjectSnapshotStore target = new(targetDb);
        await source.ApplyAsync(seed, 0);

        ProjectSnapshot actual = Assert.IsType<ProjectSnapshot>(
            (await source.ReadAsync(new ProjectId(seed.Project.Id))).Snapshot);
        ProjectSnapshotJsonCodec codec = new();
        ProjectSnapshotPackage first = codec.Encode(actual);
        ProjectSnapshot decoded = codec.Decode(first.Files);
        await target.ApplyAsync(decoded, 0);
        ProjectSnapshot imported = Assert.IsType<ProjectSnapshot>(
            (await target.ReadAsync(new ProjectId(seed.Project.Id))).Snapshot);
        ProjectSnapshotPackage second = codec.Encode(imported);

        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal(first.Files.Keys, second.Files.Keys);
        foreach (string path in first.Files.Keys)
            Assert.Equal(first.Files[path], second.Files[path]);
        Assert.Equal(seed.Trackers[0].CopiedFromTrackerId, imported.Trackers.Single(t => t.Id == seed.Trackers[0].Id).CopiedFromTrackerId);
        Assert.Equal(2, imported.Trackers.Count);
        Assert.Contains(imported.Trackers, t => t.LifecycleState == "Recycled");
        Assert.Contains(imported.Trackers.SelectMany(t => t.Entities), e => e.LifecycleState == "Archived");
        Assert.Contains(imported.Trackers.SelectMany(t => t.Entities), e => e.UnresolvedDependencies.Count > 0);
        Assert.Contains(imported.Trackers.SelectMany(t => t.Entities), e => e.Dependencies.Count > 0);
        Assert.Contains(imported.Trackers.SelectMany(t => t.Entities), e => e.ManualOverrides.Count > 0);

        ProjectSnapshot reordered = actual with
        {
            Trackers = actual.Trackers.Reverse().Select(t => t with
            {
                Entities = t.Entities.Reverse().ToArray(),
                StatusHistory = t.StatusHistory.Reverse().ToArray(),
                ProgressHistory = t.ProgressHistory.Reverse().ToArray()
            }).ToArray()
        };
        ProjectSnapshotPackage third = codec.Encode(reordered);
        Assert.Equal(first.Sha256, third.Sha256);
        foreach (string path in first.Files.Keys) Assert.Equal(first.Files[path], third.Files[path]);
    }

    [Fact]
    public async Task TrackerSyncBaseline_RoundTripsWithProjectSnapshot()
    {
        await using TemporarySqliteFile sourceFile = new();
        await using TemporarySqliteFile targetFile = new();
        SqliteDatabase sourceDb = new(sourceFile.DatabasePath);
        SqliteDatabase targetDb = new(targetFile.DatabasePath);
        await sourceDb.InitializeAsync();
        await targetDb.InitializeAsync();
        const string baseline = "{\"Source\":{\"Entities\":[]},\"Destination\":{\"Entities\":[]}}";
        ProjectSnapshot seed = CompleteSnapshot();
        seed = seed with { Trackers = seed.Trackers.Select((tracker, index) =>
            index == 0 ? tracker with { SyncBaselineJson = baseline } : tracker).ToArray() };
        SqliteProjectSnapshotStore source = new(sourceDb);
        SqliteProjectSnapshotStore target = new(targetDb);
        await source.ApplyAsync(seed, 0);
        ProjectSnapshot exported = Assert.IsType<ProjectSnapshot>(
            (await source.ReadAsync(new ProjectId(seed.Project.Id))).Snapshot);
        Assert.Equal(baseline, exported.Trackers[0].SyncBaselineJson);
        ProjectSnapshotJsonCodec codec = new();
        ProjectSnapshot decoded = codec.Decode(codec.Encode(exported).Files);
        await target.ApplyAsync(decoded, 0);
        ProjectSnapshot imported = Assert.IsType<ProjectSnapshot>(
            (await target.ReadAsync(new ProjectId(seed.Project.Id))).Snapshot);
        Assert.Equal(baseline, imported.Trackers[0].SyncBaselineJson);
    }

    [Fact]
    public async Task LargerSnapshotExportPreservesEntityRelationshipsAndCanonicalHash()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase db = new(file.DatabasePath);
        await db.InitializeAsync();
        ProjectSnapshot seed = CompleteSnapshot();
        SnapshotTracker tracker = seed.Trackers[0];
        SnapshotEntity template = tracker.Entities[0];
        SnapshotEntity[] additions = Enumerable.Range(0, 100).Select(index =>
        {
            Guid id = Guid.NewGuid();
            return template with
            {
                Id = id,
                SourceName = $"Extra entity {index:D3}",
                Dependencies = [new SnapshotDependency(id, tracker.Entities[1].Id, "Mandatory", T0, T1)],
                UnresolvedDependencies = [new SnapshotUnresolvedDependency(id,
                    $"External {index:D3}", "Optional", T0, T2)],
                ManualOverrides = [new SnapshotOverride(id,
                    $"External {index:D3}", "Suppress", T1, T2)]
            };
        }).ToArray();
        ProjectSnapshot expanded = seed with { Trackers =
            [tracker with { Entities = tracker.Entities.Concat(additions).ToArray() }, seed.Trackers[1]] };
        SqliteProjectSnapshotStore store = new(db);
        await store.ApplyAsync(expanded, 0);

        ProjectSnapshot actual = Assert.IsType<ProjectSnapshot>(
            (await store.ReadAsync(new ProjectId(seed.Project.Id))).Snapshot);
        SnapshotTracker exported = actual.Trackers.Single(item => item.Id == tracker.Id);
        Assert.Equal(102, exported.Entities.Count);
        Assert.All(exported.Entities, entity =>
        {
            if (entity.Id == tracker.Entities[1].Id) return;
            Assert.Single(entity.Dependencies);
            Assert.Single(entity.UnresolvedDependencies);
            Assert.Single(entity.ManualOverrides);
        });
        ProjectSnapshotJsonCodec codec = new();
        Assert.Equal(codec.Encode(expanded).Sha256, codec.Encode(actual).Sha256);
    }

    [Fact]
    public async Task InvalidSnapshotsAndRevisionMismatchLeaveDatabaseUnchanged()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase db = new(file.DatabasePath);
        await db.InitializeAsync();
        SqliteProjectSnapshotStore store = new(db);
        ProjectSnapshot snapshot = CompleteSnapshot();
        await store.ApplyAsync(snapshot, 0);
        ProjectSnapshotJsonCodec codec = new();
        string before = codec.Encode(Assert.IsType<ProjectSnapshot>(
            (await store.ReadAsync(new ProjectId(snapshot.Project.Id))).Snapshot)).Sha256;
        long revision = (await store.ReadAsync(new ProjectId(snapshot.Project.Id))).Revision;

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAsync(snapshot, revision - 1));
        Assert.Throws<InvalidDataException>(() => ProjectSnapshotValidator.Validate(snapshot with { FormatVersion = 2 }));
        Assert.Throws<InvalidDataException>(() => ProjectSnapshotValidator.Validate(snapshot with
        {
            Trackers = [snapshot.Trackers[0], snapshot.Trackers[0]]
        }));
        SnapshotTracker tracker = snapshot.Trackers[0];
        SnapshotEntity entity = tracker.Entities[0];
        Assert.Throws<InvalidDataException>(() => ProjectSnapshotValidator.Validate(snapshot with
        {
            Trackers = [tracker with
            {
                Entities = [entity with
                {
                    Dependencies = [new SnapshotDependency(entity.Id, Guid.NewGuid(), "Mandatory", T0, T1)]
                }, tracker.Entities[1]]
            }, snapshot.Trackers[1]]
        }));

        ProjectSnapshotPackage package = codec.Encode(snapshot);
        Dictionary<string, byte[]> malformed = package.Files.ToDictionary(p => p.Key, p => p.Value);
        malformed[".entitytracker/project.json"] = Encoding.UTF8.GetBytes("{bad json");
        Assert.Throws<InvalidDataException>(() => codec.Decode(malformed));
        malformed[".entitytracker/project.json"] = package.Files[".entitytracker/project.json"];
        malformed[".entitytracker/manifest.json"] = Encoding.UTF8.GetBytes(
            "{\"formatVersion\":2,\"projectId\":\"" + snapshot.Project.Id + "\"}");
        Assert.Throws<InvalidDataException>(() => codec.Decode(malformed));

        ProjectSnapshotRead after = await store.ReadAsync(new ProjectId(snapshot.Project.Id));
        Assert.Equal(revision, after.Revision);
        Assert.Equal(before, codec.Encode(Assert.IsType<ProjectSnapshot>(after.Snapshot)).Sha256);
    }

    [Fact]
    public async Task ApplyFailureRollsBackAndOrdinaryWritesAdvanceRevision()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase db = new(file.DatabasePath);
        await db.InitializeAsync();
        SqliteProjectSnapshotStore store = new(db);
        ProjectSnapshot snapshot = CompleteSnapshot();
        long revision = await store.ApplyAsync(snapshot, 0);
        ProjectSnapshotJsonCodec codec = new();
        string originalHash = codec.Encode(Assert.IsType<ProjectSnapshot>(
            (await store.ReadAsync(new ProjectId(snapshot.Project.Id))).Snapshot)).Sha256;

        await using (SqliteConnection connection = new($"Data Source={file.DatabasePath}"))
        {
            await connection.OpenAsync();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TRIGGER fail_project_progress BEFORE INSERT ON progress_snapshots
                BEGIN SELECT RAISE(ABORT, 'injected apply failure'); END;
                """;
            await command.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAnyAsync<Exception>(() => store.ApplyAsync(snapshot with
        {
            Project = snapshot.Project with { Name = "Changed" }
        }, revision));
        ProjectSnapshotRead afterFailure = await store.ReadAsync(new ProjectId(snapshot.Project.Id));
        Assert.Equal(revision, afterFailure.Revision);
        Assert.Equal(originalHash, codec.Encode(Assert.IsType<ProjectSnapshot>(afterFailure.Snapshot)).Sha256);

        SqliteProjectTrackerStore catalog = new(db);
        await catalog.RenameTrackerAsync(new TrackerId(snapshot.Trackers[0].Id), "Renamed");
        long renamed = (await store.ReadAsync(new ProjectId(snapshot.Project.Id))).Revision;
        Assert.True(renamed > revision);
        await catalog.SetTrackerLifecycleAsync(new TrackerId(snapshot.Trackers[0].Id), CatalogLifecycleState.Recycled);
        long recycled = (await store.ReadAsync(new ProjectId(snapshot.Project.Id))).Revision;
        Assert.True(recycled > renamed);
        await catalog.PurgeProjectAsync(new ProjectId(snapshot.Project.Id));
        ProjectSnapshotRead purged = await store.ReadAsync(new ProjectId(snapshot.Project.Id));
        Assert.Null(purged.Snapshot);
        Assert.True(purged.Revision > recycled);
    }

    [Fact]
    public async Task EverySynchronizedTableAdvancesRevisionAndWalAllowsConcurrentRead()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase db = new(file.DatabasePath);
        await db.InitializeAsync();
        ProjectSnapshot snapshot = CompleteSnapshot();
        SqliteProjectSnapshotStore store = new(db);
        long revision = await store.ApplyAsync(snapshot, 0);
        string projectId = snapshot.Project.Id.ToString("D");
        string trackerId = snapshot.Trackers[0].Id.ToString("D");
        string entityId = snapshot.Trackers[0].Entities[0].Id.ToString("D");
        await using SqliteConnection connection = new($"Data Source={file.DatabasePath}");
        await connection.OpenAsync();
        using (SqliteCommand mode = connection.CreateCommand())
        {
            mode.CommandText = "PRAGMA journal_mode;";
            Assert.Equal("wal", await mode.ExecuteScalarAsync());
        }
        foreach (string sql in new[]
        {
            $"UPDATE projects SET name = name WHERE id = '{projectId}';",
            $"UPDATE trackers SET name = name WHERE id = '{trackerId}';",
            $"UPDATE tracked_entities SET notes = notes WHERE id = '{entityId}';",
            $"UPDATE schema_dependencies SET dependency_kind = dependency_kind WHERE dependent_entity_id = '{entityId}';",
            $"UPDATE unresolved_schema_dependencies SET dependency_kind = dependency_kind WHERE dependent_entity_id = '{entityId}';",
            $"UPDATE manual_dependency_overrides SET override_action = override_action WHERE dependent_entity_id = '{entityId}';",
            $"UPDATE entity_status_history SET new_status = new_status WHERE entity_id = '{entityId}';",
            $"UPDATE progress_snapshots SET ready_count = ready_count WHERE tracker_id = '{trackerId}';",
            $"UPDATE schema_import_summary SET import_mode = import_mode WHERE tracker_id = '{trackerId}';"
        })
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            Assert.True(await command.ExecuteNonQueryAsync() > 0);
            long next = (await store.ReadAsync(new ProjectId(snapshot.Project.Id))).Revision;
            Assert.True(next > revision, sql);
            revision = next;
        }

        await using SqliteConnection reader = new($"Data Source={file.DatabasePath}");
        await reader.OpenAsync();
        await using SqliteTransaction readTransaction = reader.BeginTransaction(deferred: true);
        using (SqliteCommand read = reader.CreateCommand())
        {
            read.Transaction = readTransaction;
            read.CommandText = "SELECT name FROM projects WHERE id = $id;";
            read.Parameters.AddWithValue("$id", projectId);
            Assert.Equal("Portable project", await read.ExecuteScalarAsync());
        }
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(4));
        await new SqliteProjectTrackerStore(db).RenameProjectAsync(
            new ProjectId(snapshot.Project.Id), "Renamed while read", timeout.Token);
        await readTransaction.CommitAsync();
    }

    [Fact]
    public async Task ImportOfDefaultNamedProjectReplacesOnlyPristineBootstrapCatalog()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase db = new(file.DatabasePath);
        await db.InitializeAsync();
        ProjectSnapshot seed = CompleteSnapshot();
        ProjectSnapshot snapshot = seed with
        {
            Project = seed.Project with { Name = "Default project" }
        };
        SqliteProjectSnapshotStore store = new(db);
        await store.ApplyAsync(snapshot, 0);
        Project imported = Assert.Single(await new SqliteProjectRepository(db).GetAllAsync());
        Assert.Equal(snapshot.Project.Id, imported.Id.Value);
        Assert.Equal("Default project", imported.Name);
    }

    private static ProjectSnapshot CompleteSnapshot()
    {
        Guid project = Guid.Parse("10000000-0000-0000-0000-000000000001");
        Guid trackerA = Guid.Parse("20000000-0000-0000-0000-000000000001");
        Guid trackerB = Guid.Parse("20000000-0000-0000-0000-000000000002");
        Guid entityA = Guid.Parse("30000000-0000-0000-0000-000000000001");
        Guid entityB = Guid.Parse("30000000-0000-0000-0000-000000000002");
        Guid entityC = Guid.Parse("30000000-0000-0000-0000-000000000003");
        Guid eventA = Guid.Parse("40000000-0000-0000-0000-000000000001");
        Guid eventB = Guid.Parse("40000000-0000-0000-0000-000000000002");
        SnapshotEntity first = new(entityA, trackerA, "Orders", "InProgress", "Keep notes",
            "Active", "ManualAndImported", 2, "Ada", "Core", T0, T1, T2,
            [new SnapshotDependency(entityA, entityB, "Mandatory", T0, T1)],
            [new SnapshotUnresolvedDependency(entityA, "External", "Optional", T0, T2)],
            [new SnapshotOverride(entityA, "External", "Suppress", T1, T2)]);
        SnapshotEntity second = new(entityB, trackerA, "Customers", "Reconciled", "Archived notes",
            "Archived", "Imported", 4, "Grace", "Legacy", T0, T1, T2, [], [], []);
        SnapshotEntity third = new(entityC, trackerB, "Copied entity", "NotStarted", "",
            "Active", "Copied", null, "", "", T0, T1, T2, [], [], []);
        SnapshotTracker a = new(trackerA, project, "Delivery", "Active", T0, T2, null,
            Guid.Parse("99999999-9999-9999-9999-999999999999"),
            [first, second],
            [new SnapshotStatusEvent(eventA, entityA, null, null, "NotStarted", "Created", T0, 0),
             new SnapshotStatusEvent(eventB, entityA, eventA, "NotStarted", "InProgress", "Transition", T1, 2),
             new SnapshotStatusEvent(Guid.NewGuid(), entityB, null, null, "Reconciled", "Baseline", T0, 1)],
            [new SnapshotProgress(Guid.NewGuid(), T0, 1, 0, 0, 0, 0, 1, 0),
             new SnapshotProgress(Guid.NewGuid(), T2, 0, 0, 1, 0, 0, 1, 1)],
            new SnapshotImportSummary(T2, "schema.csv", "Partial", 1, 2, 3, 4, 5));
        SnapshotTracker b = new(trackerB, project, "Archive", "Recycled", T0, T2, T2,
            trackerA, [third], [], [new SnapshotProgress(Guid.NewGuid(), T1, 1, 0, 0, 0, 0, 0, 0)], null);
        return new ProjectSnapshot(1,
            new SnapshotProject(project, "Portable project", "Active", T0, T2, null), [a, b]);
    }
}

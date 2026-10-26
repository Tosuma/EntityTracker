using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EntityTracker.Application.Tracking;
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

    [Theory]
    [InlineData(0)]
    [InlineData(ProjectSnapshot.CurrentFormatVersion + 1)]
    public void UnsupportedManifestFormat_ReportsApplicationAndProjectVersions(int projectVersion)
    {
        ProjectSnapshot snapshot = CompleteSnapshot();
        ProjectSnapshotPackage package = new ProjectSnapshotJsonCodec().Encode(snapshot);
        Dictionary<string, byte[]> files = package.Files.ToDictionary(pair => pair.Key, pair => pair.Value);
        files[".entitytracker/manifest.json"] = Encoding.UTF8.GetBytes(
            $"{{\"formatVersion\":{projectVersion},\"projectId\":\"{snapshot.Project.Id:D}\"}}");

        ProjectSnapshotFormatVersionException error =
            Assert.Throws<ProjectSnapshotFormatVersionException>(() =>
                new ProjectSnapshotJsonCodec().Decode(files));
        Assert.Equal(ProjectSnapshot.CurrentFormatVersion, error.ApplicationFormatVersion);
        Assert.Equal(projectVersion, error.ProjectFormatVersion);
    }

    [Fact]
    public async Task FilterActive_RoundTripsThroughSqliteAndVersionedSnapshots()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        ProjectSnapshot seed = CompleteSnapshot();
        SnapshotTracker tracker = seed.Trackers[0];
        SnapshotEntity entity = tracker.Entities[0] with
        {
            FilterActive = "  Active only\nBy region  "
        };
        seed = seed with { Trackers = [tracker with
        {
            Entities = tracker.Entities.Select(item => item.Id == entity.Id ? entity : item).ToArray()
        }, .. seed.Trackers.Skip(1)] };

        SqliteProjectSnapshotStore store = new(database);
        await store.ApplyAsync(seed, 0);
        ProjectSnapshot persisted = Assert.IsType<ProjectSnapshot>(
            (await store.ReadAsync(new ProjectId(seed.Project.Id))).Snapshot);
        SnapshotEntity loaded = persisted.Trackers.SelectMany(t => t.Entities)
            .Single(item => item.Id == entity.Id);
        Assert.Equal(entity.FilterActive, loaded.FilterActive);

        ProjectSnapshotJsonCodec codec = new();
        ProjectSnapshot decoded = codec.Decode(codec.Encode(persisted).Files);
        Assert.Equal(entity.FilterActive, decoded.Trackers.SelectMany(t => t.Entities)
            .Single(item => item.Id == entity.Id).FilterActive);

        ProjectSnapshot old = AsFormat(persisted, 3);
        ProjectSnapshot olderDecoded = codec.Decode(codec.Encode(old).Files);
        Assert.Equal(string.Empty, olderDecoded.Trackers.SelectMany(t => t.Entities)
            .Single(item => item.Id == entity.Id).FilterActive);

        ProjectSnapshot versionTwo = AsFormat(persisted, 2);
        ProjectSnapshotPackage legacyPackage = codec.Encode(versionTwo);
        Assert.DoesNotContain("filterActive", Encoding.UTF8.GetString(legacyPackage.Files
            .Single(pair => pair.Key.EndsWith($"entities/{entity.Id:D}.json", StringComparison.Ordinal)).Value));
        Assert.Equal(string.Empty, codec.Decode(legacyPackage.Files).Trackers
            .SelectMany(item => item.Entities).Single(item => item.Id == entity.Id).FilterActive);
    }

    [Fact]
    public async Task DueDate_RoundTripsThroughSqliteAndVersionedSnapshots()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        ProjectSnapshot seed = CompleteSnapshot();
        SnapshotTracker tracker = seed.Trackers[0];
        SnapshotEntity entity = tracker.Entities[0] with { DueDate = new DateOnly(2026, 10, 26) };
        seed = seed with { Trackers = [tracker with
        {
            Entities = tracker.Entities.Select(item => item.Id == entity.Id ? entity : item).ToArray()
        }, .. seed.Trackers.Skip(1)] };

        SqliteProjectSnapshotStore store = new(database);
        await store.ApplyAsync(seed, 0);
        ProjectSnapshot persisted = Assert.IsType<ProjectSnapshot>(
            (await store.ReadAsync(new ProjectId(seed.Project.Id))).Snapshot);
        Assert.Equal(entity.DueDate, persisted.Trackers.SelectMany(t => t.Entities)
            .Single(item => item.Id == entity.Id).DueDate);
        Assert.Equal(entity.DueDate, (await new SqliteEntityRepository(database)
            .GetAsync(new TrackerId(tracker.Id), new EntityId(entity.Id)))!.DueDate);

        ProjectSnapshotJsonCodec codec = new();
        ProjectSnapshotPackage package = codec.Encode(persisted);
        Assert.Contains("\"dueDate\": \"2026-10-26\"", Encoding.UTF8.GetString(package.Files
            .Single(pair => pair.Key.EndsWith($"entities/{entity.Id:D}.json", StringComparison.Ordinal)).Value));
        Assert.Equal(entity.DueDate, codec.Decode(package.Files).Trackers.SelectMany(t => t.Entities)
            .Single(item => item.Id == entity.Id).DueDate);
        // Entities without a due date keep their files as they were.
        SnapshotEntity other = persisted.Trackers.SelectMany(t => t.Entities).First(item => item.Id != entity.Id);
        Assert.DoesNotContain("dueDate", Encoding.UTF8.GetString(package.Files
            .Single(pair => pair.Key.EndsWith($"entities/{other.Id:D}.json", StringComparison.Ordinal)).Value));

        // An older format can't hold a due date: it is refused both when written and when read.
        Assert.Throws<InvalidDataException>(() => codec.Encode(persisted with { FormatVersion = 8 }));
        Dictionary<string, byte[]> forged = codec.Encode(persisted).Files.ToDictionary();
        string manifestPath = forged.Keys.Single(path => path.EndsWith("manifest.json", StringComparison.Ordinal));
        forged[manifestPath] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(forged[manifestPath]).Replace(
            $"\"formatVersion\": {ProjectSnapshot.CurrentFormatVersion}", "\"formatVersion\": 8",
            StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => codec.Decode(forged));
    }

    [Fact]
    public async Task Version25_AddsNullableDueDateColumns()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        await using (SqliteConnection connection = await database.OpenConnectionAsync(CancellationToken.None))
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                ALTER TABLE tracked_entities DROP COLUMN due_date;
                ALTER TABLE tracker_sync_baseline_entities DROP COLUMN due_date;
                PRAGMA user_version = 24;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await new SqliteDatabase(file.DatabasePath).InitializeAsync();

        await using SqliteConnection check = await database.OpenConnectionAsync(CancellationToken.None);
        await using SqliteCommand query = check.CreateCommand();
        query.CommandText = """
            SELECT (SELECT count(*) FROM pragma_table_info('tracked_entities') WHERE name = 'due_date' AND "notnull" = 0)
                 + (SELECT count(*) FROM pragma_table_info('tracker_sync_baseline_entities') WHERE name = 'due_date' AND "notnull" = 0);
            """;
        Assert.Equal(2L, Convert.ToInt64(await query.ExecuteScalarAsync()));
        query.CommandText = "PRAGMA user_version;";
        Assert.Equal((long)SqliteDatabase.CurrentSchemaVersion, Convert.ToInt64(await query.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task SharedNotes_RoundTripThroughSqliteAndVersionedSnapshots()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        ProjectSnapshot seed = CompleteSnapshot();
        SnapshotTracker tracker = seed.Trackers[0];
        SnapshotEntity entity = tracker.Entities[0] with
        {
            Notes = "Internal: vendor contract",
            SharedNotes = "  Agreed with the client\nto migrate in June  "
        };
        seed = seed with { Trackers = [tracker with
        {
            Entities = tracker.Entities.Select(item => item.Id == entity.Id ? entity : item).ToArray()
        }, .. seed.Trackers.Skip(1)] };

        SqliteProjectSnapshotStore store = new(database);
        await store.ApplyAsync(seed, 0);
        ProjectSnapshot persisted = Assert.IsType<ProjectSnapshot>(
            (await store.ReadAsync(new ProjectId(seed.Project.Id))).Snapshot);
        SnapshotEntity loaded = persisted.Trackers.SelectMany(t => t.Entities).Single(item => item.Id == entity.Id);
        Assert.Equal(entity.SharedNotes, loaded.SharedNotes);
        Assert.Equal(entity.Notes, loaded.Notes);

        ProjectSnapshotJsonCodec codec = new();
        ProjectSnapshotPackage package = codec.Encode(persisted);
        Assert.Equal(entity.SharedNotes, codec.Decode(package.Files).Trackers.SelectMany(t => t.Entities)
            .Single(item => item.Id == entity.Id).SharedNotes);
        // Entities without Shared notes keep their files as they were, so the new field does not rewrite them.
        SnapshotEntity other = persisted.Trackers.SelectMany(t => t.Entities).First(item => item.Id != entity.Id);
        Assert.DoesNotContain("sharedNotes", Encoding.UTF8.GetString(package.Files
            .Single(pair => pair.Key.EndsWith($"entities/{other.Id:D}.json", StringComparison.Ordinal)).Value));

        ProjectSnapshot versionFour = AsFormat(persisted, 4);
        ProjectSnapshotPackage olderPackage = codec.Encode(versionFour);
        Assert.DoesNotContain("sharedNotes", Encoding.UTF8.GetString(olderPackage.Files
            .Single(pair => pair.Key.EndsWith($"entities/{entity.Id:D}.json", StringComparison.Ordinal)).Value));
        Assert.Equal(string.Empty, codec.Decode(olderPackage.Files).Trackers.SelectMany(t => t.Entities)
            .Single(item => item.Id == entity.Id).SharedNotes);
    }

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
        Assert.Contains(imported.Trackers.SelectMany(t => t.Entities), e => e.Dependencies.Count > 0);

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
    public void ProgressSnapshots_OmitZeroNewCountsAndRoundTripNonzeroCounts()
    {
        ProjectSnapshotJsonCodec codec = new();
        ProjectSnapshot seed = CompleteSnapshot();
        ProjectSnapshotPackage legacyCompatible = codec.Encode(seed);
        byte[] oldProgress = legacyCompatible.Files.First(static file =>
            file.Key.Contains("/progress-history/", StringComparison.Ordinal)).Value;
        Assert.DoesNotContain("manuallyBlockedCount", Encoding.UTF8.GetString(oldProgress));
        Assert.DoesNotContain("reworkingCount", Encoding.UTF8.GetString(oldProgress));

        ProjectSnapshot updated = seed with
        {
            Trackers = seed.Trackers.Select(tracker => tracker with
            {
                ProgressHistory = tracker.ProgressHistory.Select(progress => progress with
                {
                    ManuallyBlockedCount = 2,
                    ReworkingCount = 1
                }).ToArray()
            }).ToArray()
        };
        ProjectSnapshot roundTrip = codec.Decode(codec.Encode(updated).Files);
        Assert.All(roundTrip.Trackers.SelectMany(static tracker => tracker.ProgressHistory),
            static progress =>
            {
                Assert.Equal(2, progress.ManuallyBlockedCount);
                Assert.Equal(1, progress.ReworkingCount);
            });
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
        // Out of order on purpose: the database and the snapshot keep it in name order.
        TrackerSyncBaseline baseline = new(
            new TrackerSyncStructure([
                new TrackerSyncEntity("order", true, 2, "Sales", [new TrackerSyncDependency("customer")], "Open only"),
                new TrackerSyncEntity("customer", false, null, "", [])]),
            new TrackerSyncStructure([new TrackerSyncEntity("customer", true, null, "Mine", [])]));
        ProjectSnapshot seed = CompleteSnapshot();
        seed = seed with { Trackers = seed.Trackers.Select((tracker, index) =>
            index == 0 ? tracker with { SyncBaseline = baseline } : tracker).ToArray() };
        string expected = JsonSerializer.Serialize(baseline.Normalize());
        SqliteProjectSnapshotStore source = new(sourceDb);
        SqliteProjectSnapshotStore target = new(targetDb);
        await source.ApplyAsync(seed, 0);
        ProjectSnapshot exported = Assert.IsType<ProjectSnapshot>(
            (await source.ReadAsync(new ProjectId(seed.Project.Id))).Snapshot);
        Assert.Equal(expected, JsonSerializer.Serialize(exported.Trackers[0].SyncBaseline));
        ProjectSnapshotJsonCodec codec = new();
        ProjectSnapshot decoded = codec.Decode(codec.Encode(exported).Files);
        await target.ApplyAsync(decoded, 0);
        ProjectSnapshot imported = Assert.IsType<ProjectSnapshot>(
            (await target.ReadAsync(new ProjectId(seed.Project.Id))).Snapshot);
        Assert.Equal(expected, JsonSerializer.Serialize(imported.Trackers[0].SyncBaseline));
    }

    [Fact]
    public void Format7_WritesIndentedFilesWithTheBaselineAsNestedJson()
    {
        ProjectSnapshot snapshot = WithBaseline(CompleteSnapshot());
        ProjectSnapshotJsonCodec codec = new();
        ProjectSnapshotPackage package = codec.Encode(snapshot);

        // Format 8 keeps the format 7 layout.
        Assert.True(ProjectSnapshot.CurrentFormatVersion >= 7);
        foreach ((string path, byte[] bytes) in package.Files)
        {
            string text = Encoding.UTF8.GetString(bytes);
            Assert.False(text.Contains('\r'), $"{path} has Windows line endings.");
            Assert.True(text.Contains("\n  \""), $"{path} is not indented.");
        }
        string tracker = Encoding.UTF8.GetString(package.Files[TrackerPath(snapshot.Trackers[0].Id)]);
        Assert.Contains("\"syncBaseline\": {", tracker);
        Assert.Contains("\"name\": \"customer\"", tracker);
        Assert.DoesNotContain("syncBaselineJson", tracker);
        Assert.DoesNotContain("\\\"", tracker);

        ProjectSnapshot decoded = codec.Decode(package.Files);
        Assert.Equal(JsonSerializer.Serialize(snapshot.Trackers[0].SyncBaseline!.Normalize()),
            JsonSerializer.Serialize(decoded.Trackers[0].SyncBaseline));
        Assert.Equal(package.Sha256, codec.Encode(decoded).Sha256);
    }

    [Fact]
    public void Format7_FilesWrittenOnOneLineStillRead()
    {
        ProjectSnapshot snapshot = WithBaseline(CompleteSnapshot());
        ProjectSnapshotJsonCodec codec = new();
        ProjectSnapshotPackage package = codec.Encode(snapshot);
        Dictionary<string, byte[]> oneLine = package.Files.ToDictionary(pair => pair.Key,
            pair => Encoding.UTF8.GetBytes(JsonNode.Parse(pair.Value)!.ToJsonString()));

        Assert.Equal(package.Sha256, codec.Encode(codec.Decode(oneLine)).Sha256);
    }

    [Fact]
    public void Format6_BaselineTextWithDependencyKindsReadsAsTheSameBaseline()
    {
        ProjectSnapshot current = WithBaseline(CompleteSnapshot());
        ProjectSnapshotJsonCodec codec = new();
        Dictionary<string, byte[]> files = codec.Encode(current with { FormatVersion = 6 }).Files
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        string trackerPath = TrackerPath(current.Trackers[0].Id);
        JsonObject tracker = JsonNode.Parse(files[trackerPath])!.AsObject();
        Assert.False(tracker.ContainsKey("syncBaseline"));
        // As an older version wrote it: one JSON string, with a kind on each dependency.
        tracker["syncBaselineJson"] = """
            {"Source":{"Entities":[{"Name":"order","Active":true,"RequestedPriority":2,"GroupName":"Sales","Dependencies":[{"Name":"customer","Kind":0}],"FilterActive":"Open only"},{"Name":"customer","Active":false,"RequestedPriority":null,"GroupName":"","Dependencies":[]}]},"Destination":{"Entities":[{"Name":"customer","Active":true,"RequestedPriority":null,"GroupName":"Mine","Dependencies":[]}]}}
            """;
        files[trackerPath] = Encoding.UTF8.GetBytes(tracker.ToJsonString());

        ProjectSnapshot decoded = codec.Decode(files);

        Assert.Equal(6, decoded.FormatVersion);
        Assert.Equal(JsonSerializer.Serialize(current.Trackers[0].SyncBaseline!.Normalize()),
            JsonSerializer.Serialize(decoded.Trackers[0].SyncBaseline));
    }

    [Theory]
    [InlineData(6, true)]
    [InlineData(7, false)]
    public void EntityProvenance_FromVersionsBefore2_0_IsReadAndIgnoredOnlyInFormat6(int formatVersion, bool reads)
    {
        // Format 6 versions before app-v2.0.0 wrote each entity's provenance.
        ProjectSnapshot snapshot = CompleteSnapshot() with { FormatVersion = formatVersion };
        ProjectSnapshotJsonCodec codec = new();
        Dictionary<string, byte[]> files = codec.Encode(snapshot).Files.ToDictionary(pair => pair.Key, pair =>
        {
            if (!pair.Key.Contains("/entities/", StringComparison.Ordinal)) return pair.Value;
            JsonObject entity = JsonNode.Parse(pair.Value)!.AsObject();
            Assert.False(entity.ContainsKey("provenance"));
            entity["provenance"] = "Manual";
            return Encoding.UTF8.GetBytes(entity.ToJsonString());
        });

        if (!reads)
        {
            Assert.Throws<InvalidDataException>(() => codec.Decode(files));
            return;
        }
        ProjectSnapshot withoutProvenance = codec.Decode(codec.Encode(snapshot).Files);
        ProjectSnapshot decoded = codec.Decode(files);
        Assert.Equal(JsonSerializer.Serialize(withoutProvenance), JsonSerializer.Serialize(decoded));
    }

    [Fact]
    public void Format6_UpgradesToTheCurrentFormatWithoutChangingItsHistory()
    {
        ProjectSnapshot format6 = WithBaseline(CompleteSnapshot()) with { FormatVersion = 6 };

        ProjectSnapshot upgraded = ProjectSnapshotUpgrade.ToCurrent(format6, T2);

        Assert.Equal(ProjectSnapshot.CurrentFormatVersion, upgraded.FormatVersion);
        // The format 1–5 conversions must not run again on a format 6 snapshot.
        Assert.Equal(Serialized(format6), Serialized(upgraded));

        static string Serialized(ProjectSnapshot snapshot) => JsonSerializer.Serialize(snapshot.Trackers.Select(
            tracker => new
            {
                tracker.StatusHistory,
                Dependencies = tracker.Entities.Select(entity => entity.Dependencies),
                tracker.SyncBaseline
            }));
    }

    private static ProjectSnapshot WithBaseline(ProjectSnapshot snapshot) => snapshot with
    {
        Trackers = snapshot.Trackers.Select((tracker, index) => index == 0
            ? tracker with
            {
                SyncBaseline = new TrackerSyncBaseline(
                    new TrackerSyncStructure([
                        new TrackerSyncEntity("order", true, 2, "Sales", [new TrackerSyncDependency("customer")], "Open only"),
                        new TrackerSyncEntity("customer", false, null, "", [])]),
                    new TrackerSyncStructure([new TrackerSyncEntity("customer", true, null, "Mine", [])]))
            }
            : tracker).ToArray()
    };

    private static string TrackerPath(Guid trackerId) =>
        $".entitytracker/trackers/{trackerId:D}/tracker.json";

    [Fact]
    public async Task LargerSnapshotExportPreservesEntityRelationshipsAndCanonicalHash()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase db = new(file.DatabasePath);
        await db.InitializeAsync();
        ProjectSnapshot seed = CompleteSnapshot();
        SnapshotTracker tracker = seed.Trackers[0];
        SnapshotEntity template = tracker.Entities[0];
        SnapshotEntity[] additions = Enumerable.Range(0, 100).SelectMany(index =>
        {
            Guid id = Guid.NewGuid();
            SnapshotEntity external = Placeholder(Guid.NewGuid(), tracker.Id, $"External {index:D3}");
            return new[]
            {
                template with
                {
                    Id = id,
                    SourceName = $"Extra entity {index:D3}",
                    // No status history, so Not started.
                    DevelopmentStatus = "NotStarted",
                    Dependencies =
                    [
                        new SnapshotDependency(tracker.Entities[1].Id, T0, T1),
                        new SnapshotDependency(external.Id, T0, T2)
                    ],
                    ResponsibilityPeriods = []
                },
                external
            };
        }).ToArray();
        ProjectSnapshot expanded = seed with { Trackers =
            [tracker with { Entities = tracker.Entities.Concat(additions).ToArray() }, seed.Trackers[1]] };
        SqliteProjectSnapshotStore store = new(db);
        await store.ApplyAsync(expanded, 0);

        ProjectSnapshot actual = Assert.IsType<ProjectSnapshot>(
            (await store.ReadAsync(new ProjectId(seed.Project.Id))).Snapshot);
        SnapshotTracker exported = actual.Trackers.Single(item => item.Id == tracker.Id);
        // The 3 seed entities (one a placeholder) and 100 added, each with a placeholder of its own.
        Assert.Equal(203, exported.Entities.Count);
        Assert.All(exported.Entities, entity =>
        {
            if (entity.Id == tracker.Entities[1].Id || entity.LifecycleState == "Placeholder") return;
            Assert.Equal(2, entity.Dependencies.Count);
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
        Assert.Throws<ProjectSnapshotFormatVersionException>(() =>
            ProjectSnapshotValidator.Validate(snapshot with { FormatVersion = ProjectSnapshot.CurrentFormatVersion + 1 }));
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
                    Dependencies = [new SnapshotDependency(entity.Id, T0, T1)]
                }, tracker.Entities[1]]
            }, snapshot.Trackers[1]]
        }));

        ProjectSnapshotPackage package = codec.Encode(snapshot);
        Dictionary<string, byte[]> malformed = package.Files.ToDictionary(p => p.Key, p => p.Value);
        malformed[".entitytracker/project.json"] = Encoding.UTF8.GetBytes("{bad json");
        Assert.Throws<InvalidDataException>(() => codec.Decode(malformed));
        malformed[".entitytracker/project.json"] = package.Files[".entitytracker/project.json"];
        malformed[".entitytracker/manifest.json"] = Encoding.UTF8.GetBytes(
            "{\"formatVersion\":" + (ProjectSnapshot.CurrentFormatVersion + 1) + ",\"projectId\":\"" + snapshot.Project.Id + "\"}");
        Assert.Throws<ProjectSnapshotFormatVersionException>(() => codec.Decode(malformed));

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
        EntityNameCollation.Register(connection);
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
            $"UPDATE entity_dependencies SET updated_at_utc = updated_at_utc WHERE dependent_entity_id = '{entityId}';",
            $"UPDATE entity_status_history SET new_status = new_status WHERE entity_id = '{entityId}';",
            $"UPDATE progress_snapshots SET ready_count = ready_count WHERE tracker_id = '{trackerId}';"
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

    [Fact]
    public async Task Format8_KeepsANotRelevantStatusThroughTheDatabase()
    {
        // #73: format 8 adds NotRelevant; a fresh database (migrated to version 24) stores it.
        await using TemporarySqliteFile file = new();
        SqliteDatabase db = new(file.DatabasePath);
        await db.InitializeAsync();
        SqliteProjectSnapshotStore store = new(db);
        ProjectSnapshot snapshot = WithNotRelevantEntity(CompleteSnapshot());

        await store.ApplyAsync(snapshot, 0);
        ProjectSnapshot read = Assert.IsType<ProjectSnapshot>(
            (await store.ReadAsync(new ProjectId(snapshot.Project.Id))).Snapshot);

        SnapshotEntity entity = read.Trackers.SelectMany(static tracker => tracker.Entities)
            .Single(static entity => entity.SourceName == "Orders");
        Assert.Equal("NotRelevant", entity.DevelopmentStatus);
        Assert.Contains(read.Trackers.SelectMany(static tracker => tracker.StatusHistory),
            static entry => entry.NewStatus == "NotRelevant");
    }

    [Fact]
    public async Task Version24_RebuildsTheStatusTablesWithEveryRowIndexAndTrigger()
    {
        // #73: SQLite can't widen a CHECK constraint in place, so version 24 rebuilds the two tables
        // with a status list from their own definition. Run it again on a filled database.
        await using TemporarySqliteFile file = new();
        SqliteDatabase db = new(file.DatabasePath);
        await db.InitializeAsync();
        ProjectSnapshot snapshot = CompleteSnapshot();
        await new SqliteProjectSnapshotStore(db).ApplyAsync(snapshot, 0);
        string connectionString = new SqliteConnectionStringBuilder { DataSource = file.DatabasePath, Pooling = false }.ToString();
        (long Entities, long Events, long Indexes, long Triggers) before = await CountAsync();
        await ExecuteAsync("PRAGMA user_version = 23;");

        await new SqliteDatabase(file.DatabasePath).InitializeAsync();

        Assert.Equal(before, await CountAsync());
        Assert.Equal((long)SqliteDatabase.CurrentSchemaVersion, await ScalarAsync("PRAGMA user_version;"));
        ProjectSnapshot read = Assert.IsType<ProjectSnapshot>(
            (await new SqliteProjectSnapshotStore(db).ReadAsync(new ProjectId(snapshot.Project.Id))).Snapshot);
        Assert.Equal(snapshot.Trackers.Sum(static tracker => tracker.Entities.Count),
            read.Trackers.Sum(static tracker => tracker.Entities.Count));

        async Task<(long, long, long, long)> CountAsync() => (
            await ScalarAsync("SELECT count(*) FROM tracked_entities;"),
            await ScalarAsync("SELECT count(*) FROM entity_status_history;"),
            await ScalarAsync("SELECT count(*) FROM sqlite_schema WHERE type = 'index' AND tbl_name IN ('tracked_entities', 'entity_status_history');"),
            await ScalarAsync("SELECT count(*) FROM sqlite_schema WHERE type = 'trigger';"));

        async Task<long> ScalarAsync(string sql)
        {
            await using SqliteConnection connection = new(connectionString);
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        async Task ExecuteAsync(string sql)
        {
            await using SqliteConnection connection = new(connectionString);
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public void Version24_AddsNotRelevantToEveryStatusList() =>
        Assert.Equal(
            "CREATE TABLE h (previous_status TEXT NULL CHECK (previous_status IS NULL OR previous_status IN " +
            "('NotStarted', 'InProgress', 'Reworking', 'NotRelevant')), new_status TEXT NOT NULL CHECK (new_status IN " +
            "('NotStarted', 'Reconciled', 'NotRelevant')), entry_kind TEXT CHECK (entry_kind IN ('Baseline', 'Created')))",
            SqliteDatabase.WithNotRelevantStatus(
                "CREATE TABLE h (previous_status TEXT NULL CHECK (previous_status IS NULL OR previous_status IN " +
                "('NotStarted', 'InProgress', 'Reworking')), new_status TEXT NOT NULL CHECK (new_status IN " +
                "('NotStarted', 'Reconciled')), entry_kind TEXT CHECK (entry_kind IN ('Baseline', 'Created')))"));

    [Fact]
    public void Format7_CannotHoldANotRelevantStatus() =>
        Assert.Throws<InvalidDataException>(() => ProjectSnapshotValidator.Validate(
            WithNotRelevantEntity(CompleteSnapshot()) with { FormatVersion = 7 }));

    // "Orders" goes from In progress to Not relevant, so its status history ends where its status is.
    private static ProjectSnapshot WithNotRelevantEntity(ProjectSnapshot snapshot) => snapshot with
    {
        Trackers = snapshot.Trackers.Select(tracker => tracker with
        {
            Entities = tracker.Entities.Select(entity => entity.SourceName == "Orders"
                ? entity with { DevelopmentStatus = "NotRelevant" } : entity).ToArray(),
            StatusHistory = [.. tracker.StatusHistory, .. tracker.StatusHistory
                .Where(entry => entry.NewStatus == "InProgress" && entry.Kind == "Transition")
                .Select(entry => entry with
                {
                    EventId = Guid.Parse("40000000-0000-0000-0000-000000000099"),
                    PreviousEventId = entry.EventId,
                    PreviousStatus = "InProgress",
                    NewStatus = "NotRelevant",
                    OccurredAtUtc = T2,
                    Order = 3
                })]
        }).ToArray()
    };

    private static ProjectSnapshot CompleteSnapshot()
    {
        Guid project = Guid.Parse("10000000-0000-0000-0000-000000000001");
        Guid trackerA = Guid.Parse("20000000-0000-0000-0000-000000000001");
        Guid trackerB = Guid.Parse("20000000-0000-0000-0000-000000000002");
        Guid entityA = Guid.Parse("30000000-0000-0000-0000-000000000001");
        Guid entityB = Guid.Parse("30000000-0000-0000-0000-000000000002");
        Guid entityC = Guid.Parse("30000000-0000-0000-0000-000000000003");
        Guid externalA = Guid.Parse("30000000-0000-0000-0000-000000000004");
        Guid eventA = Guid.Parse("40000000-0000-0000-0000-000000000001");
        Guid eventB = Guid.Parse("40000000-0000-0000-0000-000000000002");
        SnapshotEntity first = new(entityA, trackerA, "Orders", "InProgress", "Keep notes",
            "Active", 2, "", "Core", T0, T1, T2,
            [new SnapshotDependency(entityB, T0, T1), new SnapshotDependency(externalA, T0, T2)],
            [new SnapshotResponsibilityPeriod(Guid.Parse("50000000-0000-0000-0000-000000000001"),
                entityA, Guid.Parse("60000000-0000-0000-0000-000000000001"), T2, null)]);
        SnapshotEntity second = new(entityB, trackerA, "Customers", "Reconciled", "Archived notes",
            "Archived", 4, "", "Legacy", T0, T1, T2, [],
            [new SnapshotResponsibilityPeriod(Guid.Parse("50000000-0000-0000-0000-000000000002"),
                entityB, Guid.Parse("60000000-0000-0000-0000-000000000002"), T2, null)]);
        SnapshotEntity third = new(entityC, trackerB, "Copied entity", "NotStarted", "",
            "Active", null, "", "", T0, T1, T2, []);
        SnapshotTracker a = new(trackerA, project, "Delivery", "Active", T0, T2, null,
            Guid.Parse("99999999-9999-9999-9999-999999999999"),
            [first, second, Placeholder(externalA, trackerA, "External")],
            [new SnapshotStatusEvent(eventA, entityA, null, null, "NotStarted", "Created", T0, 0),
             new SnapshotStatusEvent(eventB, entityA, eventA, "NotStarted", "InProgress", "Transition", T1, 2),
             new SnapshotStatusEvent(Guid.NewGuid(), entityB, null, null, "Reconciled", "Baseline", T0, 1)],
            [new SnapshotProgress(Guid.NewGuid(), T0, 1, 0, 0, 0, 0, 1, 0),
             new SnapshotProgress(Guid.NewGuid(), T2, 0, 0, 1, 0, 0, 1, 1)]);
        SnapshotTracker b = new(trackerB, project, "Archive", "Recycled", T0, T2, T2,
            trackerA, [third], [], [new SnapshotProgress(Guid.NewGuid(), T1, 1, 0, 0, 0, 0, 0, 0)]);
        return new ProjectSnapshot(ProjectSnapshot.CurrentFormatVersion,
            new SnapshotProject(project, "Portable project", "Active", T0, T2, null), [a, b],
            [new SnapshotDeveloper(Guid.Parse("60000000-0000-0000-0000-000000000001"),
                project, "Ada", "", false),
             new SnapshotDeveloper(Guid.Parse("60000000-0000-0000-0000-000000000002"),
                project, "Grace", "", false)]);
    }

    private static SnapshotEntity Placeholder(Guid id, Guid trackerId, string name) =>
        new(id, trackerId, name, "NotStarted", "", "Placeholder", null, "", "", T0, T0, T0, [], []);

    /// <summary>
    /// The snapshot as an older format would hold it: no placeholders, no dependency lists (formats
    /// 1–5 kept imported dependencies instead), the old provenance names, and before format 3 no
    /// responsibility periods.
    /// </summary>
    private static ProjectSnapshot AsFormat(ProjectSnapshot snapshot, int formatVersion) => snapshot with
    {
        FormatVersion = formatVersion,
        Trackers = snapshot.Trackers.Select(tracker => tracker with
        {
            Entities = tracker.Entities.Where(entity => entity.LifecycleState != "Placeholder")
                .Select(entity => entity with
                {
                    LegacyProvenance = "ManualOnly",
                    Dependencies = [],
                    LegacyDependencies = [],
                    LegacyUnresolvedDependencies = [],
                    LegacyOverrides = [],
                    ResponsibilityPeriods = formatVersion >= 3 ? entity.ResponsibilityPeriods : null
                }).ToArray()
        }).ToArray()
    };
}

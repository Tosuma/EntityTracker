using EntityTracker.Application.Persistence;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Planning;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Workflow;
using EntityTracker.Application.Projects;
using EntityTracker.Application.Snapshots;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Persistence;
using EntityTracker.Infrastructure.Snapshots;
using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Tests.Persistence;

public sealed class ResponsibilityPeriodTests
{
    [Fact]
    public async Task RemoveCurrent_EndsOnlySelectedDeveloperAndKeepsHistory()
    {
        await using TemporarySqliteFile file = new();
        MutableTimeProvider time = new(new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero));
        SqliteDatabase database = new(file.DatabasePath, time);
        await database.InitializeAsync();
        Tracker tracker = Assert.Single(await new SqliteTrackerRepository(database).GetAllAsync());
        ProjectDeveloperService developers = new(new SqliteProjectDeveloperStore(database), time);
        ProjectDeveloper alice = await developers.CreateAsync(tracker.ProjectId, "AL");
        ProjectDeveloper bob = await developers.CreateAsync(tracker.ProjectId, "BO");
        TrackedEntity entity = new(EntityId.New(), tracker.Id, "Feature");
        SqliteTrackedStateStore store = new(database);
        SqliteResponsibilityPeriodRepository periods = new(database);
        await store.ApplyAsync(tracker.Id, new TrackedStateChangeSet([entity], [], [], [], [], [],
            responsibilitySelections: [new ResponsibilitySelection(entity.Id, [alice.Id, bob.Id])]));
        time.Advance(TimeSpan.FromHours(1));
        TrackedStateChangeSet remove = new([], [], [], [], [], [], responsibilityRemovals:
            [new ResponsibilityRemoval(entity.Id, alice.Id)]);
        await store.ApplyAsync(tracker.Id, remove);
        await store.ApplyAsync(tracker.Id, remove);
        ResponsibilityPeriod[] after = (await periods.GetByEntityAsync(entity.Id)).ToArray();
        Assert.Equal(time.GetUtcNow(), after.Single(period => period.DeveloperId == alice.Id).EndedAtUtc);
        Assert.True(after.Single(period => period.DeveloperId == bob.Id).IsCurrent);

        time.Advance(TimeSpan.FromHours(1));
        await store.ApplyAsync(tracker.Id, new TrackedStateChangeSet([], [], [], [], [], [],
            responsibilityAdditions: [new ResponsibilityAddition(entity.Id, alice.Id)]));
        ResponsibilityPeriod[] alicePeriods = (await periods.GetByEntityAsync(entity.Id))
            .Where(period => period.DeveloperId == alice.Id).ToArray();
        Assert.Equal(2, alicePeriods.Length);
        Assert.Single(alicePeriods, period => period.IsCurrent &&
            period.StartedAtUtc == time.GetUtcNow());
    }

    [Fact]
    public async Task AddCurrent_AssignsAlongsideOthersOnceAtWriteTimeAndRejectsRetiredDeveloper()
    {
        await using TemporarySqliteFile file = new();
        MutableTimeProvider time = new(new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero));
        SqliteDatabase database = new(file.DatabasePath, time);
        await database.InitializeAsync();
        Tracker tracker = Assert.Single(await new SqliteTrackerRepository(database).GetAllAsync());
        ProjectDeveloperService developers = new(new SqliteProjectDeveloperStore(database), time);
        ProjectDeveloper alice = await developers.CreateAsync(tracker.ProjectId, "AL");
        ProjectDeveloper bob = await developers.CreateAsync(tracker.ProjectId, "BO");
        TrackedEntity entity = new(EntityId.New(), tracker.Id, "Feature");
        SqliteTrackedStateStore store = new(database);
        SqliteResponsibilityPeriodRepository periods = new(database);
        await store.ApplyAsync(tracker.Id, new TrackedStateChangeSet([entity], [], [], [], [], [],
            responsibilitySelections: [new ResponsibilitySelection(entity.Id, [alice.Id])]));
        time.Advance(TimeSpan.FromHours(1));
        TrackedStateChangeSet addBob = new([], [], [], [], [], [], responsibilityAdditions:
            [new ResponsibilityAddition(entity.Id, bob.Id)]);
        await store.ApplyAsync(tracker.Id, addBob);
        await store.ApplyAsync(tracker.Id, addBob);
        ResponsibilityPeriod[] current = (await periods.GetByEntityAsync(entity.Id))
            .Where(period => period.IsCurrent).ToArray();
        Assert.Equal(2, current.Length);
        Assert.Equal(time.GetUtcNow(), current.Single(period => period.DeveloperId == bob.Id).StartedAtUtc);
        Assert.Equal(time.GetUtcNow().AddHours(-1),
            current.Single(period => period.DeveloperId == alice.Id).StartedAtUtc);
        await developers.SetRetiredAsync(tracker.ProjectId, bob.Id, true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAsync(tracker.Id, addBob));
        await store.ApplyAsync(tracker.Id,
            new TrackedStateChangeSet([], [], [entity.Id], [], [], []));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ApplyAsync(tracker.Id,
            new TrackedStateChangeSet([], [], [], [], [], [], responsibilityAdditions:
                [new ResponsibilityAddition(entity.Id, alice.Id)])));
    }

    [Fact]
    public async Task OverviewProjectsIndividualCurrentDevelopersForActiveAndArchivedEntities()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        Tracker tracker = Assert.Single(await new SqliteTrackerRepository(database).GetAllAsync());
        ProjectDeveloperService developers = new(new SqliteProjectDeveloperStore(database),
            trackers: new SqliteTrackerRepository(database));
        ProjectDeveloper alice = await developers.CreateAsync(tracker.ProjectId, "AL", "Alice");
        ProjectDeveloper bob = await developers.CreateAsync(tracker.ProjectId, "BO", "Bob");
        TrackedEntity active = new(EntityId.New(), tracker.Id, "Active");
        TrackedEntity archived = new(EntityId.New(), tracker.Id, "Archived");
        TrackedEntity blank = new(EntityId.New(), tracker.Id, "Blank");
        SqliteTrackedStateStore store = new(database);
        await store.ApplyAsync(tracker.Id, new TrackedStateChangeSet(
            [active, archived, blank], [], [], [], [], [],
            responsibilitySelections:
            [new ResponsibilitySelection(active.Id, [alice.Id, bob.Id]),
             new ResponsibilitySelection(archived.Id, [bob.Id])]));
        await store.ApplyAsync(tracker.Id,
            new TrackedStateChangeSet([], [], [archived.Id], [], [], []));

        EntityOverviewService overview = new(
            new SqliteEntityRepository(database), new SqliteEntityAuditReader(database),
            new SqliteDependencyRepository(database),
            new SqliteManualDependencyOverrideRepository(database), new DependencyRanker(),
            new EffectiveDependencyResolver(), new WorkflowReadinessEvaluator(),
            new PriorityPlanningService(), new SqliteResponsibilityPeriodRepository(database),
            developers);
        EntityOverviewResult result = await overview.GetAsync(tracker.Id);
        Assert.Equal([alice.Id, bob.Id], result.Items.Single(item => item.EntityId == active.Id)
            .CurrentDevelopers.Select(developer => developer.Id));
        Assert.Equal([bob.Id], result.ArchivedItems.Single(item => item.EntityId == archived.Id)
            .CurrentDevelopers.Select(developer => developer.Id));
        Assert.Empty(result.Items.Single(item => item.EntityId == blank.Id).CurrentDevelopers);

        await developers.ChangeDetailsAsync(tracker.ProjectId, alice.Id, "AX", "Alex");
        EntityOverviewResult renamed = await overview.GetAsync(tracker.Id);
        EntityOverviewDeveloper current = renamed.Items.Single(item => item.EntityId == active.Id)
            .CurrentDevelopers.Single(developer => developer.Id == alice.Id);
        Assert.Equal("AX", current.Initials);
        Assert.Equal("Alex", current.DisplayName);
    }

    [Fact]
    public async Task SelectionHistoryAndRetirementAreAtomicAndRoundTripThroughSnapshot()
    {
        await using TemporarySqliteFile file = new();
        MutableTimeProvider time = new(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero));
        SqliteDatabase database = new(file.DatabasePath, time);
        await database.InitializeAsync();
        Tracker tracker = Assert.Single(await new SqliteTrackerRepository(database).GetAllAsync());
        ProjectDeveloperService developers = new(new SqliteProjectDeveloperStore(database), time);
        ProjectDeveloper alice = await developers.CreateAsync(tracker.ProjectId, "AL", "Alice");
        ProjectDeveloper bob = await developers.CreateAsync(tracker.ProjectId, "BO", "Bob");
        TrackedEntity entity = new(EntityId.New(), tracker.Id, "Feature");
        SqliteTrackedStateStore store = new(database);
        SqliteResponsibilityPeriodRepository periods = new(database);
        await store.ApplyAsync(tracker.Id, Change([entity], entity.Id, [alice.Id, bob.Id]));
        ResponsibilityPeriod[] initial = (await periods.GetByEntityAsync(entity.Id)).ToArray();
        Assert.Equal(2, initial.Length);
        Assert.All(initial, p => Assert.Equal(time.GetUtcNow(), p.StartedAtUtc));

        time.Advance(TimeSpan.FromHours(1));
        await store.ApplyAsync(tracker.Id, Change([], entity.Id, [alice.Id, bob.Id, bob.Id]));
        Assert.Equal(initial.Select(p => p.Id), (await periods.GetByEntityAsync(entity.Id)).Select(p => p.Id));
        await store.ApplyAsync(tracker.Id, Change([], entity.Id, [bob.Id]));
        ResponsibilityPeriod closedAlice = Assert.Single(await periods.GetByEntityAsync(entity.Id),
            p => p.DeveloperId == alice.Id);
        Assert.Equal(time.GetUtcNow(), closedAlice.EndedAtUtc);

        time.Advance(TimeSpan.FromHours(1));
        await store.ApplyAsync(tracker.Id, Change([], entity.Id, [alice.Id, bob.Id]));
        Assert.Equal(2, (await periods.GetByEntityAsync(entity.Id)).Count(p => p.DeveloperId == alice.Id));

        time.Advance(TimeSpan.FromHours(1));
        await developers.SetRetiredAsync(tracker.ProjectId, alice.Id, true);
        Assert.All((await periods.GetByEntityAsync(entity.Id)).Where(p => p.DeveloperId == alice.Id),
            p => Assert.NotNull(p.EndedAtUtc));
        await developers.SetRetiredAsync(tracker.ProjectId, alice.Id, false);
        Assert.Single(await periods.GetByEntityAsync(entity.Id), p => p.IsCurrent);

        SqliteProjectSnapshotStore snapshots = new(database);
        ProjectSnapshot snapshot = Assert.IsType<ProjectSnapshot>((await snapshots.ReadAsync(tracker.ProjectId)).Snapshot);
        ProjectSnapshotJsonCodec codec = new();
        ProjectSnapshot decoded = codec.Decode(codec.Encode(snapshot).Files);
        SnapshotEntity snapshotEntity = Assert.Single(decoded.Trackers.Single(t => t.Id == tracker.Id.Value).Entities);
        Assert.Equal(3, snapshotEntity.ResponsibilityPeriods!.Count);

        Guid[] periodIds = initial.Select(p => p.Id)
            .Concat((await periods.GetByEntityAsync(entity.Id)).Select(p => p.Id))
            .Distinct().Order().ToArray();
        await store.ApplyAsync(tracker.Id, new TrackedStateChangeSet([], [], [entity.Id], [], [], []));
        Assert.Equal(periodIds, (await periods.GetByEntityAsync(entity.Id))
            .Select(p => p.Id).Order().ToArray());
        await store.ApplyAsync(tracker.Id, new TrackedStateChangeSet([], [], [], [], [], [],
            entityIdsToRestore: [entity.Id]));
        Assert.Equal(periodIds, (await periods.GetByEntityAsync(entity.Id))
            .Select(p => p.Id).Order().ToArray());
    }

    [Fact]
    public async Task VersionSeventeenBackfillSplitsAndDeduplicatesAtMigrationTime()
    {
        await using TemporarySqliteFile file = new();
        MutableTimeProvider time = new(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero));
        SqliteDatabase database = new(file.DatabasePath, time);
        await database.InitializeAsync();
        Tracker tracker = Assert.Single(await new SqliteTrackerRepository(database).GetAllAsync());
        ProjectDeveloperService developers = new(new SqliteProjectDeveloperStore(database), time);
        ProjectDeveloper existing = await developers.CreateAsync(tracker.ProjectId, "bo", "Bob");
        TrackedEntity entity = new(EntityId.New(), tracker.Id, "Legacy",
            responsibleDeveloper: " AL, BO, al, , CC ");
        Assert.True(await new SqliteEntityRepository(database).TryAddAsync(tracker.Id, entity));
        await using (SqliteConnection connection = new($"Data Source={file.DatabasePath}"))
        {
            await connection.OpenAsync();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                DROP TABLE responsibility_periods;
                ALTER TABLE tracked_entities ADD COLUMN responsible_developer TEXT NOT NULL DEFAULT '';
                UPDATE tracked_entities SET responsible_developer = ' AL, BO, al, , CC '
                    WHERE id = $entity;
                PRAGMA user_version = 17;
                """;
            command.Parameters.AddWithValue("$entity", entity.Id.Value.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }
        time.Advance(TimeSpan.FromDays(1));
        await database.InitializeAsync();
        ResponsibilityPeriod[] migrated = (await new SqliteResponsibilityPeriodRepository(database)
            .GetByEntityAsync(entity.Id)).ToArray();
        Assert.Equal(3, migrated.Length);
        Assert.All(migrated, p => Assert.Equal(time.GetUtcNow(), p.StartedAtUtc));
        Assert.Contains(migrated, p => p.DeveloperId == existing.Id);
        Assert.Contains(await developers.ListAsync(tracker.ProjectId), d => d.Initials == "AL");
        Assert.Contains(await developers.ListAsync(tracker.ProjectId), d => d.Initials == "CC");
    }

    [Fact]
    public async Task VersionTwoSnapshotImportsLegacyTextAsCurrentPeriods()
    {
        await using TemporarySqliteFile file = new();
        MutableTimeProvider time = new(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero));
        SqliteDatabase database = new(file.DatabasePath, time);
        await database.InitializeAsync();
        Guid projectId = Guid.NewGuid();
        Guid trackerId = Guid.NewGuid();
        Guid entityId = Guid.NewGuid();
        DateTimeOffset created = time.GetUtcNow().AddDays(-30);
        ProjectSnapshot older = new(2,
            new SnapshotProject(projectId, "Imported", "Active", created, created, null),
            [new SnapshotTracker(trackerId, projectId, "Tracker", "Active", created, created,
                null, null,
                [new SnapshotEntity(entityId, trackerId, "Legacy", "NotStarted", "",
                    "Active", "Imported", null, "AL, BO, al", "", created, created,
                    created, [], [], [])], [], [], null)], []);
        ProjectSnapshotJsonCodec codec = new();
        ProjectSnapshot decoded = codec.Decode(codec.Encode(older).Files);
        await new SqliteProjectSnapshotStore(database).ApplyAsync(decoded, 0);

        ProjectSnapshot current = Assert.IsType<ProjectSnapshot>((await new SqliteProjectSnapshotStore(database)
            .ReadAsync(new ProjectId(projectId))).Snapshot);
        Assert.Equal(ProjectSnapshot.CurrentFormatVersion, current.FormatVersion);
        SnapshotEntity entity = Assert.Single(Assert.Single(current.Trackers).Entities);
        Assert.Equal(2, entity.ResponsibilityPeriods!.Count);
        Assert.All(entity.ResponsibilityPeriods, p => Assert.Equal(time.GetUtcNow(), p.StartedAtUtc));
        Assert.Equal(2, current.Developers!.Count);
        Assert.Empty(entity.ResponsibleDeveloper);
    }

    [Fact]
    public async Task InvalidProjectDeveloperLeavesEntityCreationUnchanged()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        Tracker tracker = Assert.Single(await new SqliteTrackerRepository(database).GetAllAsync());
        TrackedEntity entity = new(EntityId.New(), tracker.Id, "Feature");
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SqliteTrackedStateStore(database)
            .ApplyAsync(tracker.Id, Change([entity], entity.Id, [DeveloperId.New()])));
        Assert.Null(await new SqliteEntityRepository(database).GetAsync(tracker.Id, entity.Id));
    }

    private static TrackedStateChangeSet Change(IReadOnlyList<TrackedEntity> added,
        EntityId entityId, IReadOnlyList<DeveloperId> developers) =>
        new(added, [], [], [], [], [],
            responsibilitySelections: [new ResponsibilitySelection(entityId, developers)]);
}

using EntityTracker.Application.Importing;
using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Persistence;

using Microsoft.Data.Sqlite;

namespace EntityTracker.DemoData.Tests;

public sealed class ProgressDemoSeederTests
{
    [Fact]
    public async Task SeedAsync_ReplacesProgressButPreservesTrackedSchemaData()
    {
        await using TemporaryDatabase file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();

        TrackedEntity[] active = Enumerable.Range(1, 10)
            .Select(index => new TrackedEntity(
                new EntityId(Guid.Parse($"10000000-0000-0000-0000-{index:D12}")),
                $"Entity {index:D2}",
                index % 2 == 0
                    ? DevelopmentStatus.Reconciled
                    : DevelopmentStatus.InProgress,
                $"Keep note {index}",
                provenance: index == 10
                    ? EntityProvenance.ManualOnly
                    : EntityProvenance.Imported))
            .ToArray();
        TrackedEntity archived = new(
            new EntityId(Guid.Parse("20000000-0000-0000-0000-000000000001")),
            "Archived entity",
            DevelopmentStatus.Reconciled,
            "Keep archived note",
            EntityLifecycleState.Archived,
            EntityProvenance.Imported);
        PersistedDependency resolved = new(
            new DependencyEdge(active[1].Id, active[0].Id),
            ImportedDependencyKind.Mandatory);
        PersistedUnresolvedDependency unresolved = new(
            new UnresolvedDependency(active[2].Id, "Missing target"),
            ImportedDependencyKind.Mandatory);
        ManualDependencyOverride dependencyOverride = new(
            active[3].Id,
            active[0].SourceName,
            ManualDependencyOverrideAction.Add);
        await new SqliteTrackedStateStore(database).ApplyAsync(new TrackedStateChangeSet(
            [.. active, archived],
            [],
            [],
            [active[1].Id, active[2].Id],
            [resolved],
            [unresolved],
            reconciledOverrideOwnerIds: [active[3].Id],
            manualDependencyOverrides: [dependencyOverride]));

        EntityProjection[] expectedEntities = (await new SqliteEntityRepository(database)
                .GetAllAsync())
            .Select(EntityProjection.From)
            .ToArray();
        PersistedDependency[] expectedResolved =
            (await new SqliteDependencyRepository(database).GetAllAsync()).ToArray();
        PersistedUnresolvedDependency[] expectedUnresolved =
            (await new SqliteDependencyRepository(database).GetAllUnresolvedAsync()).ToArray();
        ManualDependencyOverride[] expectedOverrides =
            (await new SqliteManualDependencyOverrideRepository(database).GetAllAsync()).ToArray();

        ProgressDemoOptions options = new(
            days: 90,
            seed: 42,
            endDate: new DateOnly(2026, 3, 31),
            timeZone: TimeZoneInfo.Utc);
        ProgressDemoResult result = await new ProgressDemoSeeder().SeedAsync(
            file.DatabasePath,
            options);

        SqliteDatabase seededDatabase = new(file.DatabasePath);
        await seededDatabase.InitializeAsync();
        TrackedEntity[] seededEntities =
            (await new SqliteEntityRepository(seededDatabase).GetAllAsync()).ToArray();
        Assert.Equal(expectedEntities, seededEntities.Select(EntityProjection.From));
        Assert.Equal(
            expectedResolved,
            await new SqliteDependencyRepository(seededDatabase).GetAllAsync());
        Assert.Equal(
            expectedUnresolved,
            await new SqliteDependencyRepository(seededDatabase).GetAllUnresolvedAsync());
        Assert.Equal(
            expectedOverrides,
            await new SqliteManualDependencyOverrideRepository(seededDatabase).GetAllAsync());

        TrackedEntity seededArchived = Assert.Single(
            seededEntities,
            entity => entity.Id == archived.Id);
        Assert.Equal(EntityLifecycleState.Archived, seededArchived.LifecycleState);
        Assert.Equal(DevelopmentStatus.Reconciled, seededArchived.Status);
        DevelopmentStatus[] activeStatuses = seededEntities
            .Where(static entity => entity.LifecycleState == EntityLifecycleState.Active)
            .Select(static entity => entity.Status)
            .Distinct()
            .Order()
            .ToArray();
        Assert.Equal(Enum.GetValues<DevelopmentStatus>().Order(), activeStatuses);

        SqliteProgressHistoryRepository historyRepository = new(seededDatabase);
        EntityStatusHistoryEntry[] history =
            (await historyRepository.GetStatusHistoryAsync()).ToArray();
        ProgressSnapshot[] snapshots =
            (await historyRepository.GetProgressSnapshotsAsync()).ToArray();
        Assert.Equal(seededEntities.Length + result.TransitionCount, history.Length);
        Assert.Equal(result.SnapshotCount, snapshots.Length);
        Assert.True(snapshots.Select(static snapshot => snapshot.RecordedAtUtc.Date).Distinct().Count() > 10);
        Assert.Equal(
            new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
            snapshots[0].RecordedAtUtc);
        Assert.Equal(10, snapshots[^1].State.TotalActiveCount);

        foreach (TrackedEntity entity in seededEntities.Where(
                     static item => item.LifecycleState == EntityLifecycleState.Active))
        {
            EntityStatusHistoryEntry last = history.Last(entry => entry.EntityId == entity.Id);
            Assert.Equal(entity.Status, last.NewStatus);
        }
    }

    [Fact]
    public async Task SeedAsync_WhenThereAreNoActiveEntities_LeavesOriginalDatabaseUnchanged()
    {
        await using TemporaryDatabase file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        TrackedEntity archived = new(
            EntityId.New(),
            "Archived",
            lifecycleState: EntityLifecycleState.Archived);
        await new SqliteTrackedStateStore(database).ApplyAsync(new TrackedStateChangeSet(
            [archived], [], [], [], [], []));
        SqliteConnection.ClearAllPools();
        byte[] original = await File.ReadAllBytesAsync(file.DatabasePath);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ProgressDemoSeeder().SeedAsync(
                file.DatabasePath,
                new ProgressDemoOptions(
                    30,
                    1,
                    new DateOnly(2026, 3, 31),
                    TimeZoneInfo.Utc)));

        SqliteConnection.ClearAllPools();
        Assert.Equal(original, await File.ReadAllBytesAsync(file.DatabasePath));
    }

    private sealed record EntityProjection(
        EntityId Id,
        string SourceName,
        string Notes,
        EntityLifecycleState LifecycleState,
        EntityProvenance Provenance)
    {
        public static EntityProjection From(TrackedEntity entity) => new(
            entity.Id,
            entity.SourceName,
            entity.Notes,
            entity.LifecycleState,
            entity.Provenance);
    }
}

using System.Globalization;

using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Persistence;

using Microsoft.Data.Sqlite;

namespace EntityTracker.DemoData;

internal sealed record ProgressDemoOptions
{
    public ProgressDemoOptions(int days, int seed, DateOnly endDate, TimeZoneInfo timeZone)
    {
        if (days < 7)
        {
            throw new ArgumentOutOfRangeException(
                nameof(days),
                "Synthetic progress history must span at least seven days.");
        }

        ArgumentNullException.ThrowIfNull(timeZone);
        Days = days;
        Seed = seed;
        EndDate = endDate;
        TimeZone = timeZone;
    }

    public int Days { get; }

    public int Seed { get; }

    public DateOnly EndDate { get; }

    public TimeZoneInfo TimeZone { get; }

    public DateOnly StartDate => EndDate.AddDays(-(Days - 1));
}

internal sealed record ProgressDemoResult(
    string DatabasePath,
    DateOnly StartDate,
    DateOnly EndDate,
    int Seed,
    int ActiveEntityCount,
    int TransitionCount,
    int SnapshotCount,
    IReadOnlyDictionary<DevelopmentStatus, int> FinalStatusCounts);

internal sealed class ProgressDemoSeeder
{
    public async Task<ProgressDemoResult> SeedAsync(
        string databasePath,
        ProgressDemoOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(databasePath);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException(
                "The EntityTracker database path cannot be empty.",
                nameof(databasePath));
        }

        string fullPath = Path.GetFullPath(databasePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "The EntityTracker database does not exist. Run the application and import data first.",
                fullPath);
        }

        string workingPath = Path.Combine(
            Path.GetDirectoryName(fullPath)!,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.seed.tmp");
        try
        {
            await CloneDatabaseAsync(fullPath, workingPath, cancellationToken);
            ProgressDemoResult result = await SeedWorkingCopyAsync(
                workingPath,
                fullPath,
                options,
                cancellationToken);

            SqliteConnection.ClearAllPools();
            File.Replace(
                workingPath,
                fullPath,
                destinationBackupFileName: null,
                ignoreMetadataErrors: true);
            return result;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(workingPath))
            {
                File.Delete(workingPath);
            }
        }
    }

    private static async Task CloneDatabaseAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        SqliteConnectionStringBuilder sourceBuilder = new()
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadOnly
        };
        SqliteConnectionStringBuilder destinationBuilder = new()
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        };

        await using SqliteConnection source = new(sourceBuilder.ToString());
        await using SqliteConnection destination = new(destinationBuilder.ToString());
        await source.OpenAsync(cancellationToken);
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
    }

    private static async Task<ProgressDemoResult> SeedWorkingCopyAsync(
        string workingPath,
        string destinationPath,
        ProgressDemoOptions options,
        CancellationToken cancellationToken)
    {
        AdjustableTimeProvider timeProvider = new(DateTimeOffset.UnixEpoch);
        SqliteDatabase database = new(workingPath, timeProvider);
        await database.InitializeAsync(cancellationToken);

        SqliteEntityRepository entityRepository = new(database);
        TrackedEntity[] originalEntities =
            (await entityRepository.GetAllAsync(cancellationToken)).ToArray();
        TrackedEntity[] originalActive = originalEntities
            .Where(static entity => entity.LifecycleState == EntityLifecycleState.Active)
            .ToArray();
        if (originalActive.Length == 0)
        {
            throw new InvalidOperationException(
                "The database contains no active entities to seed.");
        }

        SyntheticProgressTimeline timeline = SyntheticProgressTimeline.Create(
            originalActive,
            options.StartDate,
            options.EndDate,
            options.TimeZone,
            options.Seed);
        await ResetProgressAsync(
            workingPath,
            timeline.BaselineAtUtc,
            cancellationToken);

        SqliteDependencyRepository dependencyRepository = new(database);
        SqliteManualDependencyOverrideRepository overrideRepository = new(database);
        SqliteTrackedStateStore store = new(database);
        EffectiveDependencyResolver dependencyResolver = new();
        ProgressSnapshotCalculator snapshotCalculator = new();

        Task<IReadOnlyList<TrackedEntity>> entitiesTask =
            entityRepository.GetAllAsync(cancellationToken);
        Task<IReadOnlyList<PersistedDependency>> resolvedTask =
            dependencyRepository.GetAllAsync(cancellationToken);
        Task<IReadOnlyList<PersistedUnresolvedDependency>> unresolvedTask =
            dependencyRepository.GetAllUnresolvedAsync(cancellationToken);
        Task<IReadOnlyList<ManualDependencyOverride>> overridesTask =
            overrideRepository.GetAllAsync(cancellationToken);
        await Task.WhenAll(entitiesTask, resolvedTask, unresolvedTask, overridesTask);

        TrackedEntity[] entities = (await entitiesTask).ToArray();
        EffectiveDependencyState effectiveDependencies = dependencyResolver.Resolve(
            entities,
            await resolvedTask,
            await unresolvedTask,
            await overridesTask);
        timeProvider.SetUtcNow(timeline.BaselineAtUtc);
        await store.EnsureHistoryBaselineAsync(
            entities,
            snapshotCalculator.Calculate(entities, effectiveDependencies),
            cancellationToken);

        Dictionary<EntityId, TrackedEntity> entitiesById = entities.ToDictionary(
            static entity => entity.Id);
        foreach (IGrouping<DateTimeOffset, ScheduledStatusChange> batch in
                 timeline.Changes.GroupBy(static change => change.OccurredAtUtc))
        {
            timeProvider.SetUtcNow(batch.Key);
            List<TrackedEntity> changedEntities = [];
            foreach (ScheduledStatusChange change in batch)
            {
                TrackedEntity entity = entitiesById[change.EntityId];
                entity.ChangeStatus(change.NewStatus);
                changedEntities.Add(entity);
            }

            await store.ApplyAsync(
                new TrackedStateChangeSet(
                    [],
                    [],
                    [],
                    [],
                    [],
                    [],
                    entitiesWithProgressToUpdate: changedEntities,
                    progressSnapshotAfterChanges: snapshotCalculator.Calculate(
                        entities,
                        effectiveDependencies)),
                cancellationToken);
        }

        return await ValidateAndCreateResultAsync(
            database,
            destinationPath,
            options,
            timeline,
            snapshotCalculator,
            effectiveDependencies,
            cancellationToken);
    }

    private static async Task ResetProgressAsync(
        string databasePath,
        DateTimeOffset baselineAtUtc,
        CancellationToken cancellationToken)
    {
        SqliteConnectionStringBuilder connectionString = new()
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            ForeignKeys = true
        };
        await using SqliteConnection connection = new(connectionString.ToString());
        await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM entity_status_history;
            DELETE FROM progress_snapshots;

            UPDATE tracked_entities
            SET development_status = 'NotStarted',
                progress_updated_at_utc = $baselineTimestamp
            WHERE lifecycle_state = 'Active';
            """;
        command.Parameters.AddWithValue(
            "$baselineTimestamp",
            baselineAtUtc.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<ProgressDemoResult> ValidateAndCreateResultAsync(
        SqliteDatabase database,
        string destinationPath,
        ProgressDemoOptions options,
        SyntheticProgressTimeline timeline,
        ProgressSnapshotCalculator snapshotCalculator,
        EffectiveDependencyState effectiveDependencies,
        CancellationToken cancellationToken)
    {
        SqliteEntityRepository entityRepository = new(database);
        TrackedEntity[] entities =
            (await entityRepository.GetAllAsync(cancellationToken)).ToArray();
        TrackedEntity[] active = entities
            .Where(static entity => entity.LifecycleState == EntityLifecycleState.Active)
            .ToArray();
        foreach ((EntityId id, DevelopmentStatus expectedStatus) in timeline.FinalStatuses)
        {
            TrackedEntity persisted = active.Single(entity => entity.Id == id);
            if (persisted.Status != expectedStatus)
            {
                throw new InvalidDataException(
                    $"Synthetic progress validation failed for entity '{persisted.SourceName}'.");
            }
        }

        SqliteProgressHistoryRepository historyRepository = new(database);
        EntityStatusHistoryEntry[] history =
            (await historyRepository.GetStatusHistoryAsync(cancellationToken)).ToArray();
        ProgressSnapshot[] snapshots =
            (await historyRepository.GetProgressSnapshotsAsync(cancellationToken)).ToArray();
        if (history.Length != entities.Length + timeline.Changes.Count ||
            history.Count(static entry => entry.Kind == StatusHistoryEntryKind.Baseline) !=
            entities.Length ||
            history.Count(static entry => entry.Kind == StatusHistoryEntryKind.Transition) !=
            timeline.Changes.Count)
        {
            throw new InvalidDataException(
                "Synthetic status history did not contain the expected baseline and transitions.");
        }

        if (snapshots.Length < 2 ||
            snapshots[0].RecordedAtUtc != timeline.BaselineAtUtc ||
            snapshots.Any(snapshot =>
                snapshot.RecordedAtUtc < timeline.BaselineAtUtc ||
                snapshot.RecordedAtUtc > timeline.Changes[^1].OccurredAtUtc))
        {
            throw new InvalidDataException(
                "Synthetic progress snapshots did not cover the requested timeline.");
        }

        ProgressSnapshotState expectedFinal = snapshotCalculator.Calculate(
            entities,
            effectiveDependencies);
        if (snapshots[^1].State != expectedFinal)
        {
            throw new InvalidDataException(
                "The final synthetic snapshot does not match the persisted entity state.");
        }

        IReadOnlyDictionary<DevelopmentStatus, int> finalCounts =
            Enum.GetValues<DevelopmentStatus>().ToDictionary(
                static status => status,
                status => active.Count(entity => entity.Status == status));
        return new ProgressDemoResult(
            destinationPath,
            options.StartDate,
            options.EndDate,
            options.Seed,
            active.Length,
            timeline.Changes.Count,
            snapshots.Length,
            finalCounts);
    }
}

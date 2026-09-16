global using EntityTracker.Infrastructure.Tests;

using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Synchronization;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Persistence;

namespace EntityTracker.Infrastructure.Tests;

internal static class TestTrackerExtensions
{
    internal static TrackerId GetTrackerId(this SqliteDatabase database) =>
        new SqliteTrackerRepository(database)
            .GetAllAsync()
            .GetAwaiter()
            .GetResult()
            .Single()
            .Id;

    internal static Task<TrackedEntity?> GetAsync(
        this SqliteEntityRepository repository,
        EntityId id,
        CancellationToken cancellationToken = default) =>
        repository.GetAsync(repository.Database.GetTrackerId(), id, cancellationToken);

    internal static Task<IReadOnlyList<TrackedEntity>> GetAllAsync(
        this SqliteEntityRepository repository,
        CancellationToken cancellationToken = default) =>
        repository.GetAllAsync(repository.Database.GetTrackerId(), cancellationToken);

    internal static Task<bool> TryAddAsync(
        this SqliteEntityRepository repository,
        TrackedEntity entity,
        CancellationToken cancellationToken = default) =>
        repository.TryAddAsync(repository.Database.GetTrackerId(), entity, cancellationToken);

    internal static Task<bool> UpdateSchemaMetadataAsync(
        this SqliteEntityRepository repository,
        TrackedEntity entity,
        CancellationToken cancellationToken = default) =>
        repository.UpdateSchemaMetadataAsync(
            repository.Database.GetTrackerId(),
            entity,
            cancellationToken);

    internal static Task<IReadOnlyList<PersistedDependency>> GetAllAsync(
        this SqliteDependencyRepository repository,
        CancellationToken cancellationToken = default) =>
        repository.GetAllAsync(repository.Database.GetTrackerId(), cancellationToken);

    internal static Task<IReadOnlyList<PersistedUnresolvedDependency>> GetAllUnresolvedAsync(
        this SqliteDependencyRepository repository,
        CancellationToken cancellationToken = default) =>
        repository.GetAllUnresolvedAsync(repository.Database.GetTrackerId(), cancellationToken);

    internal static Task SaveAsync(
        this SqliteDependencyRepository repository,
        PersistedDependency dependency,
        CancellationToken cancellationToken = default) =>
        repository.SaveAsync(repository.Database.GetTrackerId(), dependency, cancellationToken);

    internal static Task SaveUnresolvedAsync(
        this SqliteDependencyRepository repository,
        PersistedUnresolvedDependency dependency,
        CancellationToken cancellationToken = default) =>
        repository.SaveUnresolvedAsync(
            repository.Database.GetTrackerId(),
            dependency,
            cancellationToken);

    internal static Task<IReadOnlyList<ManualDependencyOverride>> GetAllAsync(
        this SqliteManualDependencyOverrideRepository repository,
        CancellationToken cancellationToken = default) =>
        repository.GetAllAsync(repository.Database.GetTrackerId(), cancellationToken);

    internal static Task ApplyAsync(
        this SqliteTrackedStateStore store,
        TrackedStateChangeSet changeSet,
        CancellationToken cancellationToken = default) =>
        store.ApplyAsync(store.Database.GetTrackerId(), changeSet, cancellationToken);

    internal static Task<SchemaImportSummary> ApplyAsync(
        this SqliteTrackedStateStore store,
        TrackedStateChangeSet changeSet,
        SchemaImportCompletion completion,
        CancellationToken cancellationToken = default) =>
        store.ApplyAsync(
            store.Database.GetTrackerId(),
            changeSet,
            completion,
            cancellationToken);

    internal static Task<SchemaImportSummary?> GetLatestImportAsync(
        this SqliteTrackedStateStore store,
        CancellationToken cancellationToken = default) =>
        store.GetLatestImportAsync(store.Database.GetTrackerId(), cancellationToken);

    internal static Task EnsureHistoryBaselineAsync(
        this SqliteTrackedStateStore store,
        IEnumerable<TrackedEntity> entities,
        ProgressSnapshotState snapshot,
        CancellationToken cancellationToken = default) =>
        store.EnsureHistoryBaselineAsync(
            store.Database.GetTrackerId(),
            entities,
            snapshot,
            cancellationToken);

    internal static Task<IReadOnlyList<EntityStatusHistoryEntry>> GetStatusHistoryAsync(
        this SqliteProgressHistoryRepository repository,
        CancellationToken cancellationToken = default) =>
        repository.GetStatusHistoryAsync(repository.Database.GetTrackerId(), cancellationToken);

    internal static Task<IReadOnlyList<ProgressSnapshot>> GetProgressSnapshotsAsync(
        this SqliteProgressHistoryRepository repository,
        CancellationToken cancellationToken = default) =>
        repository.GetProgressSnapshotsAsync(repository.Database.GetTrackerId(), cancellationToken);
}

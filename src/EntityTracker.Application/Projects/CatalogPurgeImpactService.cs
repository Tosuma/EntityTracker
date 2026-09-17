using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Projects;

public sealed class CatalogPurgeImpactService(
    ITrackerRepository trackerRepository,
    IEntityRepository entityRepository,
    IProgressHistoryRepository historyRepository,
    ISchemaSynchronizationStore synchronizationStore)
{
    public async Task<CatalogPurgeImpact> GetTrackerImpactAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        return await GetImpactAsync([trackerId], cancellationToken);
    }

    public async Task<CatalogPurgeImpact> GetProjectImpactAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        TrackerId[] trackerIds = (await trackerRepository.GetByProjectAsync(
                projectId,
                cancellationToken))
            .Select(static tracker => tracker.Id)
            .ToArray();
        return await GetImpactAsync(trackerIds, cancellationToken);
    }

    private async Task<CatalogPurgeImpact> GetImpactAsync(
        IReadOnlyList<TrackerId> trackerIds,
        CancellationToken cancellationToken)
    {
        int active = 0;
        int archived = 0;
        int statusHistory = 0;
        int snapshots = 0;
        int imports = 0;
        foreach (TrackerId trackerId in trackerIds)
        {
            IReadOnlyList<TrackedEntity> entities = await entityRepository.GetAllAsync(
                trackerId,
                cancellationToken);
            active += entities.Count(static entity =>
                entity.LifecycleState == EntityLifecycleState.Active);
            archived += entities.Count(static entity =>
                entity.LifecycleState == EntityLifecycleState.Archived);
            statusHistory += (await historyRepository.GetStatusHistoryAsync(
                trackerId,
                cancellationToken)).Count;
            snapshots += (await historyRepository.GetProgressSnapshotsAsync(
                trackerId,
                cancellationToken)).Count;
            imports += await synchronizationStore.GetLatestImportAsync(
                trackerId,
                cancellationToken) is null ? 0 : 1;
        }

        return new CatalogPurgeImpact(
            trackerIds.Count,
            active,
            archived,
            statusHistory,
            snapshots,
            imports);
    }
}

using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

public interface IProjectTrackerStore
{
    Task CreateProjectAsync(Project project, CancellationToken cancellationToken = default);

    Task CreateTrackerAsync(
        TrackerCreationState creation,
        CancellationToken cancellationToken = default);

    Task RenameProjectAsync(
        ProjectId projectId,
        string name,
        CancellationToken cancellationToken = default);

    Task RenameTrackerAsync(
        TrackerId trackerId,
        string name,
        CancellationToken cancellationToken = default);

    Task SetProjectLifecycleAsync(
        ProjectId projectId,
        CatalogLifecycleState lifecycleState,
        CancellationToken cancellationToken = default);

    Task SetTrackerLifecycleAsync(
        TrackerId trackerId,
        CatalogLifecycleState lifecycleState,
        CancellationToken cancellationToken = default);

    Task PurgeProjectAsync(ProjectId projectId, CancellationToken cancellationToken = default);

    Task PurgeTrackerAsync(TrackerId trackerId, CancellationToken cancellationToken = default);
}

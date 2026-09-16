using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tracking;

public sealed class CompatibilityTrackerResolver(
    IProjectRepository projectRepository,
    ITrackerRepository trackerRepository)
{
    public async Task<Tracker> ResolveAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Project> projects = await projectRepository.GetAllAsync(cancellationToken);
        HashSet<ProjectId> activeProjectIds = projects
            .Where(static project => project.LifecycleState == CatalogLifecycleState.Active)
            .Select(static project => project.Id)
            .ToHashSet();
        Tracker[] visible = (await trackerRepository.GetAllAsync(cancellationToken))
            .Where(tracker =>
                tracker.LifecycleState == CatalogLifecycleState.Active &&
                activeProjectIds.Contains(tracker.ProjectId))
            .ToArray();
        return visible.Length switch
        {
            1 => visible[0],
            0 => throw new InvalidOperationException(
                "EntityTracker cannot start because the catalog has no active tracker."),
            _ => throw new InvalidOperationException(
                "EntityTracker cannot start because more than one active tracker requires the UX-03 tracker selector.")
        };
    }
}

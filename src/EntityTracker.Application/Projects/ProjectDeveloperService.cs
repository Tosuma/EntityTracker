using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Projects;

public sealed class ProjectDeveloperService(IProjectDeveloperStore store, TimeProvider? timeProvider = null,
    ITrackerRepository? trackers = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    public async Task<ProjectId> ProjectForTrackerAsync(TrackerId trackerId,
        CancellationToken cancellationToken = default) =>
        (await (trackers ?? throw new InvalidOperationException("Tracker lookup is unavailable."))
            .GetAsync(trackerId, cancellationToken))?.ProjectId
        ?? throw new InvalidOperationException("The Tracker no longer exists.");

    public async Task<IReadOnlyList<ProjectDeveloper>> ListForTrackerAsync(TrackerId trackerId,
        CancellationToken cancellationToken = default) =>
        await ListAsync(await ProjectForTrackerAsync(trackerId, cancellationToken), cancellationToken);

    public async Task<ProjectDeveloper> CreateForTrackerAsync(TrackerId trackerId, string initials,
        string? displayName = null, CancellationToken cancellationToken = default) =>
        await CreateAsync(await ProjectForTrackerAsync(trackerId, cancellationToken),
            initials, displayName, cancellationToken);
    public Task<IReadOnlyList<ProjectDeveloper>> ListAsync(ProjectId projectId,
        CancellationToken cancellationToken = default) =>
        store.GetByProjectAsync(projectId, cancellationToken);

    public async Task<ProjectDeveloper> CreateAsync(ProjectId projectId, string initials,
        string? displayName = null, CancellationToken cancellationToken = default)
    {
        ProjectDeveloper developer = new(DeveloperId.New(), projectId, initials, displayName);
        await store.CreateAsync(developer, cancellationToken);
        return developer;
    }

    public async Task<ProjectDeveloper> ChangeDetailsAsync(ProjectId projectId, DeveloperId id,
        string initials, string? displayName, CancellationToken cancellationToken = default)
    {
        ProjectDeveloper developer = await RequireAsync(projectId, id, cancellationToken);
        developer.ChangeDetails(initials, displayName);
        await store.UpdateAsync(developer, cancellationToken);
        return developer;
    }

    public async Task<ProjectDeveloper> SetRetiredAsync(ProjectId projectId, DeveloperId id,
        bool retired, CancellationToken cancellationToken = default)
    {
        ProjectDeveloper developer = await RequireAsync(projectId, id, cancellationToken);
        if (retired) developer.Retire(); else developer.Restore();
        await store.SetRetiredAsync(developer, _timeProvider.GetUtcNow().ToUniversalTime(), cancellationToken);
        return developer;
    }

    private async Task<ProjectDeveloper> RequireAsync(ProjectId projectId, DeveloperId id,
        CancellationToken cancellationToken) =>
        (await store.GetByProjectAsync(projectId, cancellationToken)).SingleOrDefault(d => d.Id == id)
        ?? throw new InvalidOperationException("The developer no longer exists in this Project.");
}

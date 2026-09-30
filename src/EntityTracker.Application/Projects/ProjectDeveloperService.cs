using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Projects;

public sealed class ProjectDeveloperService(IProjectDeveloperStore store)
{
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
        await store.UpdateAsync(developer, cancellationToken);
        return developer;
    }

    private async Task<ProjectDeveloper> RequireAsync(ProjectId projectId, DeveloperId id,
        CancellationToken cancellationToken) =>
        (await store.GetByProjectAsync(projectId, cancellationToken)).SingleOrDefault(d => d.Id == id)
        ?? throw new InvalidOperationException("The developer no longer exists in this Project.");
}

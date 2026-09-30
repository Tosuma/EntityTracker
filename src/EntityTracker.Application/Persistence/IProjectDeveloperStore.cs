using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

public interface IProjectDeveloperStore
{
    Task<IReadOnlyList<ProjectDeveloper>> GetByProjectAsync(ProjectId projectId,
        CancellationToken cancellationToken = default);
    Task CreateAsync(ProjectDeveloper developer, CancellationToken cancellationToken = default);
    Task UpdateAsync(ProjectDeveloper developer, CancellationToken cancellationToken = default);
}

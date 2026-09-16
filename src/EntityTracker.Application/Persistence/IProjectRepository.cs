using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

public interface IProjectRepository
{
    Task<Project?> GetAsync(ProjectId projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken cancellationToken = default);
}

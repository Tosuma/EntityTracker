using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

public interface IProjectRepository
{
    Task<Project?> GetAsync(ProjectId projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<bool> IsNameReservedAsync(
        string name,
        ProjectId? excludingProjectId = null,
        CancellationToken cancellationToken = default);
}

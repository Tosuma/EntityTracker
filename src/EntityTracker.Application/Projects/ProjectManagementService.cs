using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Projects;

public sealed record PurgeProjectRequest(ProjectId ProjectId);

public sealed class ProjectManagementService(
    IProjectRepository repository,
    IProjectTrackerStore store,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<Project> CreateAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();
        Project project = new(ProjectId.New(), name, now, now);
        await store.CreateProjectAsync(project, cancellationToken);
        return project;
    }

    public async Task RenameAsync(
        ProjectId projectId,
        string name,
        CancellationToken cancellationToken = default)
    {
        await RequireAsync(projectId, cancellationToken);
        await store.RenameProjectAsync(projectId, name, cancellationToken);
    }

    public async Task RecycleAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        await RequireAsync(projectId, cancellationToken);
        await store.SetProjectLifecycleAsync(
            projectId,
            CatalogLifecycleState.Recycled,
            cancellationToken);
    }

    public async Task RestoreAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        await RequireAsync(projectId, cancellationToken);
        await store.SetProjectLifecycleAsync(
            projectId,
            CatalogLifecycleState.Active,
            cancellationToken);
    }

    public async Task PurgeAsync(
        PurgeProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Project project = await RequireAsync(request.ProjectId, cancellationToken);
        if (project.LifecycleState != CatalogLifecycleState.Recycled)
        {
            throw new InvalidOperationException("A project must be recycled before it can be purged.");
        }

        await store.PurgeProjectAsync(request.ProjectId, cancellationToken);
    }

    private async Task<Project> RequireAsync(
        ProjectId projectId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        return await repository.GetAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("The project no longer exists.");
    }
}

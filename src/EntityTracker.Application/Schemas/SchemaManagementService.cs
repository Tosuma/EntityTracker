using EntityTracker.Application.Persistence;
using EntityTracker.Application.Projects;
using EntityTracker.Domain;

namespace EntityTracker.Application.Schemas;

/// <summary>Creates, renames, recycles, restores and purges a Project's schemas.</summary>
public sealed class SchemaManagementService(
    IProjectRepository projectRepository,
    ISchemaStore store,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public Task<IReadOnlyList<SchemaSummary>> GetByProjectAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        return store.GetByProjectAsync(projectId, cancellationToken);
    }

    public async Task<CatalogNameValidationResult> ValidateNameAsync(
        ProjectId projectId,
        string? name,
        SchemaId? excludingSchemaId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        string normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
            return CatalogNameValidationResult.Invalid("Enter a schema name.");
        return await store.IsNameReservedAsync(projectId, normalized, excludingSchemaId, cancellationToken)
            ? CatalogNameValidationResult.Invalid(
                "That schema name is already reserved in this project, including by recycled schemas.")
            : CatalogNameValidationResult.Valid;
    }

    public async Task<Schema> CreateAsync(
        ProjectId projectId,
        string name,
        CancellationToken cancellationToken = default)
    {
        await RequireActiveProjectAsync(projectId, cancellationToken);
        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();
        Schema schema = new(SchemaId.New(), projectId, name, now, now);
        await store.CreateAsync(schema, cancellationToken);
        return schema;
    }

    public async Task RenameAsync(SchemaId schemaId, string name, CancellationToken cancellationToken = default)
    {
        await RequireSchemaAsync(schemaId, cancellationToken);
        await store.RenameAsync(schemaId, name, cancellationToken);
    }

    public async Task RecycleAsync(SchemaId schemaId, CancellationToken cancellationToken = default)
    {
        await RequireSchemaAsync(schemaId, cancellationToken);
        await store.SetLifecycleAsync(schemaId, CatalogLifecycleState.Recycled, cancellationToken);
    }

    public async Task RestoreAsync(SchemaId schemaId, CancellationToken cancellationToken = default)
    {
        Schema schema = await RequireSchemaAsync(schemaId, cancellationToken);
        await RequireActiveProjectAsync(schema.ProjectId, cancellationToken);
        await store.SetLifecycleAsync(schemaId, CatalogLifecycleState.Active, cancellationToken);
    }

    public async Task PurgeAsync(SchemaId schemaId, CancellationToken cancellationToken = default)
    {
        Schema schema = await RequireSchemaAsync(schemaId, cancellationToken);
        if (schema.LifecycleState != CatalogLifecycleState.Recycled)
            throw new InvalidOperationException("A schema must be recycled before it can be purged.");
        await store.PurgeAsync(schemaId, cancellationToken);
    }

    private async Task RequireActiveProjectAsync(ProjectId projectId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        Project project = await projectRepository.GetAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("The project no longer exists.");
        if (project.LifecycleState != CatalogLifecycleState.Active)
            throw new InvalidOperationException("The project is recycled.");
    }

    private async Task<Schema> RequireSchemaAsync(SchemaId schemaId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schemaId);
        return await store.GetAsync(schemaId, cancellationToken)
            ?? throw new InvalidOperationException("The schema no longer exists.");
    }
}

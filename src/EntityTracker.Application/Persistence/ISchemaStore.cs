using EntityTracker.Application.Schemas;
using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

/// <summary>Reads and writes a Project's schemas: their catalog rows, entities and dependencies.</summary>
public interface ISchemaStore
{
    Task<IReadOnlyList<SchemaSummary>> GetByProjectAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default);

    Task<Schema?> GetAsync(SchemaId schemaId, CancellationToken cancellationToken = default);

    Task<SchemaContent?> GetContentAsync(SchemaId schemaId, CancellationToken cancellationToken = default);

    Task<bool> IsNameReservedAsync(
        ProjectId projectId,
        string name,
        SchemaId? excludingSchemaId = null,
        CancellationToken cancellationToken = default);

    Task CreateAsync(Schema schema, CancellationToken cancellationToken = default);

    Task RenameAsync(SchemaId schemaId, string name, CancellationToken cancellationToken = default);

    Task SetLifecycleAsync(
        SchemaId schemaId,
        CatalogLifecycleState lifecycleState,
        CancellationToken cancellationToken = default);

    Task PurgeAsync(SchemaId schemaId, CancellationToken cancellationToken = default);

    /// <summary>Applies one edit of a schema's entities and dependencies in a single transaction.</summary>
    Task ApplyAsync(SchemaId schemaId, SchemaChangeSet changes, CancellationToken cancellationToken = default);
}

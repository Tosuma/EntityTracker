using EntityTracker.Domain;

namespace EntityTracker.Application.Schemas;

/// <summary>A schema as the Schemas page lists it.</summary>
public sealed record SchemaSummary(Schema Schema, int EntityCount);

/// <summary>A schema with all of its entities and dependencies.</summary>
public sealed record SchemaContent(
    Schema Schema,
    IReadOnlyList<SchemaEntity> Entities,
    IReadOnlyList<SchemaDependency> Dependencies);

/// <summary>
/// One edit of a schema. Entities are inserted or updated by ID; removing an entity also removes the
/// dependencies on and of it.
/// </summary>
public sealed record SchemaChangeSet(
    IReadOnlyList<SchemaEntity> UpsertedEntities,
    IReadOnlyList<SchemaEntityId> RemovedEntityIds,
    IReadOnlyList<SchemaDependency> AddedDependencies,
    IReadOnlyList<SchemaDependency> RemovedDependencies);

/// <summary>The outcome of an edit the user can correct: a message when it was refused.</summary>
public sealed record SchemaEditResult(bool Succeeded, string? ErrorMessage, SchemaEntity? Entity,
    IReadOnlyList<SchemaEntity> CreatedDependencies)
{
    public static SchemaEditResult Failed(string message) => new(false, message, null, []);
}

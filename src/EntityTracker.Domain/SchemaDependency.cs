namespace EntityTracker.Domain;

/// <summary>A schema entity's dependency on another entity of the same schema, by ID. A schema has no
/// unresolved names: every dependency points at one of its entities.</summary>
public sealed record SchemaDependency
{
    public SchemaDependency(SchemaEntityId dependentEntityId, SchemaEntityId dependencyEntityId)
    {
        ArgumentNullException.ThrowIfNull(dependentEntityId);
        ArgumentNullException.ThrowIfNull(dependencyEntityId);
        if (dependentEntityId == dependencyEntityId)
            throw new ArgumentException("An entity cannot depend on itself.", nameof(dependencyEntityId));
        DependentEntityId = dependentEntityId;
        DependencyEntityId = dependencyEntityId;
    }

    public SchemaEntityId DependentEntityId { get; }

    public SchemaEntityId DependencyEntityId { get; }
}

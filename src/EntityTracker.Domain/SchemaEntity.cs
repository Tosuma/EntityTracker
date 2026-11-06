namespace EntityTracker.Domain;

/// <summary>An entity of a <see cref="Schema"/>: a name and a group. Its dependencies are
/// <see cref="SchemaDependency"/> rows.</summary>
public sealed record SchemaEntity
{
    public SchemaEntity(SchemaEntityId id, SchemaId schemaId, string name, string groupName = "")
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(schemaId);
        ArgumentNullException.ThrowIfNull(name);
        string normalized = name.Trim();
        if (normalized.Length == 0)
            throw new ArgumentException("A schema entity name cannot be empty.", nameof(name));
        Id = id;
        SchemaId = schemaId;
        Name = normalized;
        GroupName = (groupName ?? string.Empty).Trim();
    }

    public SchemaEntityId Id { get; }

    public SchemaId SchemaId { get; }

    public string Name { get; }

    public string GroupName { get; }

    public SchemaEntity Rename(string name) => new(Id, SchemaId, name, GroupName);

    public SchemaEntity ChangeGroup(string groupName) => new(Id, SchemaId, Name, groupName);
}

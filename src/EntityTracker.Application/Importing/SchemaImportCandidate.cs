namespace EntityTracker.Application.Importing;

public sealed class SchemaImportCandidate
{
    public SchemaImportCandidate(
        IEnumerable<ImportedEntity> entities,
        IEnumerable<ImportedDependency> dependencies)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(dependencies);

        ImportedEntity[] entityArray = entities.ToArray();
        ImportedDependency[] dependencyArray = dependencies.ToArray();

        if (entityArray.Any(static entity => entity is null))
        {
            throw new ArgumentException("An import candidate cannot contain a null entity.", nameof(entities));
        }

        if (dependencyArray.Any(static dependency => dependency is null))
        {
            throw new ArgumentException(
                "An import candidate cannot contain a null dependency.",
                nameof(dependencies));
        }

        Entities = Array.AsReadOnly(entityArray);
        Dependencies = Array.AsReadOnly(dependencyArray);
    }

    public IReadOnlyList<ImportedEntity> Entities { get; }

    public IReadOnlyList<ImportedDependency> Dependencies { get; }
}

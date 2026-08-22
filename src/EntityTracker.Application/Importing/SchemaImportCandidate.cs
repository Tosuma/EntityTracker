namespace EntityTracker.Application.Importing;

public sealed class SchemaImportCandidate
{
    public SchemaImportCandidate(
        IEnumerable<ImportedEntity> entities,
        IEnumerable<ImportedDependency> dependencies)
        : this(entities, dependencies, [])
    {
    }

    public SchemaImportCandidate(
        IEnumerable<ImportedEntity> entities,
        IEnumerable<ImportedDependency> dependencies,
        IEnumerable<UnresolvedImportedDependency> unresolvedDependencies)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(unresolvedDependencies);

        ImportedEntity[] entityArray = entities.ToArray();
        ImportedDependency[] dependencyArray = dependencies.ToArray();
        UnresolvedImportedDependency[] unresolvedDependencyArray =
            unresolvedDependencies.ToArray();

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

        if (unresolvedDependencyArray.Any(static dependency => dependency is null))
        {
            throw new ArgumentException(
                "An import candidate cannot contain a null unresolved dependency.",
                nameof(unresolvedDependencies));
        }

        Entities = Array.AsReadOnly(entityArray);
        Dependencies = Array.AsReadOnly(dependencyArray);
        UnresolvedDependencies = Array.AsReadOnly(unresolvedDependencyArray);
    }

    public IReadOnlyList<ImportedEntity> Entities { get; }

    public IReadOnlyList<ImportedDependency> Dependencies { get; }

    public IReadOnlyList<UnresolvedImportedDependency> UnresolvedDependencies { get; }
}

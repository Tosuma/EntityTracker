using EntityTracker.Application.Importing;

namespace EntityTracker.Application.Tests.Importing;

public sealed class ImportedModelsTests
{
    [Fact]
    public void ImportedEntity_PreservesKeyAndTrimmedDisplayName()
    {
        EntitySourceKey sourceKey = EntitySourceKey.From("parent");

        ImportedEntity entity = new(sourceKey, " Parent ");

        Assert.Same(sourceKey, entity.SourceKey);
        Assert.Equal("Parent", entity.SourceName);
    }

    [Fact]
    public void ImportedDependency_RejectsSelfDependencyBySourceKeyValue()
    {
        EntitySourceKey dependentSourceKey = EntitySourceKey.From("Parent");
        EntitySourceKey dependencySourceKey = EntitySourceKey.From("parent");

        Assert.Throws<ArgumentException>(() => new ImportedDependency(
            dependentSourceKey,
            dependencySourceKey,
            ImportedDependencyKind.Mandatory));
    }

    [Fact]
    public void ImportedDependency_RejectsUndefinedKind()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImportedDependency(
            EntitySourceKey.From("child"),
            EntitySourceKey.From("parent"),
            (ImportedDependencyKind)999));
    }

    [Fact]
    public void SchemaImportCandidate_SnapshotsInputCollections()
    {
        List<ImportedEntity> entities =
        [
            new ImportedEntity(EntitySourceKey.From("parent"), "parent")
        ];
        List<ImportedDependency> dependencies = [];

        SchemaImportCandidate candidate = new(entities, dependencies);
        entities.Clear();

        Assert.Single(candidate.Entities);
        Assert.Empty(candidate.Dependencies);
    }
}

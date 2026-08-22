using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests.Persistence;

public sealed class PersistedDependencyTests
{
    [Fact]
    public void Constructor_PreservesStableEdgeAndImportedKind()
    {
        DependencyEdge edge = new(EntityId.New(), EntityId.New());

        PersistedDependency dependency = new(edge, ImportedDependencyKind.Optional);

        Assert.Same(edge, dependency.Edge);
        Assert.Equal(ImportedDependencyKind.Optional, dependency.Kind);
    }

    [Fact]
    public void Constructor_RejectsNullEdge()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PersistedDependency(null!, ImportedDependencyKind.Mandatory));
    }

    [Fact]
    public void Constructor_RejectsUndefinedKind()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PersistedDependency(
                new DependencyEdge(EntityId.New(), EntityId.New()),
                (ImportedDependencyKind)999));
    }
}

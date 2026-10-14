using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests.Persistence;

public sealed class PersistedDependencyTests
{
    [Fact]
    public void Constructor_PreservesStableEdge()
    {
        DependencyEdge edge = new(EntityId.New(), EntityId.New());

        PersistedDependency dependency = new(edge);

        Assert.Same(edge, dependency.Edge);
    }

    [Fact]
    public void Constructor_RejectsNullEdge()
    {
        Assert.Throws<ArgumentNullException>(() => new PersistedDependency(null!));
    }

    [Fact]
    public void UnresolvedConstructor_PreservesReference()
    {
        UnresolvedDependency unresolved = new(EntityId.New(), "facility");

        PersistedUnresolvedDependency dependency = new(unresolved);

        Assert.Same(unresolved, dependency.Dependency);
    }

    [Fact]
    public void UnresolvedConstructor_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new PersistedUnresolvedDependency(null!));
    }
}

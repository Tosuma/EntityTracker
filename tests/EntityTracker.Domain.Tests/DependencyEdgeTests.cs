namespace EntityTracker.Domain.Tests;

public sealed class DependencyEdgeTests
{
    [Fact]
    public void Constructor_PreservesDirectionAndUsesValueEquality()
    {
        EntityId dependentId = EntityId.New();
        EntityId dependencyId = EntityId.New();

        DependencyEdge first = new(dependentId, dependencyId);
        DependencyEdge second = new(dependentId, dependencyId);

        Assert.Same(dependentId, first.DependentEntityId);
        Assert.Same(dependencyId, first.DependencyEntityId);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Constructor_RejectsNullDependent()
    {
        Assert.Throws<ArgumentNullException>(() => new DependencyEdge(null!, EntityId.New()));
    }

    [Fact]
    public void Constructor_RejectsNullDependency()
    {
        Assert.Throws<ArgumentNullException>(() => new DependencyEdge(EntityId.New(), null!));
    }

    [Fact]
    public void Constructor_RejectsSelfDependency()
    {
        Guid value = Guid.NewGuid();
        EntityId dependentId = new(value);
        EntityId dependencyId = new(value);

        Assert.Throws<ArgumentException>(() => new DependencyEdge(dependentId, dependencyId));
    }
}

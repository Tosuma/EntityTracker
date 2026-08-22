using EntityTracker.Domain;

namespace EntityTracker.Domain.Tests;

public sealed class UnresolvedDependencyTests
{
    [Fact]
    public void Constructor_PreservesDependentAndTrimsSourceName()
    {
        EntityId dependentId = EntityId.New();

        UnresolvedDependency dependency = new(dependentId, " facility ");

        Assert.Same(dependentId, dependency.DependentEntityId);
        Assert.Equal("facility", dependency.DependencySourceName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsMissingSourceName(string sourceName)
    {
        Assert.Throws<ArgumentException>(() =>
            new UnresolvedDependency(EntityId.New(), sourceName));
    }
}

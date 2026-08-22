using EntityTracker.Domain;

namespace EntityTracker.Domain.Tests;

public sealed class ManualDependencyOverrideTests
{
    [Fact]
    public void Constructor_PreservesOwnerAndActionAndTrimsDependencyName()
    {
        EntityId ownerId = EntityId.New();

        ManualDependencyOverride dependencyOverride = new(
            ownerId,
            " target ",
            ManualDependencyOverrideAction.Suppress);

        Assert.Same(ownerId, dependencyOverride.DependentEntityId);
        Assert.Equal("target", dependencyOverride.DependencySourceName);
        Assert.Equal(ManualDependencyOverrideAction.Suppress, dependencyOverride.Action);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsMissingDependencyName(string dependencyName)
    {
        Assert.Throws<ArgumentException>(() => new ManualDependencyOverride(
            EntityId.New(),
            dependencyName,
            ManualDependencyOverrideAction.Add));
    }

    [Fact]
    public void Constructor_RejectsUnknownAction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManualDependencyOverride(
            EntityId.New(),
            "Target",
            (ManualDependencyOverrideAction)99));
    }
}

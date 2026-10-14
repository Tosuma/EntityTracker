using EntityTracker.Domain;

namespace EntityTracker.Domain.Tests;

public sealed class EntityDependencyTests
{
    [Fact]
    public void Constructor_RefersToTheTargetById()
    {
        EntityId ownerId = EntityId.New();
        EntityId targetId = EntityId.New();

        EntityDependency dependency = new(ownerId, targetId);

        Assert.Same(ownerId, dependency.DependentEntityId);
        Assert.Same(targetId, dependency.DependencyEntityId);
    }

    [Fact]
    public void Constructor_RejectsADependencyOnItself()
    {
        EntityId id = EntityId.New();

        Assert.Throws<ArgumentException>(() => new EntityDependency(id, id));
    }

    [Fact]
    public void Placeholder_GetsTheSameIdForTheSameNameInTheSameTracker()
    {
        TrackerId tracker = new(Guid.NewGuid());

        TrackedEntity first = TrackedEntity.Placeholder(tracker, " Customer ");
        TrackedEntity second = TrackedEntity.Placeholder(tracker, "CUSTOMER");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("Customer", first.SourceName);
        Assert.True(first.IsPlaceholder);
        Assert.NotEqual(first.Id, TrackedEntity.Placeholder(new TrackerId(Guid.NewGuid()), "Customer").Id);
        Assert.NotEqual(first.Id, TrackedEntity.Placeholder(tracker, "Invoice").Id);
    }
}

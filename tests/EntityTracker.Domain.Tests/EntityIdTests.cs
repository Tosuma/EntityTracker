namespace EntityTracker.Domain.Tests;

public sealed class EntityIdTests
{
    [Fact]
    public void Constructor_PreservesValueAndUsesValueEquality()
    {
        Guid value = Guid.NewGuid();

        EntityId first = new(value);
        EntityId second = new(value);

        Assert.Equal(value, first.Value);
        Assert.Equal(first, second);
    }

    [Fact]
    public void New_CreatesNonEmptyId()
    {
        EntityId id = EntityId.New();

        Assert.NotEqual(Guid.Empty, id.Value);
    }

    [Fact]
    public void Constructor_RejectsEmptyValue()
    {
        Assert.Throws<ArgumentException>(() => new EntityId(Guid.Empty));
    }
}

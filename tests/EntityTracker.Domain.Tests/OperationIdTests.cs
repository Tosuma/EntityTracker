namespace EntityTracker.Domain.Tests;

public sealed class OperationIdTests
{
    [Fact]
    public void ConstructorUsesValueEqualityAndRejectsEmpty()
    {
        Guid value = Guid.NewGuid();

        Assert.Equal(new OperationId(value), new OperationId(value));
        Assert.Throws<ArgumentException>(() => new OperationId(Guid.Empty));
        Assert.NotEqual(Guid.Empty, OperationId.New().Value);
    }
}

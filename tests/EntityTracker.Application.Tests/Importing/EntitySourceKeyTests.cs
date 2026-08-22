using EntityTracker.Application.Importing;

namespace EntityTracker.Application.Tests.Importing;

public sealed class EntitySourceKeyTests
{
    [Fact]
    public void From_TrimsAndNormalizesCase()
    {
        EntitySourceKey first = EntitySourceKey.From("  Sales.Order  ");
        EntitySourceKey second = EntitySourceKey.From("sales.order");

        Assert.Equal("SALES.ORDER", first.Value);
        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void From_RejectsMissingName(string? sourceName)
    {
        Assert.ThrowsAny<ArgumentException>(() => EntitySourceKey.From(sourceName!));
    }
}

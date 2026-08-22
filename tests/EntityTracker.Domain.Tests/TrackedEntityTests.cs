namespace EntityTracker.Domain.Tests;

public sealed class TrackedEntityTests
{
    [Fact]
    public void Constructor_PreservesIdentityAndSourceNameAndDefaultsStatus()
    {
        EntityId id = EntityId.New();

        TrackedEntity entity = new(id, " sales.Customer ");

        Assert.Same(id, entity.Id);
        Assert.Equal(" sales.Customer ", entity.SourceName);
        Assert.Equal(DevelopmentStatus.NotStarted, entity.Status);
    }

    [Fact]
    public void ChangeStatus_UpdatesStatus()
    {
        TrackedEntity entity = new(
            EntityId.New(),
            "sales.Customer",
            DevelopmentStatus.InProgress);

        entity.ChangeStatus(DevelopmentStatus.Completed);

        Assert.Equal(DevelopmentStatus.Completed, entity.Status);
    }

    [Fact]
    public void Constructor_RejectsNullIdentity()
    {
        Assert.Throws<ArgumentNullException>(() => new TrackedEntity(null!, "sales.Customer"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsMissingSourceName(string? sourceName)
    {
        Assert.ThrowsAny<ArgumentException>(() => new TrackedEntity(EntityId.New(), sourceName!));
    }

    [Fact]
    public void Constructor_RejectsUndefinedStatus()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TrackedEntity(EntityId.New(), "sales.Customer", (DevelopmentStatus)999));
    }

    [Fact]
    public void ChangeStatus_RejectsUndefinedStatusAndPreservesCurrentStatus()
    {
        TrackedEntity entity = new(EntityId.New(), "sales.Customer");

        Assert.Throws<ArgumentOutOfRangeException>(() => entity.ChangeStatus((DevelopmentStatus)999));
        Assert.Equal(DevelopmentStatus.NotStarted, entity.Status);
    }
}

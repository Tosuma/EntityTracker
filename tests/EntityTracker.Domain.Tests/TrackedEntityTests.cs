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
        Assert.Equal(string.Empty, entity.Notes);
        Assert.Equal(EntityLifecycleState.Active, entity.LifecycleState);
    }

    [Fact]
    public void Constructor_PreservesNotes()
    {
        TrackedEntity entity = new(
            EntityId.New(),
            "sales.Customer",
            DevelopmentStatus.InProgress,
            " Keep formatting. \nSecond line. ");

        Assert.Equal(" Keep formatting. \nSecond line. ", entity.Notes);
    }

    [Fact]
    public void ChangeSourceName_UpdatesSourceName()
    {
        TrackedEntity entity = new(EntityId.New(), "sales.Customer");

        entity.ChangeSourceName("sales.Client");

        Assert.Equal("sales.Client", entity.SourceName);
    }

    [Fact]
    public void ChangeSourceName_RejectsInvalidValueAndPreservesCurrentName()
    {
        TrackedEntity entity = new(EntityId.New(), "sales.Customer");

        Assert.Throws<ArgumentException>(() => entity.ChangeSourceName("   "));
        Assert.Equal("sales.Customer", entity.SourceName);
    }

    [Fact]
    public void ChangeNotes_UpdatesNotesWithoutNormalizingText()
    {
        TrackedEntity entity = new(EntityId.New(), "sales.Customer");

        entity.ChangeNotes("  Review with team.  ");

        Assert.Equal("  Review with team.  ", entity.Notes);
    }

    [Fact]
    public void Notes_RejectNullAndPreserveCurrentValue()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TrackedEntity(
                EntityId.New(),
                "sales.Customer",
                DevelopmentStatus.NotStarted,
                null!));

        TrackedEntity entity = new(
            EntityId.New(),
            "sales.Customer",
            DevelopmentStatus.NotStarted,
            "Existing");

        Assert.Throws<ArgumentNullException>(() => entity.ChangeNotes(null!));
        Assert.Equal("Existing", entity.Notes);
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

    [Fact]
    public void LifecycleState_CanBeArchivedAndReactivated()
    {
        TrackedEntity entity = new(EntityId.New(), "sales.Customer");

        entity.ChangeLifecycleState(EntityLifecycleState.Archived);
        Assert.Equal(EntityLifecycleState.Archived, entity.LifecycleState);

        entity.ChangeLifecycleState(EntityLifecycleState.Active);
        Assert.Equal(EntityLifecycleState.Active, entity.LifecycleState);
    }

    [Fact]
    public void LifecycleState_RejectsUndefinedValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrackedEntity(
            EntityId.New(),
            "sales.Customer",
            lifecycleState: (EntityLifecycleState)999));

        TrackedEntity entity = new(EntityId.New(), "sales.Customer");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            entity.ChangeLifecycleState((EntityLifecycleState)999));
        Assert.Equal(EntityLifecycleState.Active, entity.LifecycleState);
    }
}

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
        Assert.Equal(EntityProvenance.Imported, entity.Provenance);
        Assert.Null(entity.RequestedPriority);
        Assert.Equal(string.Empty, entity.ResponsibleDeveloper);
        Assert.Equal(string.Empty, entity.GroupName);
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
    public void ChangeStatus_AllowsForwardAndBackwardWorkflowCorrections()
    {
        TrackedEntity entity = new(
            EntityId.New(),
            "sales.Customer",
            DevelopmentStatus.InProgress);

        entity.ChangeStatus(DevelopmentStatus.DevelopmentCompleted);
        Assert.Equal(DevelopmentStatus.DevelopmentCompleted, entity.Status);

        entity.ChangeStatus(DevelopmentStatus.Reconciled);
        Assert.Equal(DevelopmentStatus.Reconciled, entity.Status);

        entity.ChangeStatus(DevelopmentStatus.NotStarted);
        Assert.Equal(DevelopmentStatus.NotStarted, entity.Status);
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

    [Fact]
    public void ManualOnlyProvenance_CanTransitionToManualAndImported()
    {
        TrackedEntity entity = new(
            EntityId.New(),
            "sales.Customer",
            provenance: EntityProvenance.ManualOnly);

        entity.ChangeProvenance(EntityProvenance.ManualAndImported);

        Assert.Equal(EntityProvenance.ManualAndImported, entity.Provenance);
    }

    [Fact]
    public void Provenance_RejectsUndefinedOrInvalidTransitions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrackedEntity(
            EntityId.New(),
            "sales.Customer",
            provenance: (EntityProvenance)999));

        TrackedEntity imported = new(EntityId.New(), "sales.Customer");
        Assert.Throws<InvalidOperationException>(() =>
            imported.ChangeProvenance(EntityProvenance.ManualAndImported));
        Assert.Equal(EntityProvenance.Imported, imported.Provenance);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void RequestedPriority_AcceptsSupportedValuesAndCanBeCleared(int priority)
    {
        TrackedEntity entity = new(
            EntityId.New(),
            "sales.Customer",
            requestedPriority: priority);

        Assert.Equal(priority, entity.RequestedPriority);

        entity.ChangeRequestedPriority(null);

        Assert.Null(entity.RequestedPriority);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void RequestedPriority_RejectsUnsupportedValuesWithoutChangingCurrentValue(int priority)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrackedEntity(
            EntityId.New(),
            "sales.Customer",
            requestedPriority: priority));

        TrackedEntity entity = new(
            EntityId.New(),
            "sales.Customer",
            requestedPriority: 3);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            entity.ChangeRequestedPriority(priority));
        Assert.Equal(3, entity.RequestedPriority);
    }

    [Theory]
    [InlineData("  Ada Lovelace  ", "Ada Lovelace")]
    [InlineData(" Platform Team ", "Platform Team")]
    [InlineData("  dEv Enablement  ", "dEv Enablement")]
    public void ResponsibleDeveloper_TrimsOuterWhitespaceAndPreservesText(
        string value,
        string expected)
    {
        TrackedEntity entity = new(
            EntityId.New(),
            "sales.Customer",
            responsibleDeveloper: value);

        Assert.Equal(expected, entity.ResponsibleDeveloper);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResponsibleDeveloper_MissingValueClearsAssignment(string? value)
    {
        TrackedEntity entity = new(
            EntityId.New(),
            "sales.Customer",
            responsibleDeveloper: "Existing Team");

        entity.ChangeResponsibleDeveloper(value);

        Assert.Equal(string.Empty, entity.ResponsibleDeveloper);
    }

    [Theory]
    [InlineData("  Finance  ", "Finance")]
    [InlineData(" Platform Data ", "Platform Data")]
    [InlineData("  cORE  ", "cORE")]
    public void GroupName_TrimsOuterWhitespaceAndPreservesText(
        string value,
        string expected)
    {
        TrackedEntity entity = new(
            EntityId.New(),
            "sales.Customer",
            groupName: value);

        Assert.Equal(expected, entity.GroupName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GroupName_MissingValueClearsGroup(string? value)
    {
        TrackedEntity entity = new(
            EntityId.New(),
            "sales.Customer",
            groupName: "Existing Group");

        entity.ChangeGroupName(value);

        Assert.Equal(string.Empty, entity.GroupName);
    }
}

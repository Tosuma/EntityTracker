using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests.Persistence;

public sealed class CollaborativeConflictTests
{
    [Theory]
    [InlineData(CollaborativeConflictField.Notes)]
    [InlineData(CollaborativeConflictField.RequestedPriority)]
    [InlineData(CollaborativeConflictField.ResponsibleDeveloper)]
    [InlineData(CollaborativeConflictField.GroupName)]
    public void Constructor_ScalarConflict_DoesNotRequireDependencyKey(
        CollaborativeConflictField field)
    {
        CollaborativeConflict conflict = new(
            EntitySourceKey.From("customer"),
            EntityId.New(),
            field,
            "base",
            "local",
            "remote");

        Assert.Equal(field, conflict.Field);
        Assert.Null(conflict.DependencyKey);
    }

    [Fact]
    public void Constructor_DependencyConflict_RequiresDependencyKey()
    {
        Assert.Throws<ArgumentException>(() => new CollaborativeConflict(
            EntitySourceKey.From("customer"),
            EntityId.New(),
            CollaborativeConflictField.ImportedDependency,
            "present",
            "removed",
            "present"));
    }

    [Fact]
    public void Constructor_ScalarConflict_RejectsDependencyKey()
    {
        Assert.Throws<ArgumentException>(() => new CollaborativeConflict(
            EntitySourceKey.From("customer"),
            EntityId.New(),
            CollaborativeConflictField.Notes,
            "base",
            "local",
            "remote",
            EntitySourceKey.From("account")));
    }

    [Fact]
    public void ConflictSet_RequiresAtLeastOneConflict()
    {
        Assert.Throws<ArgumentException>(() => new CollaborativeConflictSet([]));
    }

    [Fact]
    public void PersistenceInitializationResult_NormalizesWarnings()
    {
        PersistenceInitializationResult result = new([" warning ", "warning", "", "second"]);

        Assert.True(result.HasWarnings);
        Assert.Equal(["warning", "second"], result.Warnings);
    }
}

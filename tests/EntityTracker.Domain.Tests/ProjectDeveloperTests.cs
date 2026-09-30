namespace EntityTracker.Domain.Tests;

public sealed class ProjectDeveloperTests
{
    [Fact]
    public void DetailsAreTrimmedAndIdentitySurvivesEditsAndRetirement()
    {
        DeveloperId id = DeveloperId.New();
        ProjectId projectId = ProjectId.New();
        ProjectDeveloper developer = new(id, projectId, " AB ", " Alice Bob ");
        Assert.Equal("AB", developer.Initials);
        Assert.Equal("Alice Bob", developer.DisplayName);

        developer.ChangeDetails(" CD ", null);
        developer.Retire();
        Assert.Equal(id, developer.Id);
        Assert.Equal(projectId, developer.ProjectId);
        Assert.Equal("CD", developer.Initials);
        Assert.Equal(string.Empty, developer.DisplayName);
        Assert.True(developer.IsRetired);
        developer.Restore();
        Assert.False(developer.IsRetired);
    }

    [Fact]
    public void InitialsAreMandatory()
    {
        Assert.Throws<ArgumentException>(() =>
            new ProjectDeveloper(DeveloperId.New(), ProjectId.New(), "  "));
        Assert.Throws<ArgumentException>(() => new DeveloperId(Guid.Empty));
    }
}

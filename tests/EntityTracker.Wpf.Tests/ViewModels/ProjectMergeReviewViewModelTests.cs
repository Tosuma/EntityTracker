using EntityTracker.Application.GitSync;
using EntityTracker.Domain.Collaboration;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class ProjectMergeReviewViewModelTests
{
    [Fact]
    public void IndividualAndBulkChoicesRequireEveryConflictToBeResolved()
    {
        ProjectMergeReviewViewModel review = new([
            new ProjectMergeConflict("Project/Name", ProjectConflictKind.Field,
                "Before", "Local", "Remote"),
            new ProjectMergeConflict("Tracker/1/Entity/2/Notes", ProjectConflictKind.Field,
                "Before", "Own note", "Other note")
        ]);
        Assert.False(review.AllResolved);
        Assert.Throws<InvalidOperationException>(() => review.Decisions());
        review.Rows[0].IsLocal = true;
        Assert.False(review.AllResolved);
        review.Rows[1].IsRemote = true;
        Assert.True(review.AllResolved);
        Assert.Equal(MergeSide.Local, review.Decisions()["Project/Name"]);
        Assert.Equal(MergeSide.Remote, review.Decisions()["Tracker/1/Entity/2/Notes"]);
        review.ChooseAll(MergeSide.Local);
        Assert.All(review.Decisions().Values, side => Assert.Equal(MergeSide.Local, side));
    }

    [Fact]
    public void DisplayTextDoesNotReplaceConflictDecisionPath()
    {
        const string path = "Tracker/00000000-0000-0000-0000-000000000001";
        ProjectMergeReviewViewModel review = new([
            new ProjectMergeConflict(path, ProjectConflictKind.Addition,
                null, "{\"Name\":\"Local\"}", "{\"Name\":\"Remote\"}")
            {
                Display = new ProjectMergeConflictDisplay("Tracker: Local / Remote",
                    "Not present", "Name: Local", "Name: Remote")
            }
        ]);

        ProjectMergeChoiceRow row = Assert.Single(review.Rows);
        Assert.Equal("Tracker: Local / Remote", row.Title);
        Assert.Equal("Not present", row.BaseValue);
        Assert.Equal("Name: Local", row.LocalValue);
        Assert.Equal("Name: Remote", row.RemoteValue);
        Assert.Equal("Local value for Tracker: Local / Remote", row.LocalAutomationName);
        Assert.Equal("Keep remote value for Tracker: Local / Remote", row.KeepRemoteAutomationName);
        row.IsRemote = true;
        Assert.Equal(MergeSide.Remote, review.Decisions()[path]);
    }
}

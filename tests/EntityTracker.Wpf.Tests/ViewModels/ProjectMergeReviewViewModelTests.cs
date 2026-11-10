using EntityTracker.Application.GitSync;
using EntityTracker.Domain.Collaboration;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

/// <summary>A merge conflict reads as push review's changes do: a sentence, then the values to choose from.</summary>
public sealed class ProjectMergeReviewViewModelTests
{
    [Fact]
    public void AChangeBothSidesMadeSaysSoAndShowsWhatItWasBefore()
    {
        ProjectMergeChoiceRow row = Row(ProjectConflictKind.Field, "Check stock levels");

        Assert.Equal("Both this computer and the repository changed Orders: internal notes.", row.Description);
        Assert.True(row.HasBase);
        Assert.Equal("Check stock levels", row.BaseValue);
    }

    [Fact]
    public void AnAdditionHasNoEarlierValueToShow()
    {
        ProjectMergeChoiceRow row = Row(ProjectConflictKind.Addition, "Not present");

        Assert.Equal("This computer and the repository each added Orders: internal notes differently.", row.Description);
        Assert.False(row.HasBase);
    }

    [Fact]
    public void TheSummaryCountsWhatIsDecidedAndApplyNeedsEveryOne()
    {
        ProjectMergeReviewViewModel review = new([Conflict("a", ProjectConflictKind.Field, "x"), Conflict("b", ProjectConflictKind.Field, "y")]);
        Assert.Equal("0 of 2 conflicts decided. Choose a version for each to apply.", review.Summary);
        Assert.False(review.AllResolved);

        review.Rows[0].IsLocal = true;
        Assert.Equal("1 of 2 conflicts decided. Choose a version for each to apply.", review.Summary);

        review.ChooseAll(MergeSide.Remote);
        Assert.Equal("All 2 conflicts decided.", review.Summary);
        Assert.True(review.AllResolved);
        Assert.All(review.Decisions().Values, static side => Assert.Equal(MergeSide.Remote, side));
    }

    private static ProjectMergeChoiceRow Row(ProjectConflictKind kind, string baseValue) =>
        new(Conflict("Tracker/1", kind, baseValue));

    private static ProjectMergeConflict Conflict(string path, ProjectConflictKind kind, string baseValue) =>
        new(path, kind, baseValue, "Local", "Remote")
        {
            Display = new ProjectMergeConflictDisplay("Orders: internal notes", baseValue,
                "Add invoice validation before release", "Coordinate rollout with the fulfillment team")
        };
}

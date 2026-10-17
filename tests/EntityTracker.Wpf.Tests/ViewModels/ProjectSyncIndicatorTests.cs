using EntityTracker.Application.GitSync;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.ViewModels;

public sealed class ProjectSyncIndicatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WithoutALinkNothingIsShown()
    {
        ProjectSyncIndicator indicator = ProjectSyncIndicator.From(null, State(ProjectSyncStateKind.Unlinked), 5, Now);

        Assert.Equal(ProjectSyncIndicatorKind.None, indicator.Kind);
        Assert.False(indicator.IsShown);
    }

    [Theory]
    [InlineData("RemoteDeletedLocalKept")]
    [InlineData("Deleted")]
    public void AProjectWhoseRepositoryIsGoneShowsNothing(string status) =>
        Assert.Equal(ProjectSyncIndicatorKind.None,
            ProjectSyncIndicator.From(Link(status, 5), State(ProjectSyncStateKind.Idle), 5, Now).Kind);

    [Fact]
    public void AtTheRevisionOfTheLastSyncItIsSynced()
    {
        ProjectSyncIndicator indicator = ProjectSyncIndicator.From(
            Link("Current", 5, Now.AddMinutes(-3)), State(ProjectSyncStateKind.UpToDate), 5, Now);

        Assert.Equal(ProjectSyncIndicatorKind.Synced, indicator.Kind);
        Assert.StartsWith("Synced at ", indicator.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void AChangeAfterTheLastSyncIsUnsynced()
    {
        ProjectSyncIndicator indicator = ProjectSyncIndicator.From(
            Link("Current", 5, Now.AddMinutes(-3)), State(ProjectSyncStateKind.UpToDate), 6, Now);

        Assert.Equal(ProjectSyncIndicatorKind.Unsynced, indicator.Kind);
        Assert.StartsWith("Changes not synced yet. Last synced at ", indicator.Label, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("PendingPush")]
    [InlineData("PendingRemote")]
    [InlineData("PendingDeletion")]
    public void ACommitWaitingForPushIsUnsyncedEvenAtTheSameRevision(string status) =>
        Assert.Equal(ProjectSyncIndicatorKind.Unsynced,
            ProjectSyncIndicator.From(Link(status, 5), State(ProjectSyncStateKind.LocalPending), 5, Now).Kind);

    [Theory]
    [InlineData(ProjectSyncStateKind.Conflict)]
    [InlineData(ProjectSyncStateKind.AuthenticationRequired)]
    [InlineData(ProjectSyncStateKind.ConfigurationInvalid)]
    [InlineData(ProjectSyncStateKind.DeletionApproval)]
    [InlineData(ProjectSyncStateKind.Failed)]
    public void AProblemNeedsAttentionAndSaysWhatItIs(ProjectSyncStateKind kind)
    {
        ProjectSyncIndicator indicator = ProjectSyncIndicator.From(Link("Current", 5), State(kind, "Git push was refused."), 9, Now);

        Assert.Equal(ProjectSyncIndicatorKind.NeedsAttention, indicator.Kind);
        Assert.Equal("Sync needs attention: Git push was refused.", indicator.Label);
    }

    [Fact]
    public void SyncingWinsOverEverythingElse() =>
        Assert.Equal(ProjectSyncIndicatorKind.Syncing,
            ProjectSyncIndicator.From(Link("PendingPush", 5), State(ProjectSyncStateKind.Syncing), 9, Now).Kind);

    [Fact]
    public void APausedSyncIsShownAsPaused() =>
        Assert.Equal(ProjectSyncIndicatorKind.Paused,
            ProjectSyncIndicator.From(Link("Current", 5), State(ProjectSyncStateKind.UpdatePaused), 9, Now).Kind);

    [Fact]
    public void WithoutAKnownSyncTimeTheLabelLeavesItOut()
    {
        Assert.Equal("Changes not synced yet.",
            ProjectSyncIndicator.From(Link("Current", 5), State(ProjectSyncStateKind.Idle), 6, Now).Label);
        Assert.Equal("Synced with the Git repository.",
            ProjectSyncIndicator.From(Link("Current", 5), State(ProjectSyncStateKind.Idle), 5, Now).Label);
    }

    [Fact]
    public void ASyncOnAnEarlierDayShowsItsDate()
    {
        ProjectSyncIndicator indicator = ProjectSyncIndicator.From(
            Link("Current", 5, Now.AddDays(-2)), State(ProjectSyncStateKind.UpToDate), 5, Now);

        Assert.StartsWith("Synced on ", indicator.Label, StringComparison.Ordinal);
    }

    private static ProjectSyncLink Link(string status, long revision, DateTimeOffset? syncedAt = null) =>
        new(Guid.NewGuid(), "C:\\repo", "main", null, "abc", revision, "hash", "", status,
            LastSyncedAtUtc: syncedAt);

    private static ProjectSyncState State(ProjectSyncStateKind kind, string result = "") => new(kind, result);
}

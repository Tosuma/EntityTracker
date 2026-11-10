using EntityTracker.Application.GitSync;
using EntityTracker.Domain.Collaboration;
using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf.Tests.Services;

/// <summary>Import and sync wait for the answer to a question asked in the main window.</summary>
public sealed class InAppDialogsTests
{
    [Fact]
    public async Task AnsweringTheGitIdentityQuestionGivesTheChoiceAndClosesIt()
    {
        InAppDialogs dialogs = new();
        Task<GitIdentityChoice?> question = dialogs.AskGitIdentityAsync("Alice Brown", null);
        Assert.True(dialogs.IsGitIdentityOpen);
        Assert.True(dialogs.IsAnyOpen);
        Assert.Equal("Alice Brown", dialogs.GitIdentity!.Name);

        GitIdentityChoice choice = new("Alice Brown", "alice@example.com", GitConfigScope.Local);
        dialogs.AnswerGitIdentity(choice);

        Assert.Equal(choice, await question);
        Assert.False(dialogs.IsGitIdentityOpen);
        Assert.False(dialogs.IsAnyOpen);
    }

    [Fact]
    public async Task CancellingTheSyncClosesItsMergeReview()
    {
        InAppDialogs dialogs = new();
        using CancellationTokenSource sync = new();
        Task<IReadOnlyDictionary<string, MergeSide>?> review = dialogs.ReviewMergeAsync(
            [new ProjectMergeConflict("Tracker/1", ProjectConflictKind.Addition, null, "Local", "Remote")], sync.Token);
        Assert.True(dialogs.IsMergeReviewOpen);

        await sync.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => review);
        Assert.False(dialogs.IsMergeReviewOpen);
    }

    [Fact]
    public async Task CancellingTheMergeReviewGivesNoChoices()
    {
        InAppDialogs dialogs = new();
        Task<IReadOnlyDictionary<string, MergeSide>?> review = dialogs.ReviewMergeAsync(
            [new ProjectMergeConflict("Tracker/1", ProjectConflictKind.Addition, null, "Local", "Remote")]);

        dialogs.AnswerMergeReview(null);

        Assert.Null(await review);
    }

    [Fact]
    public async Task OneQuestionIsAskedAtATime()
    {
        InAppDialogs dialogs = new();
        Task<bool> approval = dialogs.ApproveDeletionAsync(["Tracker: Delivery"]);
        Task<IReadOnlyDictionary<string, MergeSide>?> review = dialogs.ReviewMergeAsync(
            [new ProjectMergeConflict("Tracker/1", ProjectConflictKind.Addition, null, "Local", "Remote")]);
        Assert.True(dialogs.IsDeletionApprovalOpen);
        Assert.False(dialogs.IsMergeReviewOpen);

        dialogs.AnswerDeletionApproval(true);
        Assert.True(await approval);
        await WaitUntilAsync(() => dialogs.IsMergeReviewOpen);
        Assert.False(dialogs.IsDeletionApprovalOpen);

        dialogs.AnswerMergeReview(null);
        Assert.Null(await review);
        Assert.False(dialogs.IsAnyOpen);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
            await Task.Delay(10);
        Assert.True(condition());
    }
}

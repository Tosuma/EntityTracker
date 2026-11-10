using System.ComponentModel;
using System.Runtime.CompilerServices;

using EntityTracker.Application.GitSync;
using EntityTracker.Domain.Collaboration;
using EntityTracker.Wpf.ViewModels;
using EntityTracker.Wpf.Views;

namespace EntityTracker.Wpf.Services;

/// <summary>The Git name and email to save, and where: the global or this repository's configuration.</summary>
public sealed record GitIdentityChoice(string Name, string Email, GitConfigScope Scope);

/// <summary>
/// The questions import and sync stop to ask, shown as dialogs in the main window rather than as windows of
/// their own: the Git name and email, a merge review and an approval of repository deletions. The window
/// stays open while closing waits for a sync, so they can be asked then too. One is shown at a time; the
/// one asking waits for the answer, and cancelling its work closes the dialog.
/// </summary>
public sealed class InAppDialogs : INotifyPropertyChanged
{
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private GitIdentityDialogViewModel? _gitIdentity;
    private ProjectMergeReviewViewModel? _mergeReview;
    private OutboundDeletionApprovalDialogViewModel? _deletionApproval;
    private TaskCompletionSource<GitIdentityChoice?>? _gitIdentityAnswer;
    private TaskCompletionSource<IReadOnlyDictionary<string, MergeSide>?>? _mergeAnswer;
    private TaskCompletionSource<bool>? _deletionAnswer;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The open Git name and email question, or null.</summary>
    public GitIdentityDialogViewModel? GitIdentity
    {
        get => _gitIdentity;
        private set => Set(ref _gitIdentity, value, nameof(IsGitIdentityOpen));
    }

    public bool IsGitIdentityOpen => GitIdentity is not null;

    /// <summary>The open merge review, or null.</summary>
    public ProjectMergeReviewViewModel? MergeReview
    {
        get => _mergeReview;
        private set => Set(ref _mergeReview, value, nameof(IsMergeReviewOpen));
    }

    public bool IsMergeReviewOpen => MergeReview is not null;

    /// <summary>The open approval of repository deletions, or null.</summary>
    public OutboundDeletionApprovalDialogViewModel? DeletionApproval
    {
        get => _deletionApproval;
        private set => Set(ref _deletionApproval, value, nameof(IsDeletionApprovalOpen));
    }

    public bool IsDeletionApprovalOpen => DeletionApproval is not null;

    /// <summary>Whether any of these questions is open.</summary>
    public bool IsAnyOpen => IsGitIdentityOpen || IsMergeReviewOpen || IsDeletionApprovalOpen;

    /// <summary>Asks for the Git name and email; null when the user cancelled.</summary>
    public Task<GitIdentityChoice?> AskGitIdentityAsync(
        string? currentName, string? currentEmail, CancellationToken cancellationToken = default) =>
        AskAsync<GitIdentityChoice?>(
            answer =>
            {
                _gitIdentityAnswer = answer;
                GitIdentity = new GitIdentityDialogViewModel(currentName, currentEmail);
            },
            () =>
            {
                GitIdentity = null;
                _gitIdentityAnswer = null;
            },
            cancellationToken);

    /// <summary>Asks which side to keep for each conflict; null when the user cancelled the sync.</summary>
    public Task<IReadOnlyDictionary<string, MergeSide>?> ReviewMergeAsync(
        IReadOnlyList<ProjectMergeConflict> conflicts, CancellationToken cancellationToken = default) =>
        AskAsync<IReadOnlyDictionary<string, MergeSide>?>(
            answer =>
            {
                _mergeAnswer = answer;
                MergeReview = new ProjectMergeReviewViewModel(conflicts);
            },
            () =>
            {
                MergeReview = null;
                _mergeAnswer = null;
            },
            cancellationToken);

    /// <summary>Asks whether these deletions may be synced; false when the user cancelled.</summary>
    public Task<bool> ApproveDeletionAsync(
        IReadOnlyList<string> deletedObjects, CancellationToken cancellationToken = default) =>
        AskAsync<bool>(
            answer =>
            {
                _deletionAnswer = answer;
                DeletionApproval = new OutboundDeletionApprovalDialogViewModel(deletedObjects);
            },
            () =>
            {
                DeletionApproval = null;
                _deletionAnswer = null;
            },
            cancellationToken);

    /// <summary>Answers the open Git name and email question; null cancels.</summary>
    public void AnswerGitIdentity(GitIdentityChoice? choice) => _gitIdentityAnswer?.TrySetResult(choice);

    /// <summary>Answers the open merge review: the reviewed choices, or null to cancel the sync.</summary>
    public void AnswerMergeReview(IReadOnlyDictionary<string, MergeSide>? decisions) =>
        _mergeAnswer?.TrySetResult(decisions);

    /// <summary>Answers the open deletion approval.</summary>
    public void AnswerDeletionApproval(bool approved) => _deletionAnswer?.TrySetResult(approved);

    private async Task<T> AskAsync<T>(
        Action<TaskCompletionSource<T>> open, Action close, CancellationToken cancellationToken)
    {
        await _oneAtATime.WaitAsync(cancellationToken);
        try
        {
            TaskCompletionSource<T> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
            open(answer);
            try
            {
                await using CancellationTokenRegistration cancelled =
                    cancellationToken.Register(() => answer.TrySetCanceled(cancellationToken));
                return await answer.Task;
            }
            finally
            {
                close();
            }
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private void Set<T>(ref T field, T value, string isOpenName, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(isOpenName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAnyOpen)));
    }
}

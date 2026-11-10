using System.Windows;
using EntityTracker.Application.GitSync;
using EntityTracker.Domain.Collaboration;
using EntityTracker.Wpf.ViewModels;
using EntityTracker.Wpf.Views;

namespace EntityTracker.Wpf.Services;

public sealed class WpfProjectMergeReview(InAppDialogs dialogs) : IProjectMergeReview
{
    // Sync asks from a background thread; the dialog opens in the main window, on the UI thread.
    public Task<IReadOnlyDictionary<string, MergeSide>?> ReviewAsync(
        IReadOnlyList<ProjectMergeConflict> conflicts, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return UiThread.RunAsync(() => dialogs.ReviewMergeAsync(conflicts, cancellationToken)).Unwrap();
    }
}

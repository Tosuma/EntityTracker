using System.Windows;
using EntityTracker.Application.GitSync;
using EntityTracker.Domain.Collaboration;
using EntityTracker.Wpf.ViewModels;
using EntityTracker.Wpf.Views;

namespace EntityTracker.Wpf.Services;

public sealed class WpfProjectMergeReview : IProjectMergeReview
{
    // Sync asks from a background thread; the dialog opens on the UI thread.
    public Task<IReadOnlyDictionary<string, MergeSide>?> ReviewAsync(
        IReadOnlyList<ProjectMergeConflict> conflicts, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return UiThread.RunAsync<IReadOnlyDictionary<string, MergeSide>?>(() =>
        {
            ProjectMergeReviewViewModel viewModel = new(conflicts);
            ProjectMergeReviewDialog dialog = new(viewModel) { Owner = System.Windows.Application.Current.MainWindow };
            return dialog.ShowDialog() == true ? viewModel.Decisions() : null;
        });
    }
}

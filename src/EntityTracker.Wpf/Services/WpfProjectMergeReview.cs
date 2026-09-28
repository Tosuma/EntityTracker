using System.Windows;
using EntityTracker.Application.GitSync;
using EntityTracker.Domain.Collaboration;
using EntityTracker.Wpf.ViewModels;
using EntityTracker.Wpf.Views;

namespace EntityTracker.Wpf.Services;

public sealed class WpfProjectMergeReview : IProjectMergeReview
{
    public Task<IReadOnlyDictionary<string, MergeSide>?> ReviewAsync(
        IReadOnlyList<ProjectMergeConflict> conflicts, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProjectMergeReviewViewModel viewModel = new(conflicts);
        ProjectMergeReviewDialog dialog = new(viewModel) { Owner = System.Windows.Application.Current.MainWindow };
        return Task.FromResult<IReadOnlyDictionary<string, MergeSide>?>(
            dialog.ShowDialog() == true ? viewModel.Decisions() : null);
    }
}

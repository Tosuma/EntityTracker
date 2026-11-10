using System.Windows;
using System.Windows.Controls;

using EntityTracker.Domain.Collaboration;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Views;

/// <summary>The merge review a sync asks for when local and remote changes conflict.</summary>
public partial class ProjectMergeReviewDialog : UserControl
{
    public ProjectMergeReviewDialog() => InitializeComponent();

    private InAppDialogs? Dialogs => DataContext as InAppDialogs;

    private ProjectMergeReviewViewModel? Review => Dialogs?.MergeReview;

    private void OnChooseLocal(object sender, RoutedEventArgs e) => Review?.ChooseAll(MergeSide.Local);

    private void OnChooseRemote(object sender, RoutedEventArgs e) => Review?.ChooseAll(MergeSide.Remote);

    private void OnApply(object sender, RoutedEventArgs e)
    {
        if (Review is { AllResolved: true } review) Dialogs!.AnswerMergeReview(review.Decisions());
    }

    private void OnCancel(object? sender, EventArgs e) => Dialogs?.AnswerMergeReview(null);
}

using System.Windows;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Views;

public partial class ProjectMergeReviewDialog : Window
{
    public ProjectMergeReviewDialog(ProjectMergeReviewViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => ChooseLocalButton.Focus();
    }

    private ProjectMergeReviewViewModel Review => (ProjectMergeReviewViewModel)DataContext;
    private void OnChooseLocal(object sender, RoutedEventArgs e) =>
        Review.ChooseAll(EntityTracker.Domain.Collaboration.MergeSide.Local);
    private void OnChooseRemote(object sender, RoutedEventArgs e) =>
        Review.ChooseAll(EntityTracker.Domain.Collaboration.MergeSide.Remote);
    private void OnApply(object sender, RoutedEventArgs e)
    {
        if (Review.AllResolved) DialogResult = true;
    }
}

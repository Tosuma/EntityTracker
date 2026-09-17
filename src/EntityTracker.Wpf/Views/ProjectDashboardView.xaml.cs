using System.Windows;
using System.Windows.Controls;

using EntityTracker.Application.Projects;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Views;

public partial class ProjectDashboardView : UserControl
{
    public ProjectDashboardView() => InitializeComponent();
    private ShellViewModel Shell => (ShellViewModel)DataContext;

    private void OnCreateTracker(object sender, RoutedEventArgs e)
    {
        if (Shell.SelectedProject is not null) Shell.Catalog.OpenCreateTracker(Shell.SelectedProject);
    }

    private void OnRenameProject(object sender, RoutedEventArgs e)
    {
        if (Shell.SelectedProject is not null) Shell.Catalog.OpenRenameProject(Shell.SelectedProject);
    }

    private async void OnOpenRecycleBin(object sender, RoutedEventArgs e) =>
        await Shell.Catalog.OpenRecycleBinAsync(Shell.SelectedProject);

    private async void OnOpenTracker(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TrackerDashboardSummary item)
            await Shell.OpenTrackerAsync(item.TrackerId);
    }

    private void OnRenameTracker(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TrackerDashboardSummary item &&
            Shell.Trackers.FirstOrDefault(tracker => tracker.Id == item.TrackerId) is { } tracker)
            Shell.Catalog.OpenRenameTracker(tracker);
    }

    private void OnRecycleTracker(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TrackerDashboardSummary item &&
            Shell.Trackers.FirstOrDefault(tracker => tracker.Id == item.TrackerId) is { } tracker)
            Shell.Catalog.RequestRecycle(tracker);
    }
}

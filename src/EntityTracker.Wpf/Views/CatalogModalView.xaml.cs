using System.Windows;
using System.Windows.Controls;

using EntityTracker.Domain;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Views;

public partial class CatalogModalView : UserControl
{
    public CatalogModalView() => InitializeComponent();
    private CatalogManagementViewModel ViewModel => (CatalogManagementViewModel)DataContext;

    private void OnBlankChecked(object sender, RoutedEventArgs e)
    {
        if (DataContext is CatalogManagementViewModel vm) vm.CreationMode = TrackerCreationMode.Blank;
    }
    private void OnCsvChecked(object sender, RoutedEventArgs e)
    {
        if (DataContext is CatalogManagementViewModel vm) vm.CreationMode = TrackerCreationMode.Csv;
    }
    private void OnCopyChecked(object sender, RoutedEventArgs e)
    {
        if (DataContext is CatalogManagementViewModel vm) vm.CreationMode = TrackerCreationMode.Copy;
    }
    private async void OnRestoreProject(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Project project) await ViewModel.RestoreAsync(project);
    }
    private async void OnRestoreTracker(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Tracker tracker) await ViewModel.RestoreAsync(tracker);
    }
    private async void OnPurgeProject(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Project project) await ViewModel.RequestPurgeAsync(project);
    }
    private async void OnPurgeTracker(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Tracker tracker) await ViewModel.RequestPurgeAsync(tracker);
    }
}

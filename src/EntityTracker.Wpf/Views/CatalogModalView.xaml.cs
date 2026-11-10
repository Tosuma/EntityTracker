using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

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
    private void OnSchemaChecked(object sender, RoutedEventArgs e)
    {
        if (DataContext is CatalogManagementViewModel vm) vm.CreationMode = TrackerCreationMode.Schema;
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
    private async void OnRestoreSchema(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Schema schema) await ViewModel.RestoreAsync(schema);
    }
    private async void OnPurgeSchema(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Schema schema) await ViewModel.RequestPurgeAsync(schema);
    }
    private async void OnPurgeTracker(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Tracker tracker) await ViewModel.RequestPurgeAsync(tracker);
    }

    /// <summary>A dialog with a field to fill in starts there; any other starts on Cancel.</summary>
    private void OnDialogOpened(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(FocusInitialElement, DispatcherPriority.Input);

    private void FocusInitialElement()
    {
        if (DataContext is not CatalogManagementViewModel viewModel)
        {
            return;
        }

        TextBox? textBox = viewModel.DialogKind switch
        {
            CatalogDialogKind.ProjectName or CatalogDialogKind.TrackerName => CatalogNameTextBox,
            CatalogDialogKind.TrackerCreation => TrackerNameTextBox,
            CatalogDialogKind.PurgeConfirmation => PurgeConfirmationTextBox,
            _ => null
        };

        if (textBox is not null)
        {
            textBox.Focus();
            textBox.SelectAll();
        }
        else
        {
            CancelButton.Focus();
        }
    }
}

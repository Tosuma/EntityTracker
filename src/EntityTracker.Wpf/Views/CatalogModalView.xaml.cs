using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using EntityTracker.Domain;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Views;

public partial class CatalogModalView : UserControl
{
    private IInputElement? _focusBeforeOpen;

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

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            _focusBeforeOpen = Keyboard.FocusedElement;
            Dispatcher.BeginInvoke(FocusInitialElement, DispatcherPriority.Input);
            return;
        }

        if (_focusBeforeOpen is UIElement element)
        {
            Dispatcher.BeginInvoke(() => element.Focus(), DispatcherPriority.Input);
        }

        _focusBeforeOpen = null;
    }

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

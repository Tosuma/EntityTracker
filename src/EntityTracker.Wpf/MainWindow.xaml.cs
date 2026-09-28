using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

using EntityTracker.Domain;
using EntityTracker.Wpf.ViewModels;
using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;
    private readonly MouseWheelScrollRouter _mouseWheelRouter = new();
    private bool _updatingSelectors;

    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
        Closed += OnClosed;
        PreviewMouseWheel += OnPreviewMouseWheel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ((System.Collections.Specialized.INotifyCollectionChanged)_viewModel.Notifications.Items)
            .CollectionChanged += OnNotificationsChanged;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        _mouseWheelRouter.Route(e);

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F &&
            (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
            !_viewModel.IsBusy &&
            !_viewModel.Catalog.IsOpen &&
            _viewModel.IsTrackerWorkspace &&
            WorkspaceView.TryOpenCurrentSearch())
        {
            e.Handled = true;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.InitializeAsync();
        SynchronizeSelectors();
        await Dispatcher.InvokeAsync(_viewModel.StartAutomaticSync,
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ((System.Collections.Specialized.INotifyCollectionChanged)_viewModel.Notifications.Items)
            .CollectionChanged -= OnNotificationsChanged;
        _viewModel.Dispose();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.SelectedProject) or
            nameof(ShellViewModel.SelectedTracker))
        {
            SynchronizeSelectors();
        }
    }

    private void SynchronizeSelectors()
    {
        _updatingSelectors = true;
        ProjectSelector.SelectedItem = _viewModel.SelectedProject;
        TrackerSelector.SelectedItem = _viewModel.SelectedTracker;
        _updatingSelectors = false;
    }

    private async void OnProjectSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSelectors || !IsLoaded || _viewModel.IsBusy)
        {
            return;
        }

        if (!await _viewModel.SelectProjectAsync(ProjectSelector.SelectedItem as Project))
        {
            SynchronizeSelectors();
        }
    }

    private async void OnTrackerSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSelectors || !IsLoaded || _viewModel.IsBusy)
        {
            return;
        }

        if (!await _viewModel.SelectTrackerAsync(TrackerSelector.SelectedItem as Tracker))
        {
            SynchronizeSelectors();
        }
    }

    private void OnDismissDefaultNamePrompt(object sender, RoutedEventArgs e) =>
        _viewModel.DismissDefaultNamePrompt();

    private void OnNotificationsChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        Dispatcher.BeginInvoke(() => NotificationScrollViewer.ScrollToEnd(),
            System.Windows.Threading.DispatcherPriority.Loaded);

    private void OnNotificationLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border card || !SystemParameters.ClientAreaAnimation) return;
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1,
            TimeSpan.FromMilliseconds(220)));
        if (card.RenderTransform is TranslateTransform transform)
            transform.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(220)));
    }

    private void OnRenameDefaultName(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedTracker is not null &&
            _viewModel.SelectedTracker.Name == "Default tracker")
        {
            _viewModel.Catalog.OpenRenameTracker(_viewModel.SelectedTracker);
        }
        else if (_viewModel.SelectedProject is not null)
        {
            _viewModel.Catalog.OpenRenameProject(_viewModel.SelectedProject);
        }
    }

    public FrameworkElement? FindWorkspaceElement(string name) =>
        WorkspaceView.FindWorkspaceElement(name);
}

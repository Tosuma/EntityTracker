using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

using EntityTracker.Domain;
using EntityTracker.Wpf.ViewModels;
using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;
    private readonly MouseWheelScrollRouter _mouseWheelRouter = new();
    private bool _updatingSelectors;
    private readonly AppUpdateService? _updates;
    private readonly DispatcherTimer _editTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private NotificationItem? _updateNotice;
    private NotificationItem? _offlineNotice;
    private bool _lastOverlayVisible;
    private bool _lastActionEnabled;

    public MainWindow(ShellViewModel viewModel, AppUpdateService? updates = null)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _updates = updates;
        DataContext = viewModel;
        Loaded += OnLoaded;
        Closed += OnClosed;
        PreviewMouseWheel += OnPreviewMouseWheel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        if (_updates is not null) _updates.StateChanged += OnUpdateStateChanged;
        _editTimer.Tick += (_, _) => RefreshUpdateBlock();
        ((System.Collections.Specialized.INotifyCollectionChanged)_viewModel.Notifications.Items)
            .CollectionChanged += OnNotificationsChanged;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        _mouseWheelRouter.Route(e);

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F &&
            (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
            UpdateBlock.Visibility != Visibility.Visible &&
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
        _editTimer.Start();
        _updates?.Start();
        RefreshUpdateBlock();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _editTimer.Stop();
        if (_updates is not null) _updates.StateChanged -= OnUpdateStateChanged;
        ((System.Collections.Specialized.INotifyCollectionChanged)_viewModel.Notifications.Items)
            .CollectionChanged -= OnNotificationsChanged;
        _viewModel.Dispose();
    }

    private void OnUpdateStateChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(RefreshUpdateBlock);
    }

    private void RefreshUpdateBlock()
    {
        if (_updates?.State == AppUpdateState.Offline && _offlineNotice is null)
        {
            _offlineNotice = _viewModel.Notifications.RequireAction(
                "Project sync paused",
                "EntityTracker could not check app releases. Local work is available; Project Git sync will resume after a successful check.",
                "Retry check", () => _updates.CheckAsync());
        }
        else if (_updates?.State != AppUpdateState.Offline && _offlineNotice is not null)
        {
            _viewModel.Notifications.Dismiss(_offlineNotice);
            _offlineNotice = null;
        }
        if (_updates?.IsRequired != true) return;
        bool editing = _viewModel.CurrentWorkspace?.HasUnsavedWork == true;
        bool catalogOpen = _viewModel.Catalog.IsOpen;
        bool operationRunning = _viewModel.HasActiveProjectSync || _viewModel.IsBusy ||
            _viewModel.CurrentWorkspace?.IsBusy == true;
        bool showOverlay = !editing && !catalogOpen;
        bool actionEnabled = showOverlay && !operationRunning;
        string message = editing ? "Finish or discard the current edit, then update EntityTracker." :
            catalogOpen ? "Finish or close the current dialog, then update EntityTracker." :
            operationRunning ? "Wait for the current operation to finish, then update EntityTracker." :
            $"{_updates.RequiredTag} is available. Close and update before continuing.";
        if (showOverlay)
        {
            ClearUpdateNotice();
        }
        else if (_updateNotice is null)
        {
            _updateNotice = _viewModel.Notifications.RequireAction(
                "App update required", message,
                "Close and update", RunUpdateAsync, canDismiss: false);
        }
        else if (_updateNotice.Message != message)
        {
            _viewModel.Notifications.NeedAction(_updateNotice, message,
                "Close and update", RunUpdateAsync);
        }
        if (_updateNotice is not null) _updateNotice.IsActionEnabled = false;
        UpdateBlock.Visibility = showOverlay ? Visibility.Visible : Visibility.Collapsed;
        UpdateSidebarBlock.Visibility = showOverlay ? Visibility.Visible : Visibility.Collapsed;
        UpdateActionButton.IsEnabled = actionEnabled;
        SetUnderlyingUiForUpdate(editing);
        UpdateBlockMessage.Text = operationRunning
            ? "Waiting for the current operation to finish. EntityTracker will then be ready to update."
            : $"{_updates.RequiredTag} is ready. EntityTracker must update before you continue working or sync Projects.";
        if (actionEnabled && (!_lastOverlayVisible || !_lastActionEnabled))
            UpdateActionButton.Focus();
        _lastOverlayVisible = showOverlay;
        _lastActionEnabled = actionEnabled;
    }

    private void OnUpdateClick(object sender, RoutedEventArgs e) => _ = RunUpdateAsync();

    private Task RunUpdateAsync()
    {
        if (_updates?.IsRequired != true || HasInProgressWork())
            return Task.CompletedTask;
        try
        {
            _updates.LaunchUpdater(Environment.ProcessId);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Update could not start",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        return Task.CompletedTask;
    }

    private bool HasInProgressWork() => _viewModel.IsBusy ||
        _viewModel.HasActiveProjectSync ||
        _viewModel.CurrentWorkspace?.IsBusy == true ||
        _viewModel.CurrentWorkspace?.HasUnsavedWork == true ||
        _viewModel.Catalog.IsOpen;

    private void SetUnderlyingUiForUpdate(bool allowWorkspaceEdit)
    {
        SidebarNavigation.IsEnabled = false;
        SidebarUtilities.IsEnabled = false;
        MainHeader.IsEnabled = false;
        DefaultPrompt.IsEnabled = false;
        WorkspaceArea.IsEnabled = allowWorkspaceEdit;
        BusyFooter.IsEnabled = false;
    }

    internal void ShowUpdatePreview(string tag)
    {
        ClearUpdateNotice();
        UpdateBlockMessage.Text = $"{tag} is ready. EntityTracker must update before you continue working or sync Projects.";
        UpdateBlock.Visibility = Visibility.Visible;
        UpdateSidebarBlock.Visibility = Visibility.Visible;
        SetUnderlyingUiForUpdate(false);
    }

    private void ClearUpdateNotice()
    {
        if (_updateNotice is null) return;
        _updateNotice.CanDismiss = true;
        _viewModel.Notifications.Dismiss(_updateNotice);
        _updateNotice = null;
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
        var transform = new TranslateTransform();
        card.RenderTransform = transform;
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

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

using EntityTracker.Domain;
using EntityTracker.Wpf.Controls;
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
    private DispatcherTimer? _closeAfterSync;
    private bool _lastOverlayVisible;
    private bool _lastActionEnabled;

    public MainWindow(ShellViewModel viewModel, AppUpdateService? updates = null)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _updates = updates;
        DataContext = viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
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

    /// <summary>
    /// Keeps the window open while a Project sync is running, so closing never cuts a sync between
    /// its commit and the moment it is recorded, and closes as soon as the sync has finished.
    /// </summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_viewModel.HasActiveProjectSync) return;
        e.Cancel = true;
        if (_closeAfterSync is not null) return;
        _viewModel.Notifications.BeginProgress("Closing EntityTracker",
            "Finishing the Project sync. EntityTracker closes when it is done.");
        _closeAfterSync = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background,
            (_, _) =>
            {
                if (_viewModel.HasActiveProjectSync) return;
                _closeAfterSync?.Stop();
                Close();
            }, Dispatcher);
        _closeAfterSync.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closeAfterSync?.Stop();
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

    private async Task RunUpdateAsync()
    {
        if (_updates?.IsRequired != true || HasInProgressWork())
            return;
        // Reading the updater from the new release can take a few seconds.
        UpdateActionButton.IsEnabled = false;
        try
        {
            await _updates.LaunchUpdaterAsync(Environment.ProcessId);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception error)
        {
            UpdateActionButton.IsEnabled = true;
            MessageBox.Show(this, error.Message, "Update could not start",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
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

    /// <summary>How long a leaving notice takes to slide out of the window to the left.</summary>
    internal static readonly TimeSpan NotificationSlideOut = TimeSpan.FromMilliseconds(300);

    /// <summary>How long the space a notice used takes to close once the card is gone.</summary>
    internal static readonly TimeSpan NotificationCollapse = TimeSpan.FromMilliseconds(350);

    /// <summary>How long a leaving notice stays in the list: the slide, then the collapse, plus a little slack.</summary>
    internal static TimeSpan NotificationExitDuration =>
        NotificationSlideOut + NotificationCollapse + TimeSpan.FromMilliseconds(50);

    /// <summary>Gets how far a card must move so that its right edge passes the window's left edge.</summary>
    internal static double SlideOutDistance(double rightEdgeInWindow) => -(Math.Max(rightEdgeInWindow, 0) + 8);

    /// <summary>
    /// Opens the new notice's space, then fades and slides the card up into it, and arranges for it
    /// to leave again when it starts closing. Runs regardless of the Windows animation setting, like
    /// the dependency graph's motion.
    /// </summary>
    private void OnNotificationLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border { Child: SmoothHeightDecorator { Child: Border card }, DataContext: NotificationItem item } container)
            return;
        PropertyChangedEventHandler onChanged = (_, args) =>
        {
            if (args.PropertyName == nameof(NotificationItem.IsClosing) && item.IsClosing)
                AnimateNotificationExit(container, card);
        };
        item.PropertyChanged += onChanged;
        RoutedEventHandler? onUnloaded = null;
        onUnloaded = (_, _) =>
        {
            item.PropertyChanged -= onChanged;
            container.Unloaded -= onUnloaded;
        };
        container.Unloaded += onUnloaded;

        Duration duration = TimeSpan.FromMilliseconds(220);
        CubicEase easeOut = new() { EasingMode = EasingMode.EaseOut };
        ScaleTransform scale = new(1, 0);
        container.LayoutTransform = scale;
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = easeOut });
        TranslateTransform slide = new(0, 12);
        card.RenderTransform = slide;
        slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, duration) { EasingFunction = easeOut });
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
    }

    /// <summary>
    /// Slides the card left until it is completely out of the window, and only then closes the space
    /// it used, so the cards below and the utilities glide down.
    /// </summary>
    private void AnimateNotificationExit(Border container, Border card)
    {
        if (card.RenderTransform is not TranslateTransform slide || slide.IsFrozen)
            card.RenderTransform = slide = new TranslateTransform();
        if (container.LayoutTransform is not ScaleTransform scale || scale.IsFrozen)
            container.LayoutTransform = scale = new ScaleTransform(1, 1);

        double rightEdge;
        try { rightEdge = card.TransformToAncestor(this).Transform(new Point(card.ActualWidth, 0)).X; }
        catch (InvalidOperationException) { rightEdge = card.ActualWidth + 20; }

        DoubleAnimation slideOut = new(slide.X + SlideOutDistance(rightEdge), NotificationSlideOut)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        slideOut.Completed += (_, _) => scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0, NotificationCollapse)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            });
        slide.BeginAnimation(TranslateTransform.XProperty, slideOut);
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

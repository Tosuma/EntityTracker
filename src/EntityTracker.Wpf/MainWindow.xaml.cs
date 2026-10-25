using System.Diagnostics;
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
using EntityTracker.Application.Diagnostics;
using Microsoft.Extensions.Logging;

namespace EntityTracker.Wpf;

public partial class MainWindow : Window
{
    private static readonly ILogger Logger = AppLogging.CreateLogger<MainWindow>();

    private readonly ShellViewModel _viewModel;
    private readonly MouseWheelScrollRouter _mouseWheelRouter = new();
    private bool _updatingSelectors;
    private readonly AppUpdateService? _updates;
    private readonly DispatcherTimer _editTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private NotificationItem? _updateNotice;
    private NotificationItem? _offlineNotice;
    // The available version already announced, so each new version is announced once.
    private string? _announcedUpdate;
    // The pending (available or required) version whose release notes What's new shows.
    private string? _notesFor;
    private bool _updating;
    private readonly CancellationTokenSource _closingSync = new();
    private NotificationItem? _closingNotice;
    private bool _closeWithoutSync;
    private bool _lastOverlayVisible;
    private bool _lastActionEnabled;

    public MainWindow(ShellViewModel viewModel, AppUpdateService? updates = null)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _updates = updates;
        DataContext = viewModel;
        Loaded += OnLoaded;
        SizeChanged += OnWindowSizeChanged;
        Closing += OnClosing;
        Closed += OnClosed;
        PreviewMouseWheel += OnPreviewMouseWheel;
        PreviewMouseDown += OnPreviewMouseDownLeaveTextField;
        TextFieldFocus.KeepInWindow(this);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        if (_updates is not null) _updates.StateChanged += OnUpdateStateChanged;
        _editTimer.Tick += (_, _) => RefreshUpdateBlock();
        ((System.Collections.Specialized.INotifyCollectionChanged)_viewModel.Notifications.Items)
            .CollectionChanged += OnNotificationsChanged;
    }

    /// <summary>
    /// Clicking outside a text field leaves it (see <see cref="TextFieldFocus"/>). The focus moves after the
    /// click, so a clicked button or list takes it first. Inside a dialog the focus is never cleared, so it
    /// cannot slip to the page behind.
    /// </summary>
    private void OnPreviewMouseDownLeaveTextField(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.FocusedElement is not DependencyObject focused ||
            e.OriginalSource is not DependencyObject source ||
            TextFieldFocus.FieldOf(focused) is not { } field ||
            TextFieldFocus.IsWithin(source, field))
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input,
            () => TextFieldFocus.Leave(field, source, mayClear: !_viewModel.IsModalOpen));
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        _mouseWheelRouter.Route(e);

    /// <summary>
    /// The Tracker page shortcuts: Ctrl+F search, Ctrl+1 to 3 for Overview, Dependency graph and Archived,
    /// Ctrl+N for a new entity, Ctrl+E for the export menu and F5 to refresh. None of them act while a
    /// dialog is open, the app is busy or an update is required. They work wherever the focus is, since
    /// the window keeps it when nothing else has it (<see cref="TextFieldFocus.KeepInWindow"/>).
    /// </summary>
    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (UpdateBlock.Visibility == Visibility.Visible || _viewModel.IsBusy || _viewModel.IsModalOpen ||
            !_viewModel.IsTrackerWorkspace || _viewModel.CurrentWorkspace is not { } workspace)
        {
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool control = Keyboard.Modifiers == ModifierKeys.Control;
        switch (key)
        {
            case Key.F when control:
                e.Handled = WorkspaceView.TryOpenCurrentSearch();
                break;
            case Key.D1 or Key.NumPad1 when control:
                e.Handled = Navigate(ShellDestination.Overview);
                break;
            case Key.D2 or Key.NumPad2 when control:
                e.Handled = Navigate(ShellDestination.DependencyGraph);
                break;
            case Key.D3 or Key.NumPad3 when control:
                e.Handled = Navigate(ShellDestination.Archived);
                break;
            case Key.N when control:
                e.Handled = Execute(workspace.OpenCreateEntityCommand);
                break;
            case Key.E when control:
                e.Handled = WorkspaceView.TryOpenExportMenu();
                break;
            case Key.F5 when Keyboard.Modifiers == ModifierKeys.None:
                e.Handled = Execute(workspace.RefreshCommand);
                break;
        }
    }

    private bool Navigate(ShellDestination destination)
    {
        _ = _viewModel.NavigateAsync(destination);
        return true;
    }

    private static bool Execute(ICommand command)
    {
        if (!command.CanExecute(null)) return false;
        command.Execute(null);
        return true;
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = Layout.IsCompactWidth(e.NewSize.Width);
        if (compact == Layout.GetIsCompact(this) && e.PreviousSize.Width > 0) return;
        Layout.SetIsCompact(this, compact);
        ArrangeHeader(compact);
    }

    /// <summary>
    /// Wide: the context, then the two selectors at a fixed width. Compact: the context on its own row and
    /// the selectors sharing the row below, so neither clips the context title.
    /// </summary>
    private void ArrangeHeader(bool compact)
    {
        ColumnDefinitionCollection columns = HeaderLayout.ColumnDefinitions;
        columns[0].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        columns[1].Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(280);
        columns[3].Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(280);
        Grid.SetColumnSpan(HeaderContext, compact ? 4 : 1);
        foreach (FrameworkElement selector in new FrameworkElement[] { ProjectSelectorPanel, TrackerSelectorPanel })
        {
            Grid.SetRow(selector, compact ? 1 : 0);
            selector.Margin = compact ? new Thickness(0, 8, 0, 0) : new Thickness(0);
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
        await ShowWhatsNewAfterUpdateAsync();
    }

    /// <summary>
    /// After an update, says once what changed since the version used before: a notice with What's new,
    /// and the list in Settings › About. A fresh install says nothing; otherwise About shows the installed
    /// version's own notes, unless a pending update's notes are already there.
    /// </summary>
    private async Task ShowWhatsNewAfterUpdateAsync()
    {
        if (_updates?.InstalledVersion is not { } installed) return;
        AppReleaseVersion? previous = _updates.TakePreviousRunVersion();
        if (previous is { } before && installed.CompareTo(before) > 0)
        {
            IReadOnlyList<ReleaseNote> notes = await _updates.ReadReleaseNotesAsync(before, installed, fetch: true);
            if (_notesFor is null) _viewModel.WhatsNew.ShowSince(before, notes);
            string version = WhatsNewViewModel.Short(installed);
            _viewModel.Notifications.Show($"Updated to EntityTracker {version}",
                notes.Count > 0 ? notes[0].Headline + "." : $"EntityTracker {version} is installed.",
                "What's new", _viewModel.OpenWhatsNewAsync);
            Logger.LogInformation("Updated from {Previous} to {Installed}; {Count} release notes shown.",
                before, installed, notes.Count);
            return;
        }

        IReadOnlyList<ReleaseNote> own = await _updates.ReadReleaseNotesAsync(null, installed, fetch: false);
        if (_notesFor is null) _viewModel.WhatsNew.ShowInstalled(installed, own);
    }

    /// <summary>
    /// Reads what a pending update brings, once per version, for What's new (Settings › About and the
    /// required-update screen). Returns the notes, or an empty list when they can't be read.
    /// </summary>
    private async Task<IReadOnlyList<ReleaseNote>> LoadComingNotesAsync(string tag)
    {
        if (_updates?.InstalledVersion is not { } installed || !AppReleaseVersion.TryParse(tag, out AppReleaseVersion coming))
            return [];
        _notesFor = tag;
        IReadOnlyList<ReleaseNote> notes = await _updates.ReadReleaseNotesAsync(installed, coming, fetch: true);
        if (_notesFor == tag) _viewModel.WhatsNew.ShowComing(coming, notes);
        return notes;
    }

    /// <summary>
    /// Syncs before closing (#109): the window waits for a running sync, so closing never cuts one
    /// between its commit and the moment it is recorded, then syncs the Projects with changes not synced
    /// yet, and closes. The closing notice offers to close without syncing. When a Project can't be
    /// synced, the window stays open and says why; closing again then closes without syncing.
    /// </summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // An update closes the app itself, and sync is paused until it is installed anyway.
        if (_closeWithoutSync || _updates?.IsRequired == true) return;
        e.Cancel = true;
        if (_closingNotice is null) _ = SyncThenCloseAsync();
    }

    private async Task SyncThenCloseAsync()
    {
        NotificationItem notice = _closingNotice = _viewModel.Notifications.BeginProgress("Closing EntityTracker",
            "Checking for changes that are not synced yet…");
        _viewModel.Notifications.SetAction(notice, "Close without syncing", () =>
        {
            Logger.LogInformation("Closing without syncing, as chosen in the closing notice.");
            CloseWithoutSync();
            return Task.CompletedTask;
        });
        try
        {
            if (_viewModel.HasActiveProjectSync)
            {
                _viewModel.Notifications.Progress(notice, "Finishing the Project sync. EntityTracker closes when it is done.");
                while (_viewModel.HasActiveProjectSync)
                    await Task.Delay(TimeSpan.FromMilliseconds(250), _closingSync.Token);
            }

            if (await _viewModel.HasUnsyncedProjectChangesAsync(_closingSync.Token))
            {
                _viewModel.Notifications.Progress(notice, "Syncing your changes before closing…");
                Logger.LogInformation("Closing: syncing the Projects with changes not synced yet.");
                Stopwatch closingSync = Stopwatch.StartNew();
                string? problem = await _viewModel.SyncBeforeClosingAsync(_closingSync.Token);
                Logger.LogInformation("Closing: the sync finished in {ElapsedMs} ms{Result}.", closingSync.ElapsedMilliseconds,
                    problem is null ? "; everything is synced" : "; not everything could be synced");
                if (problem is not null)
                {
                    Logger.LogWarning("EntityTracker stayed open: {Problem}", problem);
                    _closeWithoutSync = true;
                    _viewModel.Notifications.Complete(notice,
                        $"{problem} EntityTracker stayed open; close it again to close without syncing.",
                        NotificationKind.Failure);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (_closingSync.IsCancellationRequested)
        {
            // Not logged: Close without syncing was chosen, and the window is already closing.
            return;
        }
        catch (Exception error)
        {
            Logger.LogError(error, "Syncing before closing failed.");
            _closeWithoutSync = true;
            _viewModel.Notifications.Complete(notice,
                "Your changes could not be synced before closing. EntityTracker stayed open; close it again to close without syncing.",
                NotificationKind.Failure);
            return;
        }
        finally
        {
            _closingNotice = null;
        }

        CloseWithoutSync();
    }

    private void CloseWithoutSync()
    {
        _closeWithoutSync = true;
        _closingSync.Cancel();
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closingSync.Cancel();
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
                "EntityTracker could not check app releases. Local work is available; Project Git sync will resume after a successful check." +
                (_updates.OfflineReason is { } reason ? " " + reason : ""),
                "Retry check", () => _updates.CheckAsync());
        }
        else if (_updates?.State != AppUpdateState.Offline && _offlineNotice is not null)
        {
            _viewModel.Notifications.Dismiss(_offlineNotice);
            _offlineNotice = null;
        }
        RefreshAvailableUpdate();
        if (_updates?.IsRequired != true) return;
        if (_updates.RequiredTag is { } required && required != _notesFor) _ = LoadComingNotesAsync(required);
        bool editing = _viewModel.CurrentWorkspace?.HasUnsavedWork == true;
        bool catalogOpen = _viewModel.IsModalOpen;
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

    /// <summary>
    /// Offers a newer minor or patch version: one notice per new version, with Update, and the
    /// Settings dot and Settings › About's Update button until it is installed.
    /// </summary>
    private void RefreshAvailableUpdate()
    {
        // A source build has no installed version to update.
        if (_updates is null) return;
        string? tag = _updates.AvailableTag;
        string? version = tag is not null && AppReleaseVersion.TryParse(tag, out AppReleaseVersion release)
            ? $"{release.Major}.{release.Minor}.{release.Patch}"
            : null;
        _viewModel.OfferUpdate(version, () => InstallAvailableUpdateAsync("Settings › About"));
        if (version is null || tag is null || tag == _announcedUpdate) return;
        _announcedUpdate = tag;
        _ = AnnounceAvailableUpdateAsync(tag, version);
    }

    /// <summary>The "Update available" notice, with the newest version's headline once its notes are read.</summary>
    private async Task AnnounceAvailableUpdateAsync(string tag, string version)
    {
        IReadOnlyList<ReleaseNote> notes = await LoadComingNotesAsync(tag);
        if (_updates?.AvailableTag != tag) return;
        string message = notes.Count > 0
            ? $"EntityTracker {version} is available: {notes[0].Headline}. Update now, or see what's new in Settings › About."
            : $"EntityTracker {version} is available. Update now, or later from Settings › About.";
        _viewModel.Notifications.Show("Update available", message,
            "Update", () => InstallAvailableUpdateAsync("the notification"));
    }

    /// <summary>
    /// Installs the offered version: once nothing is running or unsaved, syncs the changes not synced
    /// yet (as closing does), then starts the updater and closes.
    /// </summary>
    private async Task InstallAvailableUpdateAsync(string from)
    {
        if (_updating || _updates?.AvailableTag is not { } tag) return;
        if (HasInProgressWork())
        {
            Logger.LogInformation("The update to {Tag} was not started from {From}: work is in progress.", tag, from);
            _viewModel.Notifications.Show("Update not started",
                "Finish or close the current edit, dialog or sync, then update.", NotificationKind.Failure);
            return;
        }
        _updating = true;
        Logger.LogInformation("Update to {Tag} started from {From}.", tag, from);
        NotificationItem notice = _viewModel.Notifications.BeginProgress("Updating EntityTracker",
            "Syncing your changes before updating…");
        try
        {
            if (await _viewModel.HasUnsyncedProjectChangesAsync() &&
                await _viewModel.SyncBeforeClosingAsync() is { } problem)
            {
                Logger.LogWarning("The update to {Tag} waits: {Problem}", tag, problem);
                _viewModel.Notifications.Complete(notice, $"{problem} Update when it is synced.", NotificationKind.Failure);
                _updating = false;
                return;
            }
            _viewModel.Notifications.Progress(notice, "Starting the updater…");
            await _updates.LaunchUpdaterAsync(Environment.ProcessId);
            _closeWithoutSync = true;
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception error)
        {
            Logger.LogError(error, "The updater could not be started.");
            _viewModel.Notifications.Complete(notice, $"The update could not start: {error.Message}", NotificationKind.Failure);
            _updating = false;
        }
    }

    private void OnUpdateClick(object sender, RoutedEventArgs e) => _ = RunUpdateAsync();

    private async Task RunUpdateAsync()
    {
        if (_updates?.IsRequired != true || HasInProgressWork())
            return;
        Logger.LogInformation("Required update to {Tag} started.", _updates.RequiredTag);
        // Reading the updater from the new release can take a few seconds.
        UpdateActionButton.IsEnabled = false;
        try
        {
            await _updates.LaunchUpdaterAsync(Environment.ProcessId);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception error)
        {
            Logger.LogError(error, "The updater could not be started.");
            UpdateActionButton.IsEnabled = true;
            MessageBox.Show(this, error.Message, "Update could not start",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool HasInProgressWork() => _viewModel.IsBusy ||
        _viewModel.HasActiveProjectSync ||
        _viewModel.CurrentWorkspace?.IsBusy == true ||
        _viewModel.CurrentWorkspace?.HasUnsavedWork == true ||
        _viewModel.IsModalOpen;

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
        catch (InvalidOperationException exception)
        {
            // Cosmetic: a notification that already left the window slides out by its own width.
            Logger.LogDebug(exception, "A closing notification was no longer in the window.");
            rightEdge = card.ActualWidth + 20;
        }

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

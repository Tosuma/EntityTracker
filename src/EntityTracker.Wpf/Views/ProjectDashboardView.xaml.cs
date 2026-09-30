using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

using EntityTracker.Application.Projects;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Views;

public partial class ProjectDashboardView : UserControl
{
    private ShellViewModel? _attachedShell;
    private ProjectDashboardViewModel? _attachedDashboard;
    private ToggleButton? _openTrackerActionsToggle;
    private Window? _hostWindow;

    public ProjectDashboardView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
        DataContextChanged += OnDataContextChanged;
    }

    private ShellViewModel Shell => (ShellViewModel)DataContext;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hostWindow = Window.GetWindow(this);
        if (_hostWindow is not null)
        {
            _hostWindow.PreviewMouseDown += OnPreviewMouseDown;
            _hostWindow.StateChanged += OnHostWindowStateChanged;
        }
        AttachToContext();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_openTrackerActionsToggle is not null) _openTrackerActionsToggle.IsChecked = false;
        if (_hostWindow is not null)
        {
            _hostWindow.PreviewMouseDown -= OnPreviewMouseDown;
            _hostWindow.StateChanged -= OnHostWindowStateChanged;
            _hostWindow = null;
        }
        DetachFromContext();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible && _openTrackerActionsToggle is not null)
            _openTrackerActionsToggle.IsChecked = false;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded)
        {
            AttachToContext();
        }
    }

    private void AttachToContext()
    {
        DetachFromContext();
        _attachedShell = DataContext as ShellViewModel;
        if (_attachedShell is not null)
        {
            _attachedShell.PropertyChanged += OnShellPropertyChanged;
            AttachDashboard(_attachedShell.ProjectReporting);
        }

        RebuildComparisonColumns();
    }

    private void DetachFromContext()
    {
        if (_attachedShell is not null)
        {
            _attachedShell.PropertyChanged -= OnShellPropertyChanged;
            _attachedShell = null;
        }

        AttachDashboard(null);
    }

    private void AttachDashboard(ProjectDashboardViewModel? dashboard)
    {
        if (_attachedDashboard is not null)
        {
            _attachedDashboard.PropertyChanged -= OnDashboardPropertyChanged;
        }

        _attachedDashboard = dashboard;
        if (_attachedDashboard is not null)
        {
            _attachedDashboard.PropertyChanged += OnDashboardPropertyChanged;
        }
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.ProjectReporting))
        {
            AttachDashboard(_attachedShell?.ProjectReporting);
            RebuildComparisonColumns();
        }
    }

    private void OnDashboardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProjectDashboardViewModel.Comparison) or
            nameof(ProjectDashboardViewModel.ComparisonRows))
        {
            RebuildComparisonColumns();
        }
    }

    private void RebuildComparisonColumns()
    {
        while (ComparisonGrid.Columns.Count > 1)
        {
            ComparisonGrid.Columns.RemoveAt(ComparisonGrid.Columns.Count - 1);
        }

        if (_attachedDashboard?.Comparison is not { } comparison)
        {
            return;
        }

        DataTemplate comparisonCellTemplate =
            (DataTemplate)FindResource("ComparisonCellTemplate");
        for (int index = 0; index < comparison.Trackers.Count; index++)
        {
            FrameworkElementFactory content = new(typeof(ContentControl));
            content.SetBinding(
                ContentControl.ContentProperty,
                new Binding($"Cells[{index}]"));
            content.SetValue(ContentControl.ContentTemplateProperty, comparisonCellTemplate);
            ComparisonGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = comparison.Trackers[index].Name,
                CellTemplate = new DataTemplate { VisualTree = content },
                MinWidth = 170,
                Width = new DataGridLength(190)
            });
        }
    }

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
        if (sender is FrameworkElement { DataContext: TrackerDashboardSummary item })
            await Shell.OpenTrackerAsync(item.TrackerId);
    }

    private void OnRenameTracker(object sender, RoutedEventArgs e)
    {
        var tracker = sender is FrameworkElement { DataContext: TrackerDashboardSummary item }
            ? Shell.Trackers.FirstOrDefault(candidate => candidate.Id == item.TrackerId)
            : null;
        CloseTrackerActions(sender);
        if (tracker is not null) Shell.Catalog.OpenRenameTracker(tracker);
    }

    private async void OnSyncTracker(object sender, RoutedEventArgs e)
    {
        var tracker = sender is FrameworkElement { DataContext: TrackerDashboardSummary item }
            ? Shell.Trackers.FirstOrDefault(candidate => candidate.Id == item.TrackerId)
            : null;
        CloseTrackerActions(sender);
        if (tracker is not null) await Shell.Catalog.OpenSyncTrackerAsync(tracker);
    }

    private void OnRecycleTracker(object sender, RoutedEventArgs e)
    {
        var tracker = sender is FrameworkElement { DataContext: TrackerDashboardSummary item }
            ? Shell.Trackers.FirstOrDefault(candidate => candidate.Id == item.TrackerId)
            : null;
        CloseTrackerActions(sender);
        if (tracker is not null) Shell.Catalog.RequestRecycle(tracker);
    }

    private static void CloseTrackerActions(object sender)
    {
        if (sender is FrameworkElement { Tag: ToggleButton toggle }) toggle.IsChecked = false;
    }

    private void OnTrackerActionsChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton toggle) return;
        if (_openTrackerActionsToggle is not null && _openTrackerActionsToggle != toggle)
            _openTrackerActionsToggle.IsChecked = false;
        _openTrackerActionsToggle = toggle;
        TrackerActionsMenu.DataContext = toggle.DataContext;
        TrackerActionsMenu.Visibility = Visibility.Visible;
        TrackerActionsMenu.Tag = toggle;
        RenameTrackerAction.Tag = toggle;
        SyncTrackerAction.Tag = toggle;
        RecycleTrackerAction.Tag = toggle;
        PositionTrackerActionsMenu();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (ReferenceEquals(_openTrackerActionsToggle, toggle)) RenameTrackerAction.Focus();
        }));
    }

    private void OnTrackerActionsUnchecked(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(sender, _openTrackerActionsToggle)) return;
        _openTrackerActionsToggle = null;
        TrackerActionsMenu.Visibility = Visibility.Collapsed;
        TrackerActionsMenu.DataContext = null;
        TrackerActionsMenu.Tag = null;
        RenameTrackerAction.Tag = null;
        SyncTrackerAction.Tag = null;
        RecycleTrackerAction.Tag = null;
    }

    private void PositionTrackerActionsMenu()
    {
        if (_openTrackerActionsToggle is not { IsLoaded: true } toggle) return;
        TrackerActionsMenu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Point anchorTopLeft = toggle.TranslatePoint(new Point(), TrackerActionLayer);
        CardActionMenuPosition position = CardActionMenuPlacement.Calculate(
            new Rect(anchorTopLeft, new Size(toggle.ActualWidth, toggle.ActualHeight)),
            TrackerActionsMenu.DesiredSize,
            new Size(TrackerActionLayer.ActualWidth, TrackerActionLayer.ActualHeight));
        Canvas.SetLeft(TrackerActionsMenu, position.Left);
        Canvas.SetTop(TrackerActionsMenu, position.Top);
        TrackerActionsMenu.CornerRadius = position.OpensAbove
            ? new CornerRadius(6, 6, 0, 0)
            : new CornerRadius(0, 0, 6, 6);
    }

    private void OnTrackerActionsPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || sender is not FrameworkElement { Tag: ToggleButton toggle }) return;
        toggle.IsChecked = false;
        toggle.Focus();
        e.Handled = true;
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_openTrackerActionsToggle is not { } toggle) return;
        DependencyObject? target = e.OriginalSource as DependencyObject;
        if (IsWithin(target, toggle) ||
            IsWithin(target, TrackerActionsMenu)) return;
        toggle.IsChecked = false;
    }

    private void OnTrackerActionLayerSizeChanged(object sender, SizeChangedEventArgs e) =>
        PositionTrackerActionsMenu();

    private void OnHostWindowStateChanged(object? sender, EventArgs e)
    {
        if (_hostWindow?.WindowState == WindowState.Minimized &&
            _openTrackerActionsToggle is not null)
            _openTrackerActionsToggle.IsChecked = false;
    }

    private void OnDashboardScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if ((e.VerticalChange != 0 || e.HorizontalChange != 0) &&
            _openTrackerActionsToggle is not null)
            _openTrackerActionsToggle.IsChecked = false;
    }

    private static bool IsWithin(DependencyObject? target, DependencyObject ancestor)
    {
        while (target is not null)
        {
            if (ReferenceEquals(target, ancestor)) return true;
            target = target is Visual
                ? VisualTreeHelper.GetParent(target)
                : LogicalTreeHelper.GetParent(target);
        }
        return false;
    }

    private async void OnOpenComparisonCell(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProjectComparisonDisplayCell cell })
        {
            await Shell.OpenComparisonCellAsync(cell.Model);
        }
    }
}

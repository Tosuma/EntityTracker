using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

using EntityTracker.Application.Projects;
using EntityTracker.Application.GitSync;
using EntityTracker.Domain;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Views;

public partial class PortfolioView : UserControl
{
    private ToggleButton? _openProjectActionsToggle;
    private Window? _hostWindow;

    public PortfolioView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private ShellViewModel Shell => (ShellViewModel)DataContext;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hostWindow = Window.GetWindow(this);
        if (_hostWindow is null) return;
        _hostWindow.PreviewMouseDown += OnPreviewMouseDown;
        _hostWindow.StateChanged += OnHostWindowStateChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_openProjectActionsToggle is not null) _openProjectActionsToggle.IsChecked = false;
        if (_hostWindow is null) return;
        _hostWindow.PreviewMouseDown -= OnPreviewMouseDown;
        _hostWindow.StateChanged -= OnHostWindowStateChanged;
        _hostWindow = null;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible && _openProjectActionsToggle is not null)
            _openProjectActionsToggle.IsChecked = false;
    }

    private void OnCreateProject(object sender, RoutedEventArgs e) => Shell.Catalog.OpenCreateProject();

    private async void OnImportProject(object sender, RoutedEventArgs e)
    {
        string? path = new ProjectRepositoryFolderPicker().Pick();
        if (path is null) return;
        try { await Shell.ImportProjectAsync(path); }
        catch (ProjectNameCollisionException collision)
        {
            string? name = ProjectLocalNameDialog.Prompt(Window.GetWindow(this), collision.ConflictingName);
            if (name is null) return;
            try { await Shell.ImportProjectAsync(path, name); }
            catch (Exception error) { Shell.ShowNotification(error.Message); }
        }
        catch (Exception error) { Shell.ShowNotification(error.Message); }
    }

    private async void OnOpenRecycleBins(object sender, RoutedEventArgs e) =>
        await Shell.Catalog.OpenRecycleBinAsync();

    private async void OnPublishDeletion(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProjectSyncLink link })
            await Shell.PublishPendingDeletionAsync(link);
    }

    private async void OnOpenProject(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProjectPortfolioSummary item })
        {
            await Shell.OpenProjectAsync(item.ProjectId);
        }
    }

    private void OnRenameProject(object sender, RoutedEventArgs e)
    {
        Project? project = sender is FrameworkElement { DataContext: ProjectPortfolioSummary item }
            ? Shell.Projects.FirstOrDefault(candidate => candidate.Id == item.ProjectId)
            : null;
        CloseProjectActions(sender);
        if (project is not null) Shell.Catalog.OpenRenameProject(project);
    }

    private void OnRecycleProject(object sender, RoutedEventArgs e)
    {
        Project? project = sender is FrameworkElement { DataContext: ProjectPortfolioSummary item }
            ? Shell.Projects.FirstOrDefault(candidate => candidate.Id == item.ProjectId)
            : null;
        CloseProjectActions(sender);
        if (project is not null) Shell.Catalog.RequestRecycle(project);
    }

    private static void CloseProjectActions(object sender)
    {
        if (sender is FrameworkElement { Tag: ToggleButton toggle }) toggle.IsChecked = false;
    }

    private void OnProjectActionsChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton toggle) return;
        if (_openProjectActionsToggle is not null && _openProjectActionsToggle != toggle)
            _openProjectActionsToggle.IsChecked = false;
        _openProjectActionsToggle = toggle;
        ProjectActionsMenu.DataContext = toggle.DataContext;
        ProjectActionsMenu.Visibility = Visibility.Visible;
        ProjectActionsMenu.Tag = toggle;
        RenameProjectAction.Tag = toggle;
        RecycleProjectAction.Tag = toggle;
        PositionProjectActionsMenu();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (ReferenceEquals(_openProjectActionsToggle, toggle)) RenameProjectAction.Focus();
        }));
    }

    private void OnProjectActionsUnchecked(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(sender, _openProjectActionsToggle)) return;
        _openProjectActionsToggle = null;
        ProjectActionsMenu.Visibility = Visibility.Collapsed;
        ProjectActionsMenu.DataContext = null;
        ProjectActionsMenu.Tag = null;
        RenameProjectAction.Tag = null;
        RecycleProjectAction.Tag = null;
    }

    private void PositionProjectActionsMenu()
    {
        if (_openProjectActionsToggle is not { IsLoaded: true } toggle) return;
        ProjectActionsMenu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Point anchorTopLeft = toggle.TranslatePoint(new Point(), ProjectActionLayer);
        CardActionMenuPosition position = CardActionMenuPlacement.Calculate(
            new Rect(anchorTopLeft, new Size(toggle.ActualWidth, toggle.ActualHeight)),
            ProjectActionsMenu.DesiredSize,
            new Size(ProjectActionLayer.ActualWidth, ProjectActionLayer.ActualHeight));
        Canvas.SetLeft(ProjectActionsMenu, position.Left);
        Canvas.SetTop(ProjectActionsMenu, position.Top);
        ProjectActionsMenu.CornerRadius = position.OpensAbove
            ? new CornerRadius(6, 6, 0, 0)
            : new CornerRadius(0, 0, 6, 6);
    }

    private void OnProjectActionsPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || sender is not FrameworkElement { Tag: ToggleButton toggle }) return;
        toggle.IsChecked = false;
        toggle.Focus();
        e.Handled = true;
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_openProjectActionsToggle is not { } toggle) return;
        DependencyObject? target = e.OriginalSource as DependencyObject;
        if (IsWithin(target, toggle) || IsWithin(target, ProjectActionsMenu)) return;
        toggle.IsChecked = false;
    }

    private void OnProjectActionLayerSizeChanged(object sender, SizeChangedEventArgs e) =>
        PositionProjectActionsMenu();

    private void OnHostWindowStateChanged(object? sender, EventArgs e)
    {
        if (_hostWindow?.WindowState == WindowState.Minimized &&
            _openProjectActionsToggle is not null)
            _openProjectActionsToggle.IsChecked = false;
    }

    private void OnPortfolioScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if ((e.VerticalChange != 0 || e.HorizontalChange != 0) &&
            _openProjectActionsToggle is not null)
            _openProjectActionsToggle.IsChecked = false;
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
}

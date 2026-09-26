using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

using EntityTracker.Application.Projects;
using EntityTracker.Wpf.ViewModels;
using Microsoft.Win32;

namespace EntityTracker.Wpf.Views;

public partial class ProjectDashboardView : UserControl
{
    private ShellViewModel? _attachedShell;
    private ProjectDashboardViewModel? _attachedDashboard;

    public ProjectDashboardView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private ShellViewModel Shell => (ShellViewModel)DataContext;

    private void OnLoaded(object sender, RoutedEventArgs e) => AttachToContext();

    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachFromContext();

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

    private async void OnLinkRepository(object sender, RoutedEventArgs e)
    {
        string? path = SelectRepositoryFolder("Link an existing empty Git repository");
        if (path is not null) await Shell.LinkSelectedProjectAsync(path);
    }

    private async void OnLocateRepository(object sender, RoutedEventArgs e)
    {
        string? path = SelectRepositoryFolder("Locate the registered EntityTracker repository");
        if (path is not null) await Shell.LocateSelectedRepositoryAsync(path);
    }

    private async void OnRebuildRepositoryCache(object sender, RoutedEventArgs e) =>
        await Shell.RebuildSelectedRepositoryCacheAsync();

    private async void OnSyncRepository(object sender, RoutedEventArgs e)
    {
        await Shell.SyncSelectedProjectAsync();
        if (sender is Button { IsVisible: true, IsEnabled: true } button)
            button.Focus();
    }

    private string? SelectRepositoryFolder(string title)
    {
        OpenFolderDialog dialog = new() { Title = title, Multiselect = false };
        return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FolderName : null;
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

    private async void OnOpenComparisonCell(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProjectComparisonDisplayCell cell)
        {
            await Shell.OpenComparisonCellAsync(cell.Model);
        }
    }
}

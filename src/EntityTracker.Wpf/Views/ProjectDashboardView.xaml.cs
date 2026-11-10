using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

using EntityTracker.Application.Projects;
using EntityTracker.Wpf.Controls;
using EntityTracker.Wpf.ViewModels;

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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachToContext();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachFromContext();
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
                MinWidth = 210,
                Width = new DataGridLength(230)
            });
        }
    }

    private void OnCreateTracker(object sender, RoutedEventArgs e)
    {
        if (Shell.SelectedProject is not null) Shell.Catalog.OpenCreateTracker(Shell.SelectedProject);
    }

    private void OnProjectActionsClick(object sender, RoutedEventArgs e) => ActionMenu.Open(sender);

    private void OnTrackerActionsClick(object sender, RoutedEventArgs e) => ActionMenu.Open(sender);

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
        if (tracker is not null) Shell.Catalog.OpenRenameTracker(tracker);
    }

    private async void OnConvertTracker(object sender, RoutedEventArgs e)
    {
        var tracker = sender is FrameworkElement { DataContext: TrackerDashboardSummary item }
            ? Shell.Trackers.FirstOrDefault(candidate => candidate.Id == item.TrackerId)
            : null;
        if (tracker is not null) await Shell.Catalog.OpenConvertToSchemaAsync(tracker);
    }

    private void OnDetachTracker(object sender, RoutedEventArgs e)
    {
        var item = (sender as FrameworkElement)?.DataContext as TrackerDashboardSummary;
        var tracker = item is null ? null : Shell.Trackers.FirstOrDefault(candidate => candidate.Id == item.TrackerId);
        if (tracker is not null && item?.SchemaName is { } schemaName) Shell.Catalog.RequestDetach(tracker, schemaName);
    }

    private void OnRecycleTracker(object sender, RoutedEventArgs e)
    {
        var tracker = sender is FrameworkElement { DataContext: TrackerDashboardSummary item }
            ? Shell.Trackers.FirstOrDefault(candidate => candidate.Id == item.TrackerId)
            : null;
        if (tracker is not null) Shell.Catalog.RequestRecycle(tracker);
    }

    private async void OnOpenComparisonCell(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProjectComparisonDisplayCell cell })
        {
            await Shell.OpenComparisonCellAsync(cell.Model);
        }
    }
}

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Views;

public partial class TrackerWorkspaceView : UserControl
{
    private const double MouseWheelDeltaPerNotch = 120;
    private const double ScrollPixelsPerNotch = 10;

    private MainWindowViewModel? _viewModel;
    private IInputElement? _focusBeforeEditor;

    public TrackerWorkspaceView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Attach(DataContext as MainWindowViewModel);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Detach();
    }

    private void OnDataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        Detach();
        Attach(e.NewValue as MainWindowViewModel);
    }

    private void Attach(MainWindowViewModel? viewModel)
    {
        if (viewModel is null || ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        _viewModel = viewModel;
        viewModel.OverviewSelectionClearRequested += OnOverviewSelectionClearRequested;
        viewModel.Editor.PropertyChanged += OnEditorPropertyChanged;
    }

    private void Detach()
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.OverviewSelectionClearRequested -= OnOverviewSelectionClearRequested;
        _viewModel.Editor.PropertyChanged -= OnEditorPropertyChanged;
        _viewModel = null;
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EntityDependencyEditorViewModel.IsOpen))
        {
            return;
        }

        if (_viewModel?.Editor.IsOpen == true)
        {
            _focusBeforeEditor = Keyboard.FocusedElement;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!EditorStatusComboBox.Focus())
                {
                    EditorSurface.Focus();
                }
            }));
            return;
        }

        IInputElement? restoreTarget = _focusBeforeEditor;
        _focusBeforeEditor = null;
        if (restoreTarget is not null)
        {
            Dispatcher.BeginInvoke(new Action(() => Keyboard.Focus(restoreTarget)));
        }
    }

    private void OnOverviewSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _viewModel?.UpdateOverviewSelection(
            OverviewDataGrid.SelectedItems.OfType<EntityOverviewRow>());

    private void OnOverviewSelectionClearRequested(object? sender, EventArgs e)
    {
        if (OverviewDataGrid.SelectedItems.Count > 0)
        {
            OverviewDataGrid.UnselectAll();
        }
    }

    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel is null ||
            _viewModel.SelectedTab != MainWindowTab.Overview ||
            OverviewDataGrid.SelectedItems.Count == 0 ||
            _viewModel.Editor.IsOpen ||
            e.OriginalSource is not DependencyObject source ||
            !IsDescendantOrSelf(source, this) ||
            IsDescendantOrSelf(source, OverviewDataGrid) ||
            IsDescendantOrSelf(source, BulkStatusToolbar))
        {
            return;
        }

        _viewModel.ClearOverviewSelection();
    }

    private void OnContextMenuButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: not null } button)
        {
            return;
        }

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
        e.Handled = true;
    }

    private void OnOpenOverviewSearchClick(object sender, RoutedEventArgs e) =>
        QueueCurrentSearchFocus();

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (e.Key == Key.F &&
            (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            EntityTableViewModel? table = GetCurrentEntityTable();
            if (table is not null &&
                !_viewModel.IsBusy &&
                !_viewModel.ManualCreation.IsBusy &&
                !_viewModel.Editor.IsOpen)
            {
                table.OpenSearchCommand.Execute(null);
                QueueCurrentSearchFocus();
                e.Handled = true;
            }

            return;
        }

        if (e.Key != Key.Escape || _viewModel.Editor.IsOpen)
        {
            return;
        }

        EntityTableViewModel? currentTable = GetCurrentEntityTable();
        if (currentTable?.CloseOpenFilter() == true)
        {
            e.Handled = true;
            return;
        }

        if (currentTable?.IsSearchOpen == true)
        {
            currentTable.CloseSearchCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (_viewModel.SelectedTab == MainWindowTab.Overview &&
            OverviewDataGrid.SelectedItems.Count > 0)
        {
            _viewModel.ClearOverviewSelection();
            e.Handled = true;
        }
    }

    private EntityTableViewModel? GetCurrentEntityTable() => _viewModel?.SelectedTab switch
    {
        MainWindowTab.Overview => _viewModel.ActiveTable,
        MainWindowTab.Archived => _viewModel.ArchivedTable,
        _ => null
    };

    private void QueueCurrentSearchFocus()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_viewModel is null)
            {
                return;
            }

            EntityTableViewModel? table = GetCurrentEntityTable();
            if (table?.IsSearchOpen != true)
            {
                return;
            }

            TextBox searchBox = _viewModel.SelectedTab == MainWindowTab.Archived
                ? ArchivedSearchTextBox
                : OverviewSearchTextBox;
            searchBox.Focus();
            searchBox.SelectAll();
        }));
    }

    public FrameworkElement? FindWorkspaceElement(string name) =>
        FindName(name) as FrameworkElement;

    private void OnDataGridPreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        if (sender is not DataGrid dataGrid || e.Delta == 0)
        {
            return;
        }

        ScrollViewer? scrollViewer = FindVisualDescendant<ScrollViewer>(dataGrid);
        if (scrollViewer is null || scrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        if (TryScroll(scrollViewer, e.Delta))
        {
            e.Handled = true;
        }
    }

    private void OnReviewPreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer reviewScrollViewer ||
            e.OriginalSource is not DependencyObject originalSource ||
            e.Delta == 0)
        {
            return;
        }

        DependencyObject? current = originalSource;
        while (current is not null)
        {
            if (current is ScrollViewer scrollViewer && TryScroll(scrollViewer, e.Delta))
            {
                e.Handled = true;
                return;
            }

            if (ReferenceEquals(current, reviewScrollViewer))
            {
                return;
            }

            current = GetParent(current);
        }
    }

    private static bool TryScroll(ScrollViewer scrollViewer, int wheelDelta)
    {
        if (scrollViewer.ScrollableHeight <= 0)
        {
            return false;
        }

        double scrollDelta = wheelDelta / MouseWheelDeltaPerNotch * ScrollPixelsPerNotch;
        double targetOffset = Math.Clamp(
            scrollViewer.VerticalOffset - scrollDelta,
            0,
            scrollViewer.ScrollableHeight);

        if (targetOffset == scrollViewer.VerticalOffset)
        {
            return false;
        }

        scrollViewer.ScrollToVerticalOffset(targetOffset);
        return true;
    }

    private static DependencyObject? GetParent(DependencyObject child)
    {
        if (child is FrameworkContentElement frameworkContentElement)
        {
            return frameworkContentElement.Parent;
        }

        if (child is ContentElement contentElement)
        {
            return ContentOperations.GetParent(contentElement);
        }

        return child is Visual or Visual3D
            ? VisualTreeHelper.GetParent(child)
            : LogicalTreeHelper.GetParent(child);
    }

    private static bool IsDescendantOrSelf(
        DependencyObject source,
        DependencyObject ancestor)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            current = GetParent(current);
        }

        return false;
    }

    private static T? FindVisualDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            T? descendant = FindVisualDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }
}

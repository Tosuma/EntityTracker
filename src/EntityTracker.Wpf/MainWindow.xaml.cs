using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf;

public partial class MainWindow : Window
{
    private const double MouseWheelDeltaPerNotch = 120;
    private const double ScrollPixelsPerNotch = 10;

    private readonly MainWindowViewModel _viewModel;

    public MainWindow(MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.OverviewSelectionClearRequested += OnOverviewSelectionClearRequested;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.InitializeAsync();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        _viewModel.OverviewSelectionClearRequested -= OnOverviewSelectionClearRequested;
    }

    private void OnOverviewSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _viewModel.UpdateOverviewSelection(
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
        if (_viewModel.SelectedTabIndex != 0 ||
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
        QueueOverviewSearchFocus();

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F &&
            (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (_viewModel.OpenOverviewSearchCommand.CanExecute(null))
            {
                _viewModel.OpenOverviewSearchCommand.Execute(null);
                QueueOverviewSearchFocus();
                e.Handled = true;
            }

            return;
        }

        if (e.Key != Key.Escape ||
            _viewModel.SelectedTabIndex != 0 ||
            _viewModel.Editor.IsOpen)
        {
            return;
        }

        if (_viewModel.CloseOverviewSearchCommand.CanExecute(null))
        {
            _viewModel.CloseOverviewSearchCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (OverviewDataGrid.SelectedItems.Count > 0)
        {
            _viewModel.ClearOverviewSelection();
            e.Handled = true;
        }
    }

    private void QueueOverviewSearchFocus()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_viewModel.IsOverviewSearchOpen)
            {
                return;
            }

            OverviewSearchTextBox.Focus();
            OverviewSearchTextBox.SelectAll();
        }));
    }

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

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf;

public partial class MainWindow : Window
{
    private const double MouseWheelDeltaPerNotch = 120;
    private const double GridScrollPixelsPerNotch = 10;

    private readonly MainWindowViewModel _viewModel;

    public MainWindow(MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.InitializeAsync();
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

        double scrollDelta = e.Delta / MouseWheelDeltaPerNotch * GridScrollPixelsPerNotch;
        double targetOffset = Math.Clamp(
            scrollViewer.VerticalOffset - scrollDelta,
            0,
            scrollViewer.ScrollableHeight);

        if (targetOffset == scrollViewer.VerticalOffset)
        {
            return;
        }

        scrollViewer.ScrollToVerticalOffset(targetOffset);
        e.Handled = true;
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

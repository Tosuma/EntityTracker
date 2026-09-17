using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EntityTracker.Wpf.Views;

public partial class HelpSqlView : UserControl
{
    private const double MouseWheelDeltaPerNotch = 120;
    private const double ScrollPixelsPerNotch = 10;

    public HelpSqlView() => InitializeComponent();

    private void OnQueryPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta == 0 || FindVisualDescendant<ScrollViewer>(QueryTextBox) is not { } scrollViewer)
        {
            return;
        }

        double targetOffset = Math.Clamp(
            scrollViewer.VerticalOffset -
                (e.Delta / MouseWheelDeltaPerNotch * ScrollPixelsPerNotch),
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

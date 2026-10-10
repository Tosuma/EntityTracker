using System.Windows;
using System.Windows.Controls;

namespace EntityTracker.Wpf.Controls;

/// <summary>
/// Lays children out on one row and shows only those that fit whole. From the first child that would
/// cross the right edge onwards, children are left out instead of wrapping onto another line.
/// </summary>
public sealed class SingleRowPanel : Panel
{
    public SingleRowPanel()
    {
        ClipToBounds = true;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, height = 0;
        foreach (UIElement child in InternalChildren)
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        foreach (UIElement child in InternalChildren)
        {
            if (!Fits(child, width, availableSize.Width)) break;
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        // The last box's trailing margin may overhang, but the panel never asks for more than the row.
        return new Size(Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        bool full = false;
        foreach (UIElement child in InternalChildren)
        {
            full = full || !Fits(child, x, finalSize.Width);
            // Children that do not fit are placed past the clipped edge, so nothing of them shows.
            child.Arrange(full
                ? new Rect(finalSize.Width, 0, child.DesiredSize.Width, child.DesiredSize.Height)
                : new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
            if (!full) x += child.DesiredSize.Width;
        }

        return finalSize;
    }

    /// <summary>Whether the child fits after <paramref name="x"/>; its trailing margin may overhang the edge.</summary>
    internal static bool Fits(UIElement child, double x, double available)
    {
        double trailing = child is FrameworkElement element ? element.Margin.Right : 0;
        return x + child.DesiredSize.Width - trailing <= available + 0.5;
    }
}

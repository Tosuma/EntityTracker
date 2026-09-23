using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace EntityTracker.Wpf.Services;

internal sealed class MouseWheelScrollRouter
{
    internal const double PixelsPerLine = 10;
    internal const int WheelPageScroll = -1;
    private const double DeltaPerNotch = 120;
    private static readonly TimeSpan GesturePause = TimeSpan.FromMilliseconds(300);

    private ScrollViewer? _gestureOwner;
    private long _lastWheelTimestamp;

    internal bool Route(MouseWheelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Delta == 0 || e.OriginalSource is not DependencyObject source)
        {
            return false;
        }

        ScrollViewer[] candidates = GetScrollViewerAncestors(source)
            .Where(static viewer => viewer.IsVisible && viewer.IsEnabled)
            .ToArray();
        int wheelScrollLines = SystemParameters.WheelScrollLines;
        if (wheelScrollLines == 0)
        {
            _gestureOwner = null;
            e.Handled = candidates.Length > 0;
            return e.Handled;
        }

        long now = Stopwatch.GetTimestamp();
        bool sameGesture = _gestureOwner is { IsVisible: true, IsEnabled: true } &&
            Stopwatch.GetElapsedTime(_lastWheelTimestamp, now) <= GesturePause;
        _lastWheelTimestamp = now;

        ScrollViewer? target = SelectTarget(
            candidates,
            _gestureOwner,
            sameGesture,
            viewer => CanScroll(viewer, e.Delta, wheelScrollLines));
        if (target is null)
        {
            _gestureOwner = null;
            return false;
        }

        _gestureOwner = target;
        TryScroll(target, e.Delta, wheelScrollLines);
        e.Handled = true;
        return true;
    }

    internal static double CalculateTargetOffset(
        double currentOffset,
        double scrollableHeight,
        double viewportHeight,
        int wheelDelta,
        bool usesLogicalScrolling,
        int wheelScrollLines)
    {
        if (wheelScrollLines == 0)
        {
            return currentOffset;
        }

        double step = wheelScrollLines == WheelPageScroll
            ? viewportHeight
            : wheelScrollLines * (usesLogicalScrolling ? 1 : PixelsPerLine);
        return Math.Clamp(
            currentOffset - wheelDelta / DeltaPerNotch * step,
            0,
            scrollableHeight);
    }

    internal static T? SelectTarget<T>(
        IReadOnlyList<T> candidates,
        T? gestureOwner,
        bool sameGesture,
        Func<T, bool> canScroll)
        where T : class
    {
        if (sameGesture && gestureOwner is not null)
        {
            return gestureOwner;
        }

        return candidates.FirstOrDefault(canScroll);
    }

    private static bool CanScroll(
        ScrollViewer viewer,
        int wheelDelta,
        int wheelScrollLines)
    {
        if (viewer.ScrollableHeight <= 0)
        {
            return false;
        }

        double target = CalculateTargetOffset(
            viewer.VerticalOffset,
            viewer.ScrollableHeight,
            viewer.ViewportHeight,
            wheelDelta,
            UsesLogicalScrolling(viewer),
            wheelScrollLines);
        return Math.Abs(target - viewer.VerticalOffset) >= 0.001;
    }

    private static void TryScroll(
        ScrollViewer viewer,
        int wheelDelta,
        int wheelScrollLines)
    {
        double target = CalculateTargetOffset(
            viewer.VerticalOffset,
            viewer.ScrollableHeight,
            viewer.ViewportHeight,
            wheelDelta,
            UsesLogicalScrolling(viewer),
            wheelScrollLines);
        if (Math.Abs(target - viewer.VerticalOffset) >= 0.001)
        {
            viewer.ScrollToVerticalOffset(target);
        }
    }

    internal static bool UsesLogicalScrolling(
        bool canContentScroll,
        ScrollUnit? itemsControlScrollUnit) =>
        canContentScroll && itemsControlScrollUnit != ScrollUnit.Pixel;

    private static bool UsesLogicalScrolling(ScrollViewer viewer)
    {
        ItemsControl? itemsControl = FindAncestor<ItemsControl>(viewer);
        ScrollUnit? scrollUnit = itemsControl is null
            ? null
            : VirtualizingPanel.GetScrollUnit(itemsControl);
        return UsesLogicalScrolling(viewer.CanContentScroll, scrollUnit);
    }

    private static IEnumerable<ScrollViewer> GetScrollViewerAncestors(DependencyObject source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is ScrollViewer viewer)
            {
                yield return viewer;
            }

            current = GetParent(current);
        }
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

    private static T? FindAncestor<T>(DependencyObject child)
        where T : DependencyObject
    {
        DependencyObject? current = GetParent(child);
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = GetParent(current);
        }

        return null;
    }
}

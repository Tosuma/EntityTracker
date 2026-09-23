using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace EntityTracker.Wpf.Services;

internal sealed class MouseWheelScrollRouter
{
    internal const double PixelsPerNotch = 10;
    internal const double LogicalItemsPerNotch = 1;
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
        long now = Stopwatch.GetTimestamp();
        bool sameGesture = _gestureOwner is not null &&
            Stopwatch.GetElapsedTime(_lastWheelTimestamp, now) <= GesturePause &&
            candidates.Contains(_gestureOwner);
        _lastWheelTimestamp = now;

        IEnumerable<ScrollViewer> ordered = sameGesture
            ? candidates.OrderByDescending(viewer => ReferenceEquals(viewer, _gestureOwner))
            : candidates;
        foreach (ScrollViewer viewer in ordered)
        {
            if (!TryScroll(viewer, e.Delta))
            {
                continue;
            }

            _gestureOwner = viewer;
            e.Handled = true;
            return true;
        }

        _gestureOwner = null;
        return false;
    }

    internal static double CalculateTargetOffset(
        double currentOffset,
        double scrollableHeight,
        int wheelDelta,
        bool usesLogicalScrolling)
    {
        double step = usesLogicalScrolling ? LogicalItemsPerNotch : PixelsPerNotch;
        return Math.Clamp(
            currentOffset - wheelDelta / DeltaPerNotch * step,
            0,
            scrollableHeight);
    }

    private static bool TryScroll(ScrollViewer viewer, int wheelDelta)
    {
        if (viewer.ScrollableHeight <= 0)
        {
            return false;
        }

        double target = CalculateTargetOffset(
            viewer.VerticalOffset,
            viewer.ScrollableHeight,
            wheelDelta,
            viewer.CanContentScroll);
        if (Math.Abs(target - viewer.VerticalOffset) < 0.001)
        {
            return false;
        }

        viewer.ScrollToVerticalOffset(target);
        return true;
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
}

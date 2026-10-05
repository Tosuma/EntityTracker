using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace EntityTracker.Wpf.Controls;

/// <summary>
/// Lets its content change height smoothly: the content is always laid out at its natural height,
/// while the decorator's own height glides to it and clips the content on the way. The first layout
/// is not animated, so it never fights an entry animation.
/// </summary>
public sealed class SmoothHeightDecorator : Decorator
{
    public static readonly DependencyProperty DisplayedHeightProperty = DependencyProperty.Register(
        nameof(DisplayedHeight), typeof(double), typeof(SmoothHeightDecorator),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(
        nameof(Duration), typeof(TimeSpan), typeof(SmoothHeightDecorator),
        new PropertyMetadata(TimeSpan.FromMilliseconds(250)));

    private double _target = double.NaN;

    /// <summary>Gets the height currently shown; animated between the old and the new natural height.</summary>
    public double DisplayedHeight
    {
        get => (double)GetValue(DisplayedHeightProperty);
        private set => SetValue(DisplayedHeightProperty, value);
    }

    /// <summary>Gets or sets how long a change of height takes.</summary>
    public TimeSpan Duration
    {
        get => (TimeSpan)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    /// <summary>Gets the height the content wants; the decorator is heading for it.</summary>
    public double TargetHeight => _target;

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is not { } child) return default;
        child.Measure(new Size(constraint.Width, double.PositiveInfinity));
        double natural = child.DesiredSize.Height;
        if (double.IsNaN(_target))
        {
            _target = natural;
            DisplayedHeight = natural;
        }
        else if (!AreClose(natural, _target))
        {
            double from = DisplayedHeight;
            _target = natural;
            // Starting an animation changes a measure-affecting property, so do it after this pass.
            Dispatcher.BeginInvoke(DispatcherPriority.Render, () => Animate(from, natural));
        }

        double shown = double.IsNaN(DisplayedHeight) ? natural : DisplayedHeight;
        return new Size(child.DesiredSize.Width, Math.Min(shown, constraint.Height));
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        // The content keeps its natural height; the decorator only shows as much as its own height.
        Child?.Arrange(new Rect(0, 0, arrangeSize.Width, Child.DesiredSize.Height));
        return arrangeSize;
    }

    /// <summary>
    /// Clips only the bottom while the height is changing, so content may still slide out sideways
    /// (as a leaving notification does) without being cut off at the decorator's edges.
    /// </summary>
    protected override Geometry GetLayoutClip(Size layoutSlotSize) =>
        new RectangleGeometry(new Rect(-1e5, 0, 2e5, RenderSize.Height));

    private void Animate(double from, double to)
    {
        if (!AreClose(to, _target)) return; // a newer size has already taken over
        if (Duration <= TimeSpan.Zero)
        {
            BeginAnimation(DisplayedHeightProperty, null);
            DisplayedHeight = to;
            return;
        }

        BeginAnimation(DisplayedHeightProperty, new DoubleAnimation(from, to, Duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.HoldEnd
        });
    }

    private static bool AreClose(double a, double b) => Math.Abs(a - b) < 0.5;
}

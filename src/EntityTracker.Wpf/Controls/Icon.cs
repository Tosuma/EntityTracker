using System.Windows;
using System.Windows.Media;

namespace EntityTracker.Wpf.Controls;

/// <summary>
/// Draws one of the app's icons (Themes/EntityTrackerIcons.xaml: Fluent UI System Icons paths in a
/// 20 x 20 box) at <see cref="Size"/>, filled with <see cref="Foreground"/>. Prefer an icon, with its
/// meaning in a tooltip and automation name, where it can say what a word would.
/// </summary>
public class Icon : FrameworkElement
{
    private const double SourceSize = 20;

    public static readonly DependencyProperty GeometryProperty = DependencyProperty.Register(
        nameof(Geometry), typeof(Geometry), typeof(Icon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(Icon),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(Icon),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure |
                                            FrameworkPropertyMetadataOptions.AffectsRender));

    public Geometry? Geometry
    {
        get => (Geometry?)GetValue(GeometryProperty);
        set => SetValue(GeometryProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize) => new(Size, Size);

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Geometry is null) return;
        double scale = Size / SourceSize;
        // Centred in the space the icon is given, so a rotation about its centre (the turning sync
        // icon) keeps it in place even when that space is larger than the icon.
        drawingContext.PushTransform(new TranslateTransform(
            Math.Max(0, (ActualWidth - Size) / 2), Math.Max(0, (ActualHeight - Size) / 2)));
        drawingContext.PushTransform(new ScaleTransform(scale, scale));
        drawingContext.DrawGeometry(Foreground, null, Geometry);
        drawingContext.Pop();
        drawingContext.Pop();
    }
}

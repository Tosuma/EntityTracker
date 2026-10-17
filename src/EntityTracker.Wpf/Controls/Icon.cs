using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace EntityTracker.Wpf.Controls;

/// <summary>
/// Draws one of the app's icons (Themes/EntityTrackerIcons.xaml: Fluent UI System Icons paths in a
/// 20 x 20 box) at <see cref="Size"/>, filled with <see cref="Foreground"/>. Set the icon with
/// <see cref="IconKey"/> (its resource name, such as "Icon.Search") or <see cref="Geometry"/>. Prefer an
/// icon, with its meaning in a tooltip and automation name, where it can say what a word would.
/// </summary>
public class Icon : FrameworkElement
{
    private const double SourceSize = 20;

    public static readonly DependencyProperty GeometryProperty = DependencyProperty.Register(
        nameof(Geometry), typeof(Geometry), typeof(Icon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IconKeyProperty = DependencyProperty.Register(
        nameof(IconKey), typeof(string), typeof(Icon),
        new PropertyMetadata(null, (element, args) =>
        {
            Icon icon = (Icon)element;
            if (args.NewValue is string key && key.Length > 0) icon.SetResourceReference(GeometryProperty, key);
            else icon.ClearValue(GeometryProperty);
        }));

    // Inherited like text colour, so an icon in a button takes the button's colour, dimmed when disabled.
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(Icon), new FrameworkPropertyMetadata(SystemColors.ControlTextBrush,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(Icon),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure |
                                            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsSpinningProperty = DependencyProperty.Register(
        nameof(IsSpinning), typeof(bool), typeof(Icon),
        new PropertyMetadata(false, (element, _) => ((Icon)element).Spin()));

    private readonly RotateTransform _rotation = new();

    public Icon()
    {
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _rotation;
    }

    public Geometry? Geometry
    {
        get => (Geometry?)GetValue(GeometryProperty);
        set => SetValue(GeometryProperty, value);
    }

    public string? IconKey
    {
        get => (string?)GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
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

    /// <summary>
    /// Turns the icon, to show that work is running (syncing). It turns whatever Windows' animation
    /// effects setting: the turning is what tells the user something is in progress.
    /// </summary>
    public bool IsSpinning
    {
        get => (bool)GetValue(IsSpinningProperty);
        set => SetValue(IsSpinningProperty, value);
    }

    private void Spin()
    {
        if (IsSpinning)
            _rotation.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever });
        else
            _rotation.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize) => new(Size, Size);

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Geometry is null) return;
        double scale = Size / SourceSize;
        // Centred in the space the icon is given, so a rotation about its centre (a turning icon) keeps
        // it in place even when that space is larger than the icon.
        drawingContext.PushTransform(new TranslateTransform(
            Math.Max(0, (ActualWidth - Size) / 2), Math.Max(0, (ActualHeight - Size) / 2)));
        drawingContext.PushTransform(new ScaleTransform(scale, scale));
        drawingContext.DrawGeometry(Foreground, null, Geometry);
        drawingContext.Pop();
        drawingContext.Pop();
    }
}

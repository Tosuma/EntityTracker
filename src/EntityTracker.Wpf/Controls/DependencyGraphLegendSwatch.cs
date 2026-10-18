using System.Windows;
using System.Windows.Media;

using EntityTracker.Domain;

namespace EntityTracker.Wpf.Controls;

/// <summary>A legend dot drawn like the matching dependency graph node.</summary>
public sealed class DependencyGraphLegendSwatch : FrameworkElement
{
    public static readonly DependencyProperty BrushKeyProperty = DependencyProperty.Register(
        nameof(BrushKey), typeof(string), typeof(DependencyGraphLegendSwatch),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(DevelopmentStatus?), typeof(DependencyGraphLegendSwatch),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsPlaceholderProperty = DependencyProperty.Register(
        nameof(IsPlaceholder), typeof(bool), typeof(DependencyGraphLegendSwatch),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public string? BrushKey
    {
        get => (string?)GetValue(BrushKeyProperty);
        set => SetValue(BrushKeyProperty, value);
    }

    /// <summary>The status whose mark is drawn on the dot, as on the graph's nodes.</summary>
    public DevelopmentStatus? Status
    {
        get => (DevelopmentStatus?)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public bool IsPlaceholder
    {
        get => (bool)GetValue(IsPlaceholderProperty);
        set => SetValue(IsPlaceholderProperty, value);
    }

    protected override void OnRender(DrawingContext context)
    {
        Brush brush = BrushKey is null ? Brushes.Gray : TryFindResource(BrushKey) as Brush ?? Brushes.Gray;
        double radius = Math.Min(ActualWidth, ActualHeight) / 2 - 0.75;
        Point center = new(ActualWidth / 2, ActualHeight / 2);
        if (IsPlaceholder)
            context.DrawEllipse(null, new Pen(brush, 1.25) { DashStyle = DashStyles.Dash }, center, radius, radius);
        else
        {
            // The dark theme's thin status outline (none in light), so the dark fills stay visible.
            Pen? outline = TryFindResource("Brush.Status.Outline") is SolidColorBrush { Color.A: > 0 } line
                ? new Pen(line, 1) : null;
            context.DrawEllipse(brush, outline, center, radius, radius);
            // The same mark as the graph's nodes, so the legend tells the statuses apart by shape too.
            if (Status is { } status && BrushKey is { } key &&
                DependencyGraphStatusGlyph.For(status, center, radius) is { } glyph)
            {
                Brush mark = TryFindResource($"{key}.Text") as Brush ?? Brushes.Black;
                context.DrawGeometry(null, new Pen(mark, DependencyGraphStatusGlyph.Thickness(radius)), glyph);
            }
        }
    }
}

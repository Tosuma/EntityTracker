using System.Windows;
using System.Windows.Media;

namespace EntityTracker.Wpf.Controls;

/// <summary>A legend dot drawn like the matching dependency graph node.</summary>
public sealed class DependencyGraphLegendSwatch : FrameworkElement
{
    public static readonly DependencyProperty BrushKeyProperty = DependencyProperty.Register(
        nameof(BrushKey), typeof(string), typeof(DependencyGraphLegendSwatch),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsPlaceholderProperty = DependencyProperty.Register(
        nameof(IsPlaceholder), typeof(bool), typeof(DependencyGraphLegendSwatch),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public string? BrushKey
    {
        get => (string?)GetValue(BrushKeyProperty);
        set => SetValue(BrushKeyProperty, value);
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
            context.DrawEllipse(brush, null, center, radius, radius);
    }
}

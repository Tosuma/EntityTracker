using System.Windows;
using System.Windows.Media;

using EntityTracker.Reporting;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Controls;

/// <summary>
/// Keeps a view model's progress charts in the app theme: set <c>ChartThemeFollower.Target</c> on the
/// element that shows the charts. It watches the page brush, which the theme replaces when Light or Dark
/// changes, and tells the target whether the page is dark.
/// </summary>
public static class ChartThemeFollower
{
    public static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
        "Target", typeof(IChartThemeTarget), typeof(ChartThemeFollower),
        new PropertyMetadata(null, OnTargetChanged));

    // Follows Brush.Surface.Page through a resource reference, so a theme change calls OnPageBrushChanged.
    private static readonly DependencyProperty PageBrushProperty = DependencyProperty.RegisterAttached(
        "PageBrush", typeof(Brush), typeof(ChartThemeFollower),
        new PropertyMetadata(null, OnPageBrushChanged));

    public static IChartThemeTarget? GetTarget(DependencyObject element) =>
        (IChartThemeTarget?)element.GetValue(TargetProperty);

    public static void SetTarget(DependencyObject element, IChartThemeTarget? value) =>
        element.SetValue(TargetProperty, value);

    /// <summary>Gets the chart theme for a page brush: dark when the page is darker than mid-grey.</summary>
    internal static ProgressChartTheme ThemeFor(Brush? pageBrush) =>
        pageBrush is SolidColorBrush { Color: var color } &&
        0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B < 128
            ? ProgressChartTheme.Dark
            : ProgressChartTheme.Light;

    private static void OnTargetChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not FrameworkElement frameworkElement) return;
        if (frameworkElement.ReadLocalValue(PageBrushProperty) == DependencyProperty.UnsetValue)
            frameworkElement.SetResourceReference(PageBrushProperty, "Brush.Surface.Page");
        Apply(frameworkElement);
    }

    private static void OnPageBrushChanged(DependencyObject element, DependencyPropertyChangedEventArgs e) =>
        Apply(element);

    private static void Apply(DependencyObject element)
    {
        if (GetTarget(element) is { } target)
            target.ChartTheme = ThemeFor((Brush?)element.GetValue(PageBrushProperty));
    }
}

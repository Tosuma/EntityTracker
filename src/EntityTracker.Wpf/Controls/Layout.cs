using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace EntityTracker.Wpf.Controls;

/// <summary>
/// Whether the window is narrow enough for the compact layout. MainWindow sets it from its width; it is
/// inherited, so any view's style can react with
/// <c>{Binding (controls:Layout.IsCompact), RelativeSource={RelativeSource Self}}</c>. Page toolbars move under
/// their heading and wrap; a grid marked <see cref="StackWhenCompactProperty"/> stacks its two panes; a
/// UniformGrid with <see cref="CompactColumnsProperty"/> uses fewer columns.
/// </summary>
public static class Layout
{
    /// <summary>Below this window width the compact layout is used; the window's minimum is 900.</summary>
    public const double CompactWidth = 1280;

    private const double StackGap = 16;

    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.RegisterAttached(
        "IsCompact", typeof(bool), typeof(Layout),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, OnIsCompactChanged));

    /// <summary>
    /// On a Grid with panes in columns 0 and 2 (column 1 is the gap): stacks the second pane under the first
    /// while compact, each across the full width.
    /// </summary>
    public static readonly DependencyProperty StackWhenCompactProperty = DependencyProperty.RegisterAttached(
        "StackWhenCompact", typeof(bool), typeof(Layout), new PropertyMetadata(false, OnArrangementChanged));

    /// <summary>On a UniformGrid: the number of columns while compact; 0 keeps its own.</summary>
    public static readonly DependencyProperty CompactColumnsProperty = DependencyProperty.RegisterAttached(
        "CompactColumns", typeof(int), typeof(Layout), new PropertyMetadata(0, OnArrangementChanged));

    private static readonly DependencyProperty WideColumnsProperty = DependencyProperty.RegisterAttached(
        "WideColumns", typeof(int), typeof(Layout), new PropertyMetadata(-1));

    private static readonly DependencyProperty WideColumnProperty = DependencyProperty.RegisterAttached(
        "WideColumn", typeof(int), typeof(Layout), new PropertyMetadata(-1));

    public static bool GetIsCompact(DependencyObject element) => (bool)element.GetValue(IsCompactProperty);

    public static void SetIsCompact(DependencyObject element, bool value) => element.SetValue(IsCompactProperty, value);

    public static bool GetStackWhenCompact(DependencyObject element) =>
        (bool)element.GetValue(StackWhenCompactProperty);

    public static void SetStackWhenCompact(DependencyObject element, bool value) =>
        element.SetValue(StackWhenCompactProperty, value);

    public static int GetCompactColumns(DependencyObject element) => (int)element.GetValue(CompactColumnsProperty);

    public static void SetCompactColumns(DependencyObject element, int value) =>
        element.SetValue(CompactColumnsProperty, value);

    /// <summary>Gets whether a window of this width uses the compact layout.</summary>
    public static bool IsCompactWidth(double width) => width < CompactWidth;

    private static void OnIsCompactChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        // Inherited, so this runs for every element whose value changed; only the marked panels act.
        if (element is Grid or UniformGrid) Arrange(element);
    }

    private static void OnArrangementChanged(DependencyObject element, DependencyPropertyChangedEventArgs e) =>
        Arrange(element);

    private static void Arrange(DependencyObject element)
    {
        bool compact = GetIsCompact(element);
        if (element is Grid grid && GetStackWhenCompact(grid)) ArrangePanes(grid, compact);
        else if (element is UniformGrid uniform && GetCompactColumns(uniform) > 0) ArrangeColumns(uniform, compact);
    }

    private static void ArrangePanes(Grid grid, bool compact)
    {
        grid.RowDefinitions.Clear();
        if (compact)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(StackGap) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        int span = Math.Max(1, grid.ColumnDefinitions.Count);
        foreach (UIElement child in grid.Children)
        {
            int wideColumn = (int)child.GetValue(WideColumnProperty);
            if (wideColumn < 0)
            {
                wideColumn = Grid.GetColumn(child);
                child.SetValue(WideColumnProperty, wideColumn);
            }

            Grid.SetColumn(child, compact ? 0 : wideColumn);
            Grid.SetColumnSpan(child, compact ? span : 1);
            Grid.SetRow(child, compact && wideColumn > 0 ? 2 : 0);
        }
    }

    private static void ArrangeColumns(UniformGrid grid, bool compact)
    {
        int wide = (int)grid.GetValue(WideColumnsProperty);
        if (wide < 0)
        {
            wide = grid.Columns;
            grid.SetValue(WideColumnsProperty, wide);
        }

        grid.Columns = compact ? GetCompactColumns(grid) : wide;
    }
}

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace EntityTracker.Wpf.Controls;

/// <summary>
/// What "Implemented" is made of: a thin bar of the Reconciled, Rework needed, Reworking and Completed
/// shares of <see cref="Total"/>, in their status colours. It shows wherever the Implemented number does,
/// with <see cref="ImplementedBreakdown"/> as the hover on the number, so it explains itself without a
/// description.
/// </summary>
public sealed class ImplementedComposition : Border
{
    public static readonly DependencyProperty CompletedProperty = Count(nameof(Completed));
    public static readonly DependencyProperty ReconciledProperty = Count(nameof(Reconciled));
    public static readonly DependencyProperty ReworkNeededProperty = Count(nameof(ReworkNeeded));
    public static readonly DependencyProperty ReworkingProperty = Count(nameof(Reworking));
    public static readonly DependencyProperty TotalProperty = Count(nameof(Total));

    private readonly Grid _bar = new();

    public ImplementedComposition()
    {
        Height = 6;
        CornerRadius = new CornerRadius(3);
        ClipToBounds = true;
        SetResourceReference(BackgroundProperty, "Brush.Border.Default");
        Child = _bar;
        foreach (ImplementedBreakdown.Part part in ImplementedBreakdown.Parts)
        {
            var segment = new Border();
            segment.SetResourceReference(BackgroundProperty, part.BrushKey);
            Grid.SetColumn(segment, _bar.Children.Count);
            _bar.Children.Add(segment);
        }

        Update();
    }

    public int Completed { get => (int)GetValue(CompletedProperty); set => SetValue(CompletedProperty, value); }

    public int Reconciled { get => (int)GetValue(ReconciledProperty); set => SetValue(ReconciledProperty, value); }

    public int ReworkNeeded { get => (int)GetValue(ReworkNeededProperty); set => SetValue(ReworkNeededProperty, value); }

    public int Reworking { get => (int)GetValue(ReworkingProperty); set => SetValue(ReworkingProperty, value); }

    /// <summary>The entities the shares are measured against (the relevant, active ones).</summary>
    public int Total { get => (int)GetValue(TotalProperty); set => SetValue(TotalProperty, value); }

    /// <summary>The bar's star widths, left to right: the four parts, then the rest of <see cref="Total"/>.</summary>
    internal IReadOnlyList<double> SegmentWeights =>
        _bar.ColumnDefinitions.Select(static column => column.Width.Value).ToArray();

    private static DependencyProperty Count(string name) => DependencyProperty.Register(
        name, typeof(int), typeof(ImplementedComposition),
        new PropertyMetadata(0, static (owner, _) => ((ImplementedComposition)owner).Update()));

    private void Update()
    {
        int[] values = [Reconciled, ReworkNeeded, Reworking, Completed];
        values = values.Select(static value => Math.Max(0, value)).ToArray();
        int rest = Math.Max(0, Total - values.Sum());

        _bar.ColumnDefinitions.Clear();
        foreach (int value in values.Append(rest))
            _bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(value, GridUnitType.Star) });

        AutomationProperties.SetHelpText(this,
            "Implemented: " + ImplementedBreakdown.Text(Completed, Reconciled, ReworkNeeded, Reworking));
    }
}

/// <summary>
/// The hover on an Implemented number: a dot in the status colour and the count for each status it counts,
/// all four always listed. Set as the ToolTip of the whole number (label, value and bar); a tooltip takes
/// its owner's DataContext, so the counts bind as usual.
/// </summary>
public sealed class ImplementedBreakdown : StackPanel
{
    public static readonly DependencyProperty CompletedProperty = Count(nameof(Completed));
    public static readonly DependencyProperty ReconciledProperty = Count(nameof(Reconciled));
    public static readonly DependencyProperty ReworkNeededProperty = Count(nameof(ReworkNeeded));
    public static readonly DependencyProperty ReworkingProperty = Count(nameof(Reworking));

    internal sealed record Part(string BrushKey, string Label);

    /// <summary>Left to right on the bar, top to bottom here, as on the Overview's "% implemented" card.</summary>
    internal static IReadOnlyList<Part> Parts { get; } =
    [
        new("Brush.Status.Reconciled", "Reconciled"),
        new("Brush.Status.ReworkNeeded", "Rework needed"),
        new("Brush.Status.Reworking", "Reworking"),
        new("Brush.Status.DevelopmentCompleted", "Completed")
    ];

    private readonly TextBlock[] _counts;

    public ImplementedBreakdown()
    {
        _counts = Parts.Select((part, index) =>
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, index == 0 ? 0 : 2, 0, 0) };
            var dot = new Border
            {
                Width = 8, Height = 8, CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center
            };
            dot.SetResourceReference(BackgroundProperty, part.BrushKey);
            var count = new TextBlock();
            row.Children.Add(dot);
            row.Children.Add(count);
            Children.Add(row);
            return count;
        }).ToArray();
        Update();
    }

    public int Completed { get => (int)GetValue(CompletedProperty); set => SetValue(CompletedProperty, value); }

    public int Reconciled { get => (int)GetValue(ReconciledProperty); set => SetValue(ReconciledProperty, value); }

    public int ReworkNeeded { get => (int)GetValue(ReworkNeededProperty); set => SetValue(ReworkNeededProperty, value); }

    public int Reworking { get => (int)GetValue(ReworkingProperty); set => SetValue(ReworkingProperty, value); }

    /// <summary>The four counts on one line, as screen readers and the report give them.</summary>
    public static string Text(int completed, int reconciled, int reworkNeeded, int reworking) =>
        $"{completed} Completed, {reconciled} Reconciled, {reworkNeeded} Rework needed, {reworking} Reworking";

    internal IReadOnlyList<string> Lines => _counts.Select(static count => count.Text).ToArray();

    private static DependencyProperty Count(string name) => DependencyProperty.Register(
        name, typeof(int), typeof(ImplementedBreakdown),
        new PropertyMetadata(0, static (owner, _) => ((ImplementedBreakdown)owner).Update()));

    private void Update()
    {
        int[] values = [Reconciled, ReworkNeeded, Reworking, Completed];
        for (int index = 0; index < _counts.Length; index++)
            _counts[index].Text = $"{values[index]} {Parts[index].Label}";
    }
}

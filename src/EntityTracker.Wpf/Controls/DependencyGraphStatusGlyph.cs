using System.Windows;
using System.Windows.Media;

using EntityTracker.Domain;

namespace EntityTracker.Wpf.Controls;

/// <summary>
/// The mark drawn inside a dependency graph node, and in its legend swatch, so a status is told by
/// shape as well as colour. Each mark is a filled Fluent System Icon from <c>Themes/EntityTrackerIcons.xaml</c>:
/// Play for In progress, Fast Forward for Reworking, Important for Rework needed, Subtract for Blocked,
/// Checkmark for Completed, Checkmark Starburst for Reconciled and Prohibited for Not relevant. Not started
/// is plain. Filled in the status's text colour, which reads on every status colour.
/// </summary>
internal static class DependencyGraphStatusGlyph
{
    /// <summary>Nodes smaller than this (in pixels) are too small for a readable mark.</summary>
    internal const double MinimumRadius = 5;

    // The icons' 20-pixel box, and how much of a node's diameter the box takes.
    private const double IconBox = 20;
    private const double BoxPerRadius = 1.3;

    private static readonly Lazy<ResourceDictionary> Icons = new(static () =>
        (ResourceDictionary)System.Windows.Application.LoadComponent(
            new Uri("/EntityTracker.Wpf;component/Themes/EntityTrackerIcons.xaml", UriKind.Relative)));

    /// <summary>Gets the key of the Fluent icon that marks the status, or null for none.</summary>
    internal static string? IconKey(DevelopmentStatus status) => status switch
    {
        DevelopmentStatus.InProgress => "Icon.Play",
        DevelopmentStatus.Reworking => "Icon.FastForward",
        DevelopmentStatus.ReworkNeeded => "Icon.Important",
        DevelopmentStatus.Blocked => "Icon.Subtract",
        DevelopmentStatus.DevelopmentCompleted => "Icon.Checkmark",
        DevelopmentStatus.Reconciled => "Icon.CheckmarkStarburst",
        DevelopmentStatus.NotRelevant => "Icon.Prohibited",
        _ => null
    };

    /// <summary>Gets the mark, to fill, for a node of this radius around the center, or null for no mark.</summary>
    internal static Geometry? For(DevelopmentStatus status, Point center, double radius)
    {
        if (radius < MinimumRadius || IconKey(status) is not { } key || Icons.Value[key] is not Geometry icon)
            return null;
        double size = radius * BoxPerRadius;
        Geometry mark = icon.Clone();
        mark.Transform = new MatrixTransform(size / IconBox, 0, 0, size / IconBox,
            center.X - size / 2, center.Y - size / 2);
        mark.Freeze();
        return mark;
    }
}

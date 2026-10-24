using System.Windows;
using System.Windows.Media;

using EntityTracker.Domain;

namespace EntityTracker.Wpf.Controls;

/// <summary>
/// The mark drawn inside a dependency graph node, and in its legend swatch, so a status is told by
/// shape as well as colour: a play triangle for In progress, two for Reworking, an exclamation bar for
/// Rework needed, a bar for Blocked, a check for Completed, two for Reconciled and a slashed circle for
/// Not relevant. Not started is plain.
/// Drawn as lines in the status's text colour, which reads on every status colour.
/// </summary>
internal static class DependencyGraphStatusGlyph
{
    /// <summary>Nodes smaller than this (in pixels) are too small for a readable mark.</summary>
    internal const double MinimumRadius = 5;

    /// <summary>Gets the mark's outline for a node of this radius, or null for no mark.</summary>
    internal static Geometry? For(DevelopmentStatus status, Point center, double radius)
    {
        if (radius < MinimumRadius || status == DevelopmentStatus.NotStarted) return null;
        double r = radius * 0.5;
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            switch (status)
            {
                case DevelopmentStatus.InProgress:
                    Triangle(context, center, r);
                    break;
                case DevelopmentStatus.Reworking:
                    Triangle(context, new Point(center.X - r * 0.45, center.Y), r * 0.7);
                    Triangle(context, new Point(center.X + r * 0.45, center.Y), r * 0.7);
                    break;
                case DevelopmentStatus.ReworkNeeded:
                    Line(context, new Point(center.X, center.Y - r), new Point(center.X, center.Y + r * 0.25));
                    Line(context, new Point(center.X, center.Y + r * 0.7), new Point(center.X, center.Y + r * 0.75));
                    break;
                case DevelopmentStatus.Blocked:
                    Line(context, new Point(center.X - r, center.Y), new Point(center.X + r, center.Y));
                    break;
                case DevelopmentStatus.DevelopmentCompleted:
                    Check(context, center, r);
                    break;
                case DevelopmentStatus.Reconciled:
                    Check(context, new Point(center.X - r * 0.35, center.Y), r * 0.75);
                    Check(context, new Point(center.X + r * 0.35, center.Y), r * 0.75);
                    break;
                case DevelopmentStatus.NotRelevant:
                    SlashedCircle(context, center, r);
                    break;
            }
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>Gets the line thickness for a node of this radius.</summary>
    internal static double Thickness(double radius) => Math.Max(1.2, radius * 0.16);

    private static void Triangle(StreamGeometryContext context, Point center, double r)
    {
        context.BeginFigure(new Point(center.X - r * 0.55, center.Y - r * 0.8), false, true);
        context.LineTo(new Point(center.X + r * 0.75, center.Y), true, false);
        context.LineTo(new Point(center.X - r * 0.55, center.Y + r * 0.8), true, false);
    }

    private static void Check(StreamGeometryContext context, Point center, double r)
    {
        context.BeginFigure(new Point(center.X - r * 0.8, center.Y), false, false);
        context.LineTo(new Point(center.X - r * 0.2, center.Y + r * 0.6), true, false);
        context.LineTo(new Point(center.X + r * 0.8, center.Y - r * 0.6), true, false);
    }

    private static void SlashedCircle(StreamGeometryContext context, Point center, double r)
    {
        Point top = new(center.X, center.Y - r);
        context.BeginFigure(top, false, true);
        context.ArcTo(new Point(center.X, center.Y + r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
        context.ArcTo(top, new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
        double d = r * 0.7071;
        Line(context, new Point(center.X - d, center.Y + d), new Point(center.X + d, center.Y - d));
    }

    private static void Line(StreamGeometryContext context, Point from, Point to)
    {
        context.BeginFigure(from, false, false);
        context.LineTo(to, true, false);
    }
}

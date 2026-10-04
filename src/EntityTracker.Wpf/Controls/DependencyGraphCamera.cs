using System.Windows;
using System.Windows.Media;

namespace EntityTracker.Wpf.Controls;

/// <summary>
/// The camera on the dependency map: zoom, pan and the slow rotation around the centre.
/// A world point is rotated by <see cref="Angle"/>, scaled, then shifted by <see cref="Offset"/>.
/// </summary>
internal readonly record struct DependencyGraphCamera(double Scale, Vector Offset, double Angle)
{
    internal Point ToScreen(double x, double y)
    {
        (double sin, double cos) = Math.SinCos(Angle);
        return new Point(Offset.X + Scale * (x * cos - y * sin), Offset.Y + Scale * (x * sin + y * cos));
    }

    internal Point ToWorld(Point screen)
    {
        (double sin, double cos) = Math.SinCos(Angle);
        double x = (screen.X - Offset.X) / Scale;
        double y = (screen.Y - Offset.Y) / Scale;
        return new Point(x * cos + y * sin, -x * sin + y * cos);
    }

    /// <summary>
    /// Gets the transform that moves a drawing made with <paramref name="rendered"/> to where this
    /// view would draw it, so the cached map can follow pan, zoom and rotation without a redraw.
    /// </summary>
    internal Matrix LayerMatrix(DependencyGraphCamera rendered)
    {
        Matrix matrix = Matrix.Identity;
        matrix.Translate(-rendered.Offset.X, -rendered.Offset.Y);
        matrix.Rotate((Angle - rendered.Angle) * 180 / Math.PI);
        matrix.Scale(Scale / rendered.Scale, Scale / rendered.Scale);
        matrix.Translate(Offset.X, Offset.Y);
        return matrix;
    }

    /// <summary>Gets the offset that keeps <paramref name="world"/> under <paramref name="screen"/>.</summary>
    internal Vector OffsetKeeping(Point world, Point screen) =>
        screen - (this with { Offset = default }).ToScreen(world.X, world.Y);
}

using System.Windows;

using EntityTracker.Wpf.Controls;

namespace EntityTracker.Wpf.Tests.Controls;

public sealed class DependencyGraphViewTests
{
    [Fact]
    public void ScreenAndWorld_RoundTripWhileRotated()
    {
        DependencyGraphView view = new(1.7, new Vector(320, 180), 0.6);

        Point screen = view.ToScreen(42, -17);
        Point world = view.ToWorld(screen);

        Assert.Equal(42, world.X, 6);
        Assert.Equal(-17, world.Y, 6);
    }

    [Fact]
    public void Rotation_TurnsAroundTheCentre()
    {
        DependencyGraphView view = new(2, new Vector(100, 100), Math.PI / 2);

        Point turned = view.ToScreen(10, 0);

        Assert.Equal(100, turned.X, 6);
        Assert.Equal(120, turned.Y, 6);
        Assert.Equal(new Point(100, 100), view.ToScreen(0, 0));
    }

    [Fact]
    public void LayerMatrix_MovesTheCachedDrawingToTheNewView()
    {
        DependencyGraphView rendered = new(1.2, new Vector(400, 300), 0.3);
        DependencyGraphView current = new(1.8, new Vector(380, 330), 0.45);

        foreach ((double x, double y) in new[] { (0.0, 0.0), (50.0, -20.0), (-80.0, 65.0) })
        {
            Point moved = current.LayerMatrix(rendered).Transform(rendered.ToScreen(x, y));
            Point expected = current.ToScreen(x, y);
            Assert.Equal(expected.X, moved.X, 6);
            Assert.Equal(expected.Y, moved.Y, 6);
        }
    }

    [Fact]
    public void OffsetKeeping_HoldsAWorldPointUnderTheCursor()
    {
        DependencyGraphView view = new(2.5, new Vector(10, 20), 1.1);
        Point world = new(33, -12);
        Point cursor = new(250, 140);

        DependencyGraphView zoomed = view with { Offset = view.OffsetKeeping(world, cursor) };

        Point result = zoomed.ToScreen(world.X, world.Y);
        Assert.Equal(cursor.X, result.X, 6);
        Assert.Equal(cursor.Y, result.Y, 6);
    }
}

using System.IO;
using System.Windows;
using System.Windows.Media;

using EntityTracker.Domain;
using EntityTracker.Wpf.ViewModels.DependencyGraph;

using static EntityTracker.Wpf.Tests.Controls.GraphCanvasHost;

namespace EntityTracker.Wpf.Tests.Controls;

/// <summary>Drives the real canvas: pointer input, what is drawn, fitting and export.</summary>
[Collection(WpfWindowCollection.Name)]
public sealed class DependencyGraphCanvasTests
{
    private static readonly Point EmptySpot = new(6, 6);

    [Fact]
    public void Click_SelectsAnEntityAndClickingEmptySpaceClearsIt()
    {
        Run(host =>
        {
            DependencyGraphNode node = host.Graph.Model.Nodes[5];
            host.Click(host.Canvas.ScreenPositionOf(node));
            Assert.Same(node, host.Graph.SelectedNode);

            host.Click(EmptySpot);
            Assert.Null(host.Graph.SelectedNode);
        });
    }

    [Fact]
    public void Click_LeavesTheMapStillSoADoubleClickLandsOnTheSameEntity()
    {
        // Regression: a plain click used to restart the layout and make the entities drift.
        List<EntityId> opened = [];
        Run(host =>
        {
            DependencyGraphNode node = host.Graph.Model.Nodes[5];
            Point[] before = host.Graph.Model.Nodes.Select(host.Canvas.ScreenPositionOf).ToArray();

            host.Click(host.Canvas.ScreenPositionOf(node));
            Pump(0.5);

            Assert.Equal(before, host.Graph.Model.Nodes.Select(host.Canvas.ScreenPositionOf));
            Assert.False(host.Canvas.IsAnimating);
            host.Canvas.PointerPressed(host.Canvas.ScreenPositionOf(node), clickCount: 2);
            Assert.Equal([node.EntityId!], opened);
            Assert.Same(node, host.Graph.SelectedNode);
        }, openDetails: id => { opened.Add(id); return true; });
    }

    [Fact]
    public void Drag_MovesTheEntityAndItStaysWhereItWasDropped()
    {
        Run(host =>
        {
            DependencyGraphNode node = host.Graph.Model.Nodes[^1];
            Point start = host.Canvas.ScreenPositionOf(node);
            Point drop = start + new Vector(70, -45);

            host.Drag(start, drop);
            Pump(1);

            Point settled = host.Canvas.ScreenPositionOf(node);
            Assert.True((settled - drop).Length < 4, $"Dropped at {drop}, settled at {settled}.");
            Assert.Null(host.Graph.SelectedNode);
        });
    }

    [Fact]
    public void Pan_MovesEveryEntityTogether()
    {
        Run(host =>
        {
            Point[] before = host.Graph.Model.Nodes.Select(host.Canvas.ScreenPositionOf).ToArray();
            Vector shift = new(50, 30);

            host.Drag(EmptySpot, EmptySpot + shift);

            Point[] after = host.Graph.Model.Nodes.Select(host.Canvas.ScreenPositionOf).ToArray();
            Assert.All(before.Zip(after), pair => Assert.True(((pair.Second - pair.First) - shift).Length < 0.01));
            Assert.Null(host.Graph.SelectedNode);
        });
    }

    [Fact]
    public void Wheel_ZoomsAroundThePointer()
    {
        Run(host =>
        {
            DependencyGraphNode node = host.Graph.Model.Nodes[3];
            Point pointer = host.Canvas.ScreenPositionOf(node);
            double scale = host.Canvas.Scale;

            host.Canvas.PointerWheel(pointer, 240);

            Assert.True(host.Canvas.Scale > scale);
            Assert.True((host.Canvas.ScreenPositionOf(node) - pointer).Length < 0.01);
        });
    }

    [Fact]
    public void Hover_ShowsTheInfoCardAndLeavingHidesIt()
    {
        Run(host =>
        {
            DependencyGraphNode node = host.Graph.Model.Nodes[2];
            Assert.True(IsEmpty(host.Canvas.HoverCardDrawing));

            host.Canvas.PointerMoved(host.Canvas.ScreenPositionOf(node));
            Assert.Same(node, host.Canvas.HoveredNode);
            Assert.False(IsEmpty(host.Canvas.HoverCardDrawing));

            host.Canvas.PointerLeft();
            Assert.Null(host.Canvas.HoveredNode);
            Assert.True(IsEmpty(host.Canvas.HoverCardDrawing));
        });
    }

    [Fact]
    public void Rings_AreDrawnOnlyWhenSwitchedOn()
    {
        Run(host =>
        {
            Assert.Equal(0, RingCount(host));

            host.Graph.ShowRings = true;
            Assert.Equal(host.Graph.Layout.RingRadii.Count, RingCount(host));

            host.Graph.ShowRings = false;
            Assert.Equal(0, RingCount(host));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FitToView_KeepsEveryEntityInView(bool animate)
    {
        Run(host =>
        {
            host.Canvas.PointerWheel(new Point(400, 300), 1200);
            host.Canvas.FitToView();

            Rect bounds = new(0, 0, host.Canvas.ActualWidth, host.Canvas.ActualHeight);
            Assert.All(host.Graph.Model.Nodes, node => Assert.True(bounds.Contains(host.Canvas.ScreenPositionOf(node))));
            if (!animate) return;

            // While turning, the whole circle must fit, so no entity can rotate out of view.
            double reach = host.Graph.Model.Nodes.Max(node => Math.Sqrt(node.X * node.X + node.Y * node.Y));
            Assert.True(reach * host.Canvas.Scale <= Math.Min(bounds.Width, bounds.Height) / 2);
        }, animate);
    }

    [Fact]
    public void ZoomedIn_DrawsOnlyEntitiesNearTheView()
    {
        Run(host =>
        {
            int all = NodeCount(host);
            Assert.True(all >= host.Graph.Model.Nodes.Count);

            DependencyGraphNode edge = host.Graph.Model.Nodes[^1];
            host.Canvas.PointerWheel(host.Canvas.ScreenPositionOf(edge), 1200);
            host.Canvas.CenterOn(edge);

            int drawn = NodeCount(host);
            Assert.True(drawn < all, $"Zoomed in, {drawn} of {all} entities were still drawn.");
            Assert.True(drawn > 0);
        });
    }

    [Fact]
    public void SavePng_WritesTheCurrentViewAsAnImage()
    {
        string path = Path.Combine(Path.GetTempPath(), $"graph-{Guid.NewGuid():N}.png");
        try
        {
            Run(host => host.Canvas.SavePng(path));

            byte[] header = File.ReadAllBytes(path).Take(24).ToArray();
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, header[..8]);
            int width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            Assert.True(width >= 800, $"The image should cover the whole map ({width}px wide).");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ThemeChange_RedrawsWithTheNewBrushes()
    {
        Run(host =>
        {
            host.Canvas.EdgeBrush = new SolidColorBrush(Colors.Red);

            Assert.Contains(host.MapPrimitives(), drawing =>
                drawing.Pen?.Brush is SolidColorBrush { Color: var color } && color == Colors.Red);
        });
    }

    private static int RingCount(GraphCanvasHost host)
    {
        double[] radii = host.Graph.Layout.RingRadii.Select(radius => radius * host.Canvas.Scale).ToArray();
        return host.MapPrimitives().Count(drawing => drawing.Geometry is EllipseGeometry ellipse &&
            drawing.Brush is null && radii.Any(radius => Math.Abs(ellipse.RadiusX - radius) < 0.01));
    }

    private static int NodeCount(GraphCanvasHost host) =>
        host.MapPrimitives().Count(drawing => drawing.Geometry is EllipseGeometry && drawing.Brush is not null);

    private static bool IsEmpty(DrawingGroup? drawing) => drawing is null || drawing.Children.Count == 0;
}

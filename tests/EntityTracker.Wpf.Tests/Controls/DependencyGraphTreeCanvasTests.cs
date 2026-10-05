using System.IO;
using System.Windows;
using System.Windows.Media;

using EntityTracker.Domain;
using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf.ViewModels.DependencyGraph;

using static EntityTracker.Wpf.Tests.Controls.GraphCanvasHost;

namespace EntityTracker.Wpf.Tests.Controls;

/// <summary>Drives the real canvas in tree view.</summary>
[Collection(WpfWindowCollection.Name)]
public sealed class DependencyGraphTreeCanvasTests
{
    [Fact]
    public void Tree_DrawsRoundedBoxesWithAStatusBand()
    {
        Run(host =>
        {
            host.Graph.View = DependencyGraphView.Tree;
            IReadOnlyList<GeometryDrawing> drawn = host.MapPrimitives();

            int rounded = drawn.Count(drawing => drawing.Geometry is RectangleGeometry { RadiusX: > 0 } && drawing.Brush is not null);
            int bands = drawn.Count(drawing => drawing.Geometry is RectangleGeometry { RadiusX: 0 } &&
                drawing.Brush is SolidColorBrush { Color: var color } && color == Colors.Gray);
            Assert.True(rounded >= host.Graph.Model.Nodes.Count, $"{rounded} rounded boxes for {host.Graph.Model.Nodes.Count} entities.");
            Assert.True(bands >= host.Graph.Model.Nodes.Count, $"{bands} status bands for {host.Graph.Model.Nodes.Count} entities.");
            Assert.DoesNotContain(drawn, drawing => drawing.Geometry is EllipseGeometry);
        });
    }

    [Fact]
    public void Tree_ClickSelectsDoubleClickOpensDetailsAndBoxesCannotBeDragged()
    {
        List<EntityId> opened = [];
        Run(host =>
        {
            host.Graph.View = DependencyGraphView.Tree;
            DependencyGraphNode node = host.Graph.Model.Nodes[4];
            Point center = host.Canvas.ScreenPositionOf(node);

            host.Click(center);
            Assert.Same(node, host.Graph.SelectedNode);

            host.Canvas.PointerPressed(center, clickCount: 2);
            Assert.Equal([node.EntityId!], opened);

            host.Drag(center, center + new Vector(90, 60));
            Assert.Equal(center, host.Canvas.ScreenPositionOf(node));
        }, openDetails: id => { opened.Add(id); return true; });
    }

    [Fact]
    public void Hover_EmphasizesLinksOnlyWhileNothingIsSelected()
    {
        Run(host =>
        {
            host.Graph.View = DependencyGraphView.Tree;
            DependencyGraphNode hovered = host.Graph.Model.Nodes.First(node =>
                host.Graph.Model.EssentialEdges.Any(edge => ReferenceEquals(edge.To, node)));
            DependencyGraphNode selected = host.Graph.Model.Nodes.First(node => !ReferenceEquals(node, hovered) &&
                !host.Graph.Model.EssentialEdges.Any(edge =>
                    (ReferenceEquals(edge.From, node) || ReferenceEquals(edge.To, node)) &&
                    (ReferenceEquals(edge.From, hovered) || ReferenceEquals(edge.To, hovered))));

            host.Canvas.ShowHover(hovered);
            Assert.False(Emphasized(host).IsEmpty);

            host.Canvas.ShowHover(null);
            host.Graph.SelectedNode = selected;
            Rect selectedOnly = Emphasized(host);
            host.Canvas.ShowHover(hovered);
            Assert.Equal(selectedOnly, Emphasized(host));
        });

        static Rect Emphasized(GraphCanvasHost host) => host.MapPrimitives()
            .Where(drawing => drawing.Pen is { Thickness: 2, Brush: SolidColorBrush { Color: var color } } && color == Colors.Black)
            .Select(drawing => drawing.Geometry.Bounds).Aggregate(Rect.Empty, Rect.Union);
    }

    [Fact]
    public void CtrlClickAddsAndRemovesEntitiesAndEachSelectedBoxIsOutlined()
    {
        Run(host =>
        {
            host.Graph.View = DependencyGraphView.Tree;
            DependencyGraphNode first = host.Graph.Model.Nodes[4];
            DependencyGraphNode second = host.Graph.Model.Nodes[9];
            DependencyGraphNode third = host.Graph.Model.Nodes[14];

            host.Click(host.Canvas.ScreenPositionOf(first));
            host.Click(host.Canvas.ScreenPositionOf(second), ctrl: true);
            host.Click(host.Canvas.ScreenPositionOf(third), ctrl: true);
            Assert.Equal([first, second, third], host.Graph.SelectedNodes);
            Assert.Equal(3, SelectionOutlines(host));

            host.Click(host.Canvas.ScreenPositionOf(second), ctrl: true);
            Assert.Equal([first, third], host.Graph.SelectedNodes);

            host.Click(new Point(4, 4), ctrl: true);
            Assert.Equal([first, third], host.Graph.SelectedNodes);

            host.Click(host.Canvas.ScreenPositionOf(first));
            Assert.Equal([first], host.Graph.SelectedNodes);
            host.Click(host.Canvas.ScreenPositionOf(first));
            Assert.False(host.Graph.HasSelection);
        });

        static int SelectionOutlines(GraphCanvasHost host) =>
            host.MapPrimitives().Count(drawing => drawing.Pen is { Thickness: 2.5 });
    }

    [Fact]
    public void Tree_FitToViewShowsEveryBox()
    {
        Run(host =>
        {
            host.Graph.View = DependencyGraphView.Tree;
            host.Canvas.PointerWheel(new Point(400, 300), 1200);
            host.Canvas.FitToView();

            Rect bounds = new(0, 0, host.Canvas.ActualWidth, host.Canvas.ActualHeight);
            Vector half = new(TreeDependencyLayout.BoxWidth / 2 * host.Canvas.Scale,
                TreeDependencyLayout.BoxHeight / 2 * host.Canvas.Scale);
            Assert.All(host.Graph.Model.Nodes, node =>
            {
                Point center = host.Canvas.ScreenPositionOf(node);
                Assert.True(bounds.Contains(center - half) && bounds.Contains(center + half));
            });
        });
    }

    [Fact]
    public void SwitchingBackRestoresTheSolarSystemArrangement()
    {
        Run(host =>
        {
            (double X, double Y)[] before = host.Graph.Model.Nodes.Select(node => (node.X, node.Y)).ToArray();

            host.Graph.View = DependencyGraphView.Tree;
            host.Drag(new Point(6, 6), new Point(80, 50));
            host.Graph.View = DependencyGraphView.SolarSystem;

            Assert.Equal(before, host.Graph.Model.Nodes.Select(node => (node.X, node.Y)));
            Assert.Contains(host.MapPrimitives(), drawing => drawing.Geometry is EllipseGeometry);
        });
    }

    [Fact]
    public void Tree_ExportsAsPng()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tree-{Guid.NewGuid():N}.png");
        try
        {
            Run(host =>
            {
                host.Graph.View = DependencyGraphView.Tree;
                host.Canvas.SavePng(path);
            });

            byte[] header = File.ReadAllBytes(path).Take(8).ToArray();
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, header);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

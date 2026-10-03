using System.Reflection;

using EntityTracker.Wpf.Controls;
using EntityTracker.Wpf.ViewModels.DependencyGraph;

using static EntityTracker.Wpf.Tests.Controls.GraphCanvasHost;

namespace EntityTracker.Wpf.Tests.Controls;

/// <summary>Runs the real canvas in an off-screen window, since the rotation lives in its frame loop.</summary>
[Collection(WpfWindowCollection.Name)]
public sealed class DependencyGraphRotationTests
{
    [Fact]
    public void Rotation_TurnsPausesAfterAnInteractionAndResumes()
    {
        Run(host =>
        {
            DependencyGraphCanvas canvas = host.Canvas;
            Pump(1.5);
            Assert.True(Angle(canvas) > 0.5, $"The map should turn on its own ({Angle(canvas):0.0}°).");

            Invoke(canvas, "MarkInteraction");
            double paused = Angle(canvas);
            Pump(2);
            Assert.Equal(paused, Angle(canvas), 3);

            Pump(3);
            Assert.True(Angle(canvas) - paused > 0.5, "The map should resume turning after the pause.");

            host.Graph.IsAnimationEnabled = false;
            double stopped = Angle(canvas);
            Pump(1);
            Assert.Equal(stopped, Angle(canvas), 3);
        }, animate: true);
    }

    [Fact]
    public void Rotation_HoldsStillWhileHoveringAnEntity()
    {
        Run(host =>
        {
            Pump(0.5);
            DependencyGraphNode node = host.Graph.Model.Nodes[^1];
            host.Canvas.PointerMoved(host.Canvas.ScreenPositionOf(node));
            Assert.Same(node, host.Canvas.HoveredNode);
            double hovered = Angle(host.Canvas);
            Pump(1.5);
            Assert.Equal(hovered, Angle(host.Canvas), 3);
        }, animate: true);
    }

    [Fact]
    public void Rotation_HoldsStillWhileSomethingIsSelected()
    {
        Run(host =>
        {
            Pump(0.5);
            host.Graph.SelectedNode = host.Graph.Model.Nodes[^1];
            double selected = Angle(host.Canvas);
            Pump(1.5);
            Assert.Equal(selected, Angle(host.Canvas), 3);
            Assert.False(host.Canvas.IsAnimating);
        }, animate: true);
    }

    private static double Angle(DependencyGraphCanvas canvas) =>
        (double)typeof(DependencyGraphCanvas).GetField("_angle", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(canvas)! * 180 / Math.PI;

    private static void Invoke(DependencyGraphCanvas canvas, string method) =>
        typeof(DependencyGraphCanvas).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(canvas, null);
}

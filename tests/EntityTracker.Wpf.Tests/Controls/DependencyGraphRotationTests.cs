using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;

using EntityTracker.Application.Ranking;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using EntityTracker.Wpf.Controls;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.Controls;

/// <summary>Runs the real canvas in an off-screen window, since the rotation lives in its frame loop.</summary>
public sealed class DependencyGraphRotationTests
{
    [Fact]
    public void Rotation_TurnsPausesAfterAnInteractionAndResumes()
    {
        RunOnStaThread(() =>
        {
            DependencyGraphViewModel graph = new(_ => true);
            graph.Rebuild(Rows());
            DependencyGraphCanvas canvas = new() { Graph = graph };
            Window window = new()
            {
                Width = 800, Height = 600, Left = -10000, Top = -10000,
                ShowInTaskbar = false, WindowStyle = WindowStyle.None, Content = canvas
            };
            window.Show();
            try
            {
                Pump(1.5);
                Assert.True(Angle(canvas) > 0.5, $"The map should turn on its own ({Angle(canvas):0.0}°).");

                Invoke(canvas, "MarkInteraction");
                double paused = Angle(canvas);
                Pump(2);
                Assert.Equal(paused, Angle(canvas), 3);

                Pump(3);
                Assert.True(Angle(canvas) - paused > 0.5, "The map should resume turning after the pause.");

                graph.IsAnimationEnabled = false;
                double stopped = Angle(canvas);
                Pump(1);
                Assert.Equal(stopped, Angle(canvas), 3);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static double Angle(DependencyGraphCanvas canvas) =>
        (double)typeof(DependencyGraphCanvas).GetField("_angle", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(canvas)! * 180 / Math.PI;

    private static void Invoke(DependencyGraphCanvas canvas, string method) =>
        typeof(DependencyGraphCanvas).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(canvas, null);

    private static void Pump(double seconds)
    {
        DispatcherFrame frame = new();
        DispatcherTimer stop = new(TimeSpan.FromSeconds(seconds), DispatcherPriority.Normal,
            (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        stop.Start();
        Dispatcher.PushFrame(frame);
        stop.Stop();
    }

    private static void RunOnStaThread(Action test)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try { test(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static EntityOverviewRow[] Rows() => Enumerable.Range(1, 30).Select(id => new EntityOverviewRow(
        new EntityId(new Guid(id, 0, 0, new byte[8])), EntityLifecycleState.Active,
        DevelopmentStatus.NotStarted, EntityWorkflowState.Ready, DependencyResolutionState.Resolved,
        "—", "—", $"e{id}", "", "", "CSV", "", "", "", id == 1 ? [] : [$"e{(id - 1) / 2 + 1}"], [],
        "", "", "", "—", "", "")).ToArray();
}

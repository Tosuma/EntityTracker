using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

using EntityTracker.Application.Ranking;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using EntityTracker.Wpf.Controls;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.Controls;

/// <summary>
/// Runs a real <see cref="DependencyGraphCanvas"/> in an off-screen window on its own STA thread,
/// so tests can drive pointer input and inspect what is drawn.
/// </summary>
internal sealed class GraphCanvasHost
{
    private GraphCanvasHost(DependencyGraphCanvas canvas, DependencyGraphViewModel graph, Window window)
    {
        Canvas = canvas;
        Graph = graph;
        Window = window;
    }

    public DependencyGraphCanvas Canvas { get; }
    public DependencyGraphViewModel Graph { get; }
    public Window Window { get; }

    public static void Run(Action<GraphCanvasHost> test, bool animate = false,
        Func<EntityId, bool>? openDetails = null, IReadOnlyList<EntityOverviewRow>? rows = null)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                DependencyGraphViewModel graph = new(openDetails ?? (_ => true)) { IsAnimationEnabled = animate };
                graph.Rebuild(rows ?? Rows(30));
                DependencyGraphCanvas canvas = new()
                {
                    Graph = graph,
                    Background = Brushes.White,
                    EdgeBrush = Brushes.Gray,
                    LabelBrush = Brushes.Black,
                    HighlightBrush = Brushes.Black,
                    PlaceholderBrush = Brushes.Gray,
                    CardBackground = Brushes.White
                };
                Window window = new()
                {
                    Width = 800, Height = 600, Left = -10000, Top = -10000,
                    ShowInTaskbar = false, WindowStyle = WindowStyle.None, Content = canvas,
                    ShowActivated = false
                };
                window.Show();
                try
                {
                    Pump(0.2);
                    canvas.FitToView();
                    test(new GraphCanvasHost(canvas, graph, window));
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    /// <summary>Lets WPF process input, timers and rendering for the given time.</summary>
    public static void Pump(double seconds)
    {
        DispatcherFrame frame = new();
        DispatcherTimer stop = new(TimeSpan.FromSeconds(seconds), DispatcherPriority.Normal,
            (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        stop.Start();
        Dispatcher.PushFrame(frame);
        stop.Stop();
    }

    public void Click(Point point, int clickCount = 1, bool ctrl = false)
    {
        Canvas.PointerPressed(point, clickCount, ctrl);
        Canvas.PointerReleased();
    }

    public void Drag(Point from, Point to, int steps = 6)
    {
        Canvas.PointerPressed(from, 1);
        for (int step = 1; step <= steps; step++)
            Canvas.PointerMoved(from + (to - from) * step / steps);
        Canvas.PointerReleased();
    }

    /// <summary>Gets every primitive drawn into the cached map layer.</summary>
    public IReadOnlyList<GeometryDrawing> MapPrimitives()
    {
        List<GeometryDrawing> found = [];
        void Walk(Drawing? drawing)
        {
            switch (drawing)
            {
                case DrawingGroup group:
                    foreach (Drawing child in group.Children) Walk(child);
                    break;
                case GeometryDrawing geometry:
                    found.Add(geometry);
                    break;
            }
        }

        Walk(Canvas.MapDrawing);
        return found;
    }

    /// <summary>A small tree: e1 is the foundation, every other entity depends on its "parent".</summary>
    public static EntityOverviewRow[] Rows(int count) => Enumerable.Range(1, count).Select(id => Row(id,
        id == 1 ? [] : [$"e{(id - 1) / 2 + 1}"])).ToArray();

    public static EntityOverviewRow Row(int id, params string[] dependencies) => new(
        new EntityId(new Guid(id, 0, 0, new byte[8])), EntityLifecycleState.Active,
        DevelopmentStatus.NotStarted, EntityWorkflowState.Ready, DependencyResolutionState.Resolved,
        "—", "—", $"e{id}", "", "", "CSV", "", "", "", dependencies, [],
        "", "", "", "—", "", "");
}

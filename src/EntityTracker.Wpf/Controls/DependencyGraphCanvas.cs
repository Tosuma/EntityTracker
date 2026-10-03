using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using EntityTracker.Domain;
using EntityTracker.Wpf.ViewModels;
using EntityTracker.Wpf.ViewModels.DependencyGraph;

namespace EntityTracker.Wpf.Controls;

/// <summary>
/// Draws the dependency map and handles pan, zoom, node dragging, selection and double-click.
/// The map is rasterised once into a cached layer; panning and zooming only move or scale that
/// layer, and the map is redrawn sharply once the view has been still for a moment. Without this,
/// every pan step re-rasterised every visible line, dot and name, which made panning over the
/// entities stutter.
/// </summary>
public sealed class DependencyGraphCanvas : FrameworkElement
{
    public static readonly DependencyProperty GraphProperty = DependencyProperty.Register(
        nameof(Graph), typeof(DependencyGraphViewModel), typeof(DependencyGraphCanvas),
        new PropertyMetadata(null, OnGraphChanged));

    public static readonly DependencyProperty BackgroundProperty = RegisterBrush(nameof(Background));
    public static readonly DependencyProperty LabelBrushProperty = RegisterBrush(nameof(LabelBrush));
    public static readonly DependencyProperty EdgeBrushProperty = RegisterBrush(nameof(EdgeBrush));
    public static readonly DependencyProperty HighlightBrushProperty = RegisterBrush(nameof(HighlightBrush));
    public static readonly DependencyProperty PlaceholderBrushProperty = RegisterBrush(nameof(PlaceholderBrush));
    public static readonly DependencyProperty CardBackgroundProperty = RegisterBrush(nameof(CardBackground));

    private const double MinScale = 0.15;
    private const double MaxScale = 4;
    private const double LabelScale = 1.1;
    private const double DimOpacity = 0.12;
    private const double RestingLinkOpacity = 0.25;
    private const double OrbitOpacity = 0.3;
    private const double DragThreshold = 3;
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan ResumeDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FullTurn = TimeSpan.FromMinutes(4);
    private const double RedrawAfterRotation = 1.5 * Math.PI / 180;

    private readonly Dictionary<(string Text, bool Bold, bool Dimmed), FormattedText> _labelCache = [];
    private readonly Dictionary<DevelopmentStatus, Brush> _statusBrushes = [];
    private readonly Dictionary<(Brush Brush, double Opacity), Brush> _fadedBrushes = [];
    private readonly Dictionary<(Brush Brush, double Thickness, bool Dashed), Pen> _pens = [];
    private double _scale = 1;
    private Vector _offset;
    private double _angle;
    private bool _needsFit = true;
    private bool _isAnimating;
    private DependencyGraphNode? _hoverNode;
    private DependencyGraphNode? _dragNode;
    private Point _pressPoint;
    private Point _lastPoint;
    private bool _isPanning;
    private bool _hasMoved;
    private readonly DrawingVisual _content = new();
    private readonly DrawingVisual _overlay = new();
    private readonly MatrixTransform _contentTransform = new();
    private readonly DispatcherTimer _settleTimer;
    private DependencyGraphView _renderedView = new(1, default, 0);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _resumeTimer;
    private TimeSpan _lastFrame;
    private TimeSpan _lastInteraction = -ResumeDelay;

    public DependencyGraphCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        _content.Transform = _contentTransform;
        _content.CacheMode = new BitmapCache { SnapsToDevicePixels = true };
        AddVisualChild(_content);
        AddVisualChild(_overlay);
        _settleTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = SettleDelay };
        _settleTimer.Tick += (_, _) => Redraw();
        _resumeTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = ResumeDelay };
        _resumeTimer.Tick += (_, _) => OnResumeTimerTick();
        Loaded += (_, _) => UpdateAnimation();
        Unloaded += (_, _) => { StopAnimation(); _settleTimer.Stop(); _resumeTimer.Stop(); };
        SizeChanged += (_, _) => { if (_needsFit) FitToView(); else Redraw(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) { if (_needsFit) FitToView(); else Redraw(); UpdateAnimation(); } };
    }

    protected override int VisualChildrenCount => 2;

    protected override Visual GetVisualChild(int index) => index switch
    {
        0 => _content,
        1 => _overlay,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    public DependencyGraphViewModel? Graph
    {
        get => (DependencyGraphViewModel?)GetValue(GraphProperty);
        set => SetValue(GraphProperty, value);
    }

    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public Brush? LabelBrush
    {
        get => (Brush?)GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public Brush? EdgeBrush
    {
        get => (Brush?)GetValue(EdgeBrushProperty);
        set => SetValue(EdgeBrushProperty, value);
    }

    public Brush? HighlightBrush
    {
        get => (Brush?)GetValue(HighlightBrushProperty);
        set => SetValue(HighlightBrushProperty, value);
    }

    public Brush? PlaceholderBrush
    {
        get => (Brush?)GetValue(PlaceholderBrushProperty);
        set => SetValue(PlaceholderBrushProperty, value);
    }

    /// <summary>Gets or sets the opaque background of the hover card, so the map never shows through it.</summary>
    public Brush? CardBackground
    {
        get => (Brush?)GetValue(CardBackgroundProperty);
        set => SetValue(CardBackgroundProperty, value);
    }

    /// <summary>Gets the current zoom factor; exposed for tests and screenshots.</summary>
    public double Scale => _scale;

    public void FitToView()
    {
        DependencyGraphViewModel? graph = Graph;
        if (graph is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            _needsFit = true;
            return;
        }

        DependencyGraphNode[] nodes = graph.Model.Nodes.Where(graph.IsVisible).ToArray();
        if (nodes.Length == 0)
        {
            _needsFit = true;
            Redraw();
            return;
        }

        const double padding = 40;
        if (graph.IsAnimationEnabled)
        {
            // While the map turns, fit the whole circle so rotation never carries entities out of view.
            double reach = nodes.Max(static node => Math.Sqrt(node.X * node.X + node.Y * node.Y) + node.Radius) + 18;
            double size = Math.Min(ActualWidth, ActualHeight) - padding * 2;
            _scale = Math.Clamp(size / Math.Max(reach * 2, 1), MinScale, 2);
            _offset = new Vector(ActualWidth / 2, ActualHeight / 2);
        }
        else
        {
            DependencyGraphView turned = new(1, default, _angle);
            Point[] points = nodes.Select(node => turned.ToScreen(node.X, node.Y)).ToArray();
            double left = points.Zip(nodes, static (point, node) => point.X - node.Radius).Min();
            double right = points.Zip(nodes, static (point, node) => point.X + node.Radius).Max();
            double top = points.Zip(nodes, static (point, node) => point.Y - node.Radius).Min();
            double bottom = points.Zip(nodes, static (point, node) => point.Y + node.Radius + 18).Max();
            double scale = Math.Min(
                (ActualWidth - padding * 2) / Math.Max(right - left, 1),
                (ActualHeight - padding * 2) / Math.Max(bottom - top, 1));
            _scale = Math.Clamp(scale, MinScale, 2);
            _offset = new Vector(
                ActualWidth / 2 - (left + right) / 2 * _scale,
                ActualHeight / 2 - (top + bottom) / 2 * _scale);
        }

        _needsFit = false;
        Redraw();
    }

    public void CenterOn(DependencyGraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _scale = Math.Max(_scale, 1);
        _offset = View.OffsetKeeping(new Point(node.X, node.Y), new Point(ActualWidth / 2, ActualHeight / 2));
        _needsFit = false;
        Redraw();
    }

    /// <summary>Writes the current view, including its background, as a PNG file.</summary>
    public void SavePng(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        int width = Math.Max(1, (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX));
        int height = Math.Max(1, (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY));
        DrawingVisual visual = new();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRectangle(Background ?? Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
            if (Graph is { } graph) DrawScene(context, graph, area: null);
        }

        RenderTargetBitmap bitmap = new(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>Shows the hover card for an entity, as if the pointer rested on it; used for screenshots.</summary>
    internal void ShowHover(DependencyGraphNode? node)
    {
        _hoverNode = node;
        Redraw();
    }

    /// <summary>Draws only the background; the map lives in the cached child layer.</summary>
    protected override void OnRender(DrawingContext context) =>
        context.DrawRectangle(Background ?? Brushes.Transparent, null, new Rect(RenderSize));

    /// <summary>
    /// Re-renders the map for the current view into the cached layer and resets its transform.
    /// Only the visible area plus a margin is drawn, which keeps the cached bitmap small.
    /// </summary>
    private void Redraw()
    {
        _settleTimer.Stop();
        DependencyGraphViewModel? graph = Graph;
        using (DrawingContext context = _content.RenderOpen())
        {
            if (graph is not null && ActualWidth > 0 && ActualHeight > 0)
            {
                Rect area = new(0, 0, ActualWidth, ActualHeight);
                area.Inflate(ActualWidth / 2, ActualHeight / 2);
                context.PushClip(new RectangleGeometry(area));
                DrawScene(context, graph, area);
                context.Pop();
            }
        }

        _renderedView = View;
        _contentTransform.Matrix = Matrix.Identity;
        using DrawingContext overlay = _overlay.RenderOpen();
        if (graph is not null && _hoverNode is not null && !IsMouseCaptured && graph.IsVisible(_hoverNode))
            DrawHoverCard(overlay, graph, _hoverNode);
    }

    /// <summary>
    /// Follows a pan or zoom by moving and scaling the cached map instead of redrawing it, then
    /// schedules a sharp redraw for when the view stops changing.
    /// </summary>
    private void ViewChanged()
    {
        _contentTransform.Matrix = View.LayerMatrix(_renderedView);
        _overlay.RenderOpen().Close();
        _settleTimer.Stop();
        _settleTimer.Start();
    }

    /// <summary>
    /// Draws the map. Faint and dimmed parts use brushes with the opacity built in, and lines are
    /// batched into one geometry per style, because per-element opacity layers make WPF render
    /// every line and dot to its own off-screen surface, which is what made panning stutter.
    /// </summary>
    private void DrawScene(DrawingContext context, DependencyGraphViewModel graph, Rect? area)
    {
        bool hasSelection = graph.HasSelection;
        Brush edgeBrush = EdgeBrush ?? Brushes.Gray;
        Brush highlightBrush = HighlightBrush ?? Brushes.Black;

        // Orbit guides: foundations in the centre, each ring one more layer of dependencies.
        Pen orbitPen = CachedPen(Faded(edgeBrush, OrbitOpacity), 1);
        Point origin = new(_offset.X, _offset.Y);
        foreach (double ring in graph.Layout.RingRadii)
            context.DrawEllipse(null, orbitPen, origin, ring * _scale, ring * _scale);

        HashSet<DependencyGraphNode> hoverNeighbours = new(ReferenceEqualityComparer.Instance);
        StreamGeometry faint = new();
        StreamGeometry emphasized = new();
        using (StreamGeometryContext faintLines = faint.Open())
        using (StreamGeometryContext strongLines = emphasized.Open())
        {
            foreach (DependencyGraphEdge edge in graph.Model.EssentialEdges)
            {
                if (!graph.IsVisible(edge)) continue;
                if (area is { } visibleArea && !new Rect(ToScreen(edge.From), ToScreen(edge.To)).IntersectsWith(visibleArea))
                    continue;
                bool hovered = _hoverNode is not null &&
                    (ReferenceEquals(edge.From, _hoverNode) || ReferenceEquals(edge.To, _hoverNode));
                if (hovered)
                {
                    hoverNeighbours.Add(edge.From);
                    hoverNeighbours.Add(edge.To);
                }

                AddEdge(hovered || graph.HighlightedEdges.Contains(edge) ? strongLines : faintLines, edge);
            }
        }

        faint.Freeze();
        emphasized.Freeze();
        context.DrawGeometry(null, CachedPen(Faded(edgeBrush, hasSelection ? DimOpacity : RestingLinkOpacity), 1), faint);
        context.DrawGeometry(null, CachedPen(highlightBrush, 2), emphasized);

        // Dimmed entities first, so the highlighted ones are drawn on top of them.
        List<DependencyGraphNode> dimmed = [];
        List<DependencyGraphNode> normal = [];
        foreach (DependencyGraphNode node in graph.Model.Nodes)
        {
            if (!graph.IsVisible(node) || !IsInArea(node, area)) continue;
            bool isDimmed = hasSelection && !graph.HighlightedNodes.Contains(node) && !hoverNeighbours.Contains(node);
            (isDimmed ? dimmed : normal).Add(node);
        }

        foreach (DependencyGraphNode node in dimmed) DrawNode(context, graph, node, DimOpacity);
        foreach (DependencyGraphNode node in normal) DrawNode(context, graph, node, 1);

        DrawLabels(context, graph, hoverNeighbours, area);
    }

    private bool IsInArea(DependencyGraphNode node, Rect? area)
    {
        if (area is not { } visibleArea) return true;
        double margin = Math.Max(node.Radius * _scale, 2.5) + 200;
        visibleArea.Inflate(margin, margin);
        return visibleArea.Contains(ToScreen(node));
    }

    private void DrawNode(DrawingContext context, DependencyGraphViewModel graph, DependencyGraphNode node,
        double opacity)
    {
        Brush edgeBrush = EdgeBrush ?? Brushes.Gray;
        Brush highlightBrush = HighlightBrush ?? Brushes.Black;
        Point center = ToScreen(node);
        double radius = Math.Max(node.Radius * _scale, 2.5);
        if (node.IsPlaceholder)
        {
            context.DrawEllipse(Background, CachedPen(Faded(PlaceholderBrush ?? Brushes.Gray, opacity), 1.5, dashed: true),
                center, radius, radius);
        }
        else
        {
            context.DrawEllipse(Faded(StatusBrush(node.Status), opacity), CachedPen(Faded(edgeBrush, opacity), 1),
                center, radius, radius);
        }

        if (ReferenceEquals(node, graph.SelectedNode))
            context.DrawEllipse(null, CachedPen(highlightBrush, 2.5), center, radius + 4, radius + 4);
        else if (graph.HighlightedNodes.Contains(node))
            context.DrawEllipse(null, CachedPen(Faded(highlightBrush, opacity), 1.5), center, radius + 2.5, radius + 2.5);
    }

    /// <summary>
    /// Draws names on top of the map. Landmarks keep the zoomed-out map readable; names that
    /// would overlap a more important one are skipped.
    /// </summary>
    private void DrawLabels(DrawingContext context, DependencyGraphViewModel graph,
        HashSet<DependencyGraphNode> hoverNeighbours, Rect? area)
    {
        bool hasSelection = graph.HasSelection;
        List<DependencyGraphLabelCandidate<FormattedText>> candidates = [];
        foreach (DependencyGraphNode node in graph.Model.Nodes)
        {
            if (!graph.IsVisible(node) || !IsInArea(node, area)) continue;
            bool highlighted = graph.HighlightedNodes.Contains(node);
            bool landmark = graph.Landmarks.Contains(node);
            int tier = ReferenceEquals(node, _hoverNode) ? 0
                : ReferenceEquals(node, graph.SelectedNode) ? 1
                : hoverNeighbours.Contains(node) ? 2
                : hasSelection && highlighted ? 3
                : landmark ? 4
                : _scale >= LabelScale ? 5
                : -1;
            if (tier < 0) continue;
            bool dimmed = hasSelection && !highlighted && !hoverNeighbours.Contains(node);
            FormattedText text = Label(node.Label, landmark, dimmed);
            Point center = ToScreen(node);
            double radius = Math.Max(node.Radius * _scale, 2.5);
            Rect bounds = new(center.X - text.Width / 2, center.Y + radius + 3, text.Width, text.Height);
            candidates.Add(new(text, bounds, tier * 100_000 - node.DependentCount, tier <= 1));
        }

        foreach (DependencyGraphLabelCandidate<FormattedText> label in DependencyGraphLabelPlacer.Place(candidates))
            context.DrawText(label.Item, label.Bounds.TopLeft);
    }

    /// <summary>Draws a small card explaining the hovered entity's place on the map.</summary>
    private void DrawHoverCard(DrawingContext context, DependencyGraphViewModel graph, DependencyGraphNode node)
    {
        DependencyGraphNodeInfo info = graph.Describe(node);
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        Typeface regular = new(TextElementFontFamily(), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        Typeface bold = new(TextElementFontFamily(), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        FormattedText title = new(info.Title, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            bold, 13, LabelBrush ?? Brushes.Black, pixelsPerDip);
        FormattedText[] lines = info.Lines.Select(line => new FormattedText(line, CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, regular, 12, PlaceholderBrush ?? Brushes.Gray, pixelsPerDip)).ToArray();

        const double padding = 10;
        const double gap = 3;
        double width = Math.Max(title.Width, lines.Select(static line => line.Width).DefaultIfEmpty(0).Max()) + padding * 2;
        double height = title.Height + lines.Sum(static line => line.Height + gap) + padding * 2;
        Point center = ToScreen(node);
        double radius = Math.Max(node.Radius * _scale, 2.5);
        double x = center.X + radius + 12;
        if (x + width > ActualWidth - 8) x = center.X - radius - 12 - width;
        x = Math.Clamp(x, 8, Math.Max(8, ActualWidth - width - 8));
        double y = Math.Clamp(center.Y - height / 2, 8, Math.Max(8, ActualHeight - height - 8));

        Pen border = Freeze(new Pen(EdgeBrush ?? Brushes.Gray, 1));
        context.DrawRoundedRectangle(CardBackground ?? Background ?? Brushes.White, border,
            new Rect(x, y, width, height), 6, 6);
        double cursor = y + padding;
        context.DrawText(title, new Point(x + padding, cursor));
        cursor += title.Height + gap;
        foreach (FormattedText line in lines)
        {
            context.DrawText(line, new Point(x + padding, cursor));
            cursor += line.Height + gap;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        DependencyGraphViewModel? graph = Graph;
        if (graph is null) return;
        Point point = e.GetPosition(this);
        DependencyGraphNode? node = HitTestNode(point);
        if (node is not null && e.ClickCount == 2)
        {
            graph.SelectedNode = node;
            graph.OpenDetails(node);
            e.Handled = true;
            return;
        }

        MarkInteraction();
        _pressPoint = _lastPoint = point;
        _hasMoved = false;
        _dragNode = node;
        _isPanning = node is null;
        if (node is not null) node.IsPinned = true;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point point = e.GetPosition(this);
        if (IsMouseCaptured && (_dragNode is not null || _isPanning))
        {
            if (!_hasMoved && (point - _pressPoint).Length < DragThreshold) return;
            _hasMoved = true;
            MarkInteraction();
            if (_dragNode is not null)
            {
                Point world = ToWorld(point);
                _dragNode.X = world.X;
                _dragNode.Y = world.Y;
                Graph?.Layout.Reheat();
                UpdateAnimation();
                Redraw();
            }
            else
            {
                _offset += point - _lastPoint;
                ViewChanged();
            }

            _lastPoint = point;
            return;
        }

        DependencyGraphNode? hover = HitTestNode(point);
        if (!ReferenceEquals(hover, _hoverNode))
        {
            _hoverNode = hover;
            Cursor = hover is null ? null : Cursors.Hand;
            MarkInteraction();
            Redraw();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!IsMouseCaptured) return;
        DependencyGraphViewModel? graph = Graph;
        if (graph is not null && !_hasMoved)
            graph.SelectedNode = _dragNode;
        if (_dragNode is not null)
        {
            _dragNode.IsPinned = false;

            // Only a real drag wakes the layout. A plain click must leave the map still, or the
            // entity drifts away from the pointer before the second click of a double-click.
            if (_hasMoved)
            {
                graph?.Layout.MoveAnchor(_dragNode);
                graph?.Layout.Reheat();
                UpdateAnimation();
            }
        }

        _dragNode = null;
        _isPanning = false;
        ReleaseMouseCapture();
        MarkInteraction();
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverNode is null) return;
        _hoverNode = null;
        MarkInteraction();
        Redraw();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        MarkInteraction();
        ZoomAt(e.GetPosition(this), Math.Pow(1.0015, e.Delta));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        Point center = new(ActualWidth / 2, ActualHeight / 2);
        switch (e.Key)
        {
            case Key.Escape when Graph?.HasSelection == true:
                Graph.SelectedNode = null;
                break;
            case Key.OemPlus or Key.Add:
                ZoomAt(center, 1.2);
                break;
            case Key.OemMinus or Key.Subtract:
                ZoomAt(center, 1 / 1.2);
                break;
            case Key.D0 or Key.NumPad0:
                FitToView();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property.PropertyType != typeof(Brush)) return;

        // A theme change swaps the brushes, so everything derived from them is rebuilt.
        _labelCache.Clear();
        _statusBrushes.Clear();
        _fadedBrushes.Clear();
        _pens.Clear();
        Redraw();
    }

    private static DependencyProperty RegisterBrush(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(DependencyGraphCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static void OnGraphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        DependencyGraphCanvas canvas = (DependencyGraphCanvas)d;
        if (e.OldValue is DependencyGraphViewModel oldGraph)
        {
            oldGraph.PropertyChanged -= canvas.OnGraphPropertyChanged;
            oldGraph.VisualStateChanged -= canvas.OnGraphVisualStateChanged;
            oldGraph.CenterOnRequested -= canvas.OnCenterOnRequested;
            oldGraph.FitRequested -= canvas.OnFitRequested;
            oldGraph.PngWriter = null;
        }

        if (e.NewValue is DependencyGraphViewModel newGraph)
        {
            newGraph.PropertyChanged += canvas.OnGraphPropertyChanged;
            newGraph.VisualStateChanged += canvas.OnGraphVisualStateChanged;
            newGraph.CenterOnRequested += canvas.OnCenterOnRequested;
            newGraph.FitRequested += canvas.OnFitRequested;
            newGraph.PngWriter = canvas.SavePng;
        }

        canvas._hoverNode = null;
        canvas.FitToView();
        canvas.UpdateAnimation();
    }

    private void OnGraphPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DependencyGraphViewModel.Model)) return;
        _hoverNode = null;
        _dragNode = null;
        if (_needsFit) FitToView();
        UpdateAnimation();
        Redraw();
    }

    private void OnGraphVisualStateChanged(object? sender, EventArgs e)
    {
        Redraw();
        UpdateAnimation();
    }

    private void OnCenterOnRequested(object? sender, DependencyGraphNode node) => CenterOn(node);

    private void OnFitRequested(object? sender, EventArgs e) => FitToView();

    /// <summary>
    /// Starts the frame loop while the layout is still settling or the map may rotate, and stops
    /// it otherwise, so an idle map costs nothing.
    /// </summary>
    private void UpdateAnimation()
    {
        bool needed = IsLoaded && IsVisible && (IsSettling || ShouldRotate);
        if (needed == _isAnimating) return;
        if (!needed)
        {
            StopAnimation();
            return;
        }

        _isAnimating = true;
        _lastFrame = _clock.Elapsed;
        CompositionTarget.Rendering += OnRendering;
    }

    private bool IsSettling => Graph?.Layout.IsSettled == false;

    /// <summary>
    /// Gets whether the map may turn: the setting is on and nobody is hovering, dragging,
    /// panning, zooming or looking at a selection. The app setting alone decides; Windows'
    /// animation-effects switch is off on many machines and would silently stop the rotation.
    /// </summary>
    private bool ShouldRotate =>
        Graph is { IsAnimationEnabled: true, HasSelection: false, HasNodes: true } &&
        _hoverNode is null && !IsMouseCaptured &&
        _clock.Elapsed - _lastInteraction >= ResumeDelay;

    /// <summary>
    /// Resumes the rotation once the pause is over. Timers can fire a few milliseconds early, so
    /// the remaining time is checked and the timer re-armed instead of giving up.
    /// </summary>
    private void OnResumeTimerTick()
    {
        _resumeTimer.Stop();
        TimeSpan remaining = ResumeDelay - (_clock.Elapsed - _lastInteraction);
        if (remaining > TimeSpan.Zero)
        {
            _resumeTimer.Interval = remaining + TimeSpan.FromMilliseconds(20);
            _resumeTimer.Start();
            return;
        }

        _resumeTimer.Interval = ResumeDelay;
        UpdateAnimation();
    }

    /// <summary>Pauses the rotation; it resumes once the map has been left alone for a moment.</summary>
    private void MarkInteraction()
    {
        _lastInteraction = _clock.Elapsed;
        _resumeTimer.Stop();
        _resumeTimer.Interval = ResumeDelay;
        _resumeTimer.Start();
        if (!IsSettling) StopAnimation();
    }

    private void StopAnimation()
    {
        if (!_isAnimating) return;
        _isAnimating = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        DependencyGraphViewModel? graph = Graph;
        TimeSpan now = _clock.Elapsed;
        double elapsed = Math.Min((now - _lastFrame).TotalSeconds, 0.1);
        _lastFrame = now;
        if (graph is null || !IsVisible)
        {
            StopAnimation();
            return;
        }

        bool redraw = false;
        if (IsSettling)
        {
            graph.Layout.Step();
            redraw = true;
        }

        if (ShouldRotate)
        {
            _angle = (_angle + elapsed * 2 * Math.PI / FullTurn.TotalSeconds) % (2 * Math.PI);
            // The GPU turns the cached map; names are redrawn upright every few degrees.
            if (Math.Abs(Math.IEEERemainder(_angle - _renderedView.Angle, 2 * Math.PI)) > RedrawAfterRotation)
                redraw = true;
            else if (!redraw)
                _contentTransform.Matrix = View.LayerMatrix(_renderedView);
        }

        if (redraw) Redraw();
        if (!IsSettling && !ShouldRotate && _dragNode is null) StopAnimation();
    }

    private void ZoomAt(Point anchor, double factor)
    {
        double scale = Math.Clamp(_scale * factor, MinScale, MaxScale);
        Point world = ToWorld(anchor);
        _scale = scale;
        _offset = View.OffsetKeeping(world, anchor);
        _needsFit = false;
        ViewChanged();
    }

    /// <summary>Draws a plain line between the two node edges; the orbits already show direction.</summary>
    private void AddEdge(StreamGeometryContext lines, DependencyGraphEdge edge)
    {
        Point from = ToScreen(edge.From);
        Point to = ToScreen(edge.To);
        Vector direction = to - from;
        double length = direction.Length;
        double fromRadius = Math.Max(edge.From.Radius * _scale, 2.5);
        double toRadius = Math.Max(edge.To.Radius * _scale, 2.5);
        if (length <= fromRadius + toRadius) return;
        direction /= length;
        lines.BeginFigure(from + direction * fromRadius, false, false);
        lines.LineTo(to - direction * toRadius, true, false);
    }

    private DependencyGraphNode? HitTestNode(Point point)
    {
        DependencyGraphViewModel? graph = Graph;
        if (graph is null) return null;
        IReadOnlyList<DependencyGraphNode> nodes = graph.Model.Nodes;
        for (int index = nodes.Count - 1; index >= 0; index--)
        {
            DependencyGraphNode node = nodes[index];
            if (!graph.IsVisible(node)) continue;
            double radius = Math.Max(node.Radius * _scale, 2.5) + 3;
            if ((ToScreen(node) - point).LengthSquared <= radius * radius) return node;
        }

        return null;
    }

    private Brush StatusBrush(DevelopmentStatus? nullableStatus)
    {
        if (nullableStatus is not { } status) return Brushes.Gray;
        if (_statusBrushes.TryGetValue(status, out Brush? cached)) return cached;
        Brush brush = TryFindResource($"Brush.Status.{status}") as Brush ?? Brushes.Gray;
        _statusBrushes[status] = brush;
        return brush;
    }

    /// <summary>Gets a frozen copy of a brush with the opacity built in, cached per brush.</summary>
    private Brush Faded(Brush brush, double opacity)
    {
        if (opacity >= 1) return brush;
        if (_fadedBrushes.TryGetValue((brush, opacity), out Brush? cached)) return cached;
        Brush faded = brush.CloneCurrentValue();
        faded.Opacity *= opacity;
        if (faded.CanFreeze) faded.Freeze();
        _fadedBrushes[(brush, opacity)] = faded;
        return faded;
    }

    private Pen CachedPen(Brush brush, double thickness, bool dashed = false)
    {
        if (_pens.TryGetValue((brush, thickness, dashed), out Pen? cached)) return cached;
        Pen pen = new(brush, thickness);
        if (dashed) pen.DashStyle = DashStyles.Dash;
        _pens[(brush, thickness, dashed)] = Freeze(pen);
        return pen;
    }

    /// <summary>Gets a cached name label; landmark names are drawn semibold.</summary>
    private FormattedText Label(string text, bool bold = false, bool dimmed = false)
    {
        if (_labelCache.TryGetValue((text, bold, dimmed), out FormattedText? cached)) return cached;
        Brush brush = LabelBrush ?? Brushes.Black;
        FormattedText label = new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(TextElementFontFamily(), FontStyles.Normal,
                bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            12, dimmed ? Faded(brush, DimOpacity) : brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        _labelCache[(text, bold, dimmed)] = label;
        return label;
    }

    private FontFamily TextElementFontFamily() =>
        (FontFamily)GetValue(System.Windows.Documents.TextElement.FontFamilyProperty);

    private DependencyGraphView View => new(_scale, _offset, _angle);

    private Point ToScreen(DependencyGraphNode node) => View.ToScreen(node.X, node.Y);

    private Point ToWorld(Point point) => View.ToWorld(point);

    private static Pen Freeze(Pen pen)
    {
        // Theme brushes bound to dynamic colors cannot be frozen.
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }
}

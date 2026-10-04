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
    private readonly Dictionary<string, FormattedText[]> _treeNames = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Text, Brush Brush), FormattedText> _treeStatusLabels = [];
    private const double TreeTextScale = 0.35;
    private const double TreeCornerRadius = 8;
    private const double TreeNamePadding = 8;
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
    private bool _isPointerDown;
    private readonly DrawingVisual _content = new();
    private readonly DrawingVisual _overlay = new();
    private readonly MatrixTransform _contentTransform = new();
    private readonly DispatcherTimer _settleTimer;
    private DependencyGraphCamera _renderedView = new(1, default, 0);
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
        if (graph.IsTreeView)
        {
            Rect bounds = nodes.Select(node => graph.TreeLayout.BoxOf(node)).Aggregate(Rect.Union);
            double fit = Math.Min((ActualWidth - padding * 2) / Math.Max(bounds.Width, 1),
                (ActualHeight - padding * 2) / Math.Max(bounds.Height, 1));
            _scale = Math.Clamp(fit, MinScale, 2);
            _offset = new Vector(ActualWidth / 2 - (bounds.Left + bounds.Width / 2) * _scale,
                ActualHeight / 2 - (bounds.Top + bounds.Height / 2) * _scale);
        }
        else if (graph.IsAnimationEnabled)
        {
            // While the map turns, fit the whole circle so rotation never carries entities out of view.
            double reach = nodes.Max(static node => Math.Sqrt(node.X * node.X + node.Y * node.Y) + node.Radius) + 18;
            double size = Math.Min(ActualWidth, ActualHeight) - padding * 2;
            _scale = Math.Clamp(size / Math.Max(reach * 2, 1), MinScale, 2);
            _offset = new Vector(ActualWidth / 2, ActualHeight / 2);
        }
        else
        {
            DependencyGraphCamera turned = new(1, default, _angle);
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
        _offset = View.OffsetKeeping(PositionOf(node), new Point(ActualWidth / 2, ActualHeight / 2));
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
        if (graph is not null && _hoverNode is not null && !_isPointerDown && graph.IsVisible(_hoverNode))
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
        if (graph.IsTreeView)
        {
            DrawTree(context, graph, area);
            return;
        }

        bool hasSelection = graph.HasSelection;
        Brush edgeBrush = EdgeBrush ?? Brushes.Gray;
        Brush highlightBrush = HighlightBrush ?? Brushes.Black;

        // Orbit guides: foundations in the centre, each ring one more layer of dependencies.
        Pen orbitPen = CachedPen(Faded(edgeBrush, OrbitOpacity), 1);
        Point origin = new(_offset.X, _offset.Y);
        if (graph.ShowRings)
        {
            foreach (double ring in graph.Layout.RingRadii)
                context.DrawEllipse(null, orbitPen, origin, ring * _scale, ring * _scale);
        }

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
                // While an entity is selected its highlight stands alone; hovering only shows the card.
                bool hovered = !hasSelection && _hoverNode is not null &&
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
        double margin = Math.Max(HalfSize(node).Width, HalfSize(node).Height) + 200;
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

    /// <summary>
    /// Draws the top-to-bottom tree: links curve down from a dependency's bottom edge to the top of
    /// the entity using it, and each entity is a rounded box with its name above a band in its
    /// status colour.
    /// </summary>
    private void DrawTree(DrawingContext context, DependencyGraphViewModel graph, Rect? area)
    {
        TreeDependencyLayout tree = graph.TreeLayout;
        bool hasSelection = graph.HasSelection;
        Brush edgeBrush = EdgeBrush ?? Brushes.Gray;
        Brush highlightBrush = HighlightBrush ?? Brushes.Black;

        HashSet<DependencyGraphNode> hoverNeighbours = new(ReferenceEqualityComparer.Instance);
        StreamGeometry faint = new();
        StreamGeometry emphasized = new();
        using (StreamGeometryContext faintLines = faint.Open())
        using (StreamGeometryContext strongLines = emphasized.Open())
        {
            foreach ((DependencyGraphEdge edge, IReadOnlyList<Point> route) in tree.Routes)
            {
                if (!graph.IsVisible(edge)) continue;
                Point[] points = route.Select(point => View.ToScreen(point.X, point.Y)).ToArray();
                if (area is { } visibleArea &&
                    !points.Skip(1).Aggregate(new Rect(points[0], points[0]), (bounds, point) => Rect.Union(bounds, point))
                        .IntersectsWith(visibleArea))
                    continue;
                // While an entity is selected its highlight stands alone; hovering only shows the card.
                bool hovered = !hasSelection && _hoverNode is not null &&
                    (ReferenceEquals(edge.From, _hoverNode) || ReferenceEquals(edge.To, _hoverNode));
                if (hovered)
                {
                    hoverNeighbours.Add(edge.From);
                    hoverNeighbours.Add(edge.To);
                }

                AddTreeRoute(hovered || graph.HighlightedEdges.Contains(edge) ? strongLines : faintLines, points);
            }
        }

        faint.Freeze();
        emphasized.Freeze();
        context.DrawGeometry(null, CachedPen(Faded(edgeBrush, hasSelection ? DimOpacity : RestingLinkOpacity), 1), faint);
        context.DrawGeometry(null, CachedPen(highlightBrush, 2), emphasized);

        List<DependencyGraphNode> dimmed = [];
        List<DependencyGraphNode> normal = [];
        foreach (DependencyGraphNode node in graph.Model.Nodes)
        {
            if (!graph.IsVisible(node) || !IsInArea(node, area)) continue;
            bool isDimmed = hasSelection && !graph.HighlightedNodes.Contains(node) && !hoverNeighbours.Contains(node);
            (isDimmed ? dimmed : normal).Add(node);
        }

        foreach (DependencyGraphNode node in dimmed) DrawTreeBox(context, graph, node, DimOpacity);
        foreach (DependencyGraphNode node in normal) DrawTreeBox(context, graph, node, 1);
    }

    /// <summary>Adds smooth vertical curves through the route points.</summary>
    private static void AddTreeRoute(StreamGeometryContext lines, IReadOnlyList<Point> points)
    {
        lines.BeginFigure(points[0], false, false);
        for (int index = 1; index < points.Count; index++)
        {
            Point from = points[index - 1];
            Point to = points[index];
            double middle = (from.Y + to.Y) / 2;
            lines.BezierTo(new Point(from.X, middle), new Point(to.X, middle), to, true, false);
        }
    }

    private void DrawTreeBox(DrawingContext context, DependencyGraphViewModel graph, DependencyGraphNode node, double opacity)
    {
        Rect box = ScreenBox(node);
        double corner = TreeCornerRadius * _scale;
        double nameHeight = box.Height * TreeDependencyLayout.NameHeight / TreeDependencyLayout.BoxHeight;
        Rect band = new(box.Left, box.Top + nameHeight, box.Width, box.Height - nameHeight);
        Brush edgeBrush = EdgeBrush ?? Brushes.Gray;
        Brush highlightBrush = HighlightBrush ?? Brushes.Black;
        Brush bandBrush = node.IsPlaceholder
            ? Faded(PlaceholderBrush ?? Brushes.Gray, 0.18)
            : StatusBrush(node.Status);

        // The name area takes the card colour; the status band is clipped to the rounded outline.
        RectangleGeometry outline = new(box, corner, corner);
        outline.Freeze();
        context.DrawGeometry(Faded(CardBackground ?? Background ?? Brushes.White, opacity), null, outline);
        context.PushClip(outline);
        context.DrawRectangle(Faded(bandBrush, opacity), null, band);
        context.Pop();
        Pen border = node.IsPlaceholder
            ? CachedPen(Faded(PlaceholderBrush ?? Brushes.Gray, opacity), 1.5, dashed: true)
            : CachedPen(Faded(edgeBrush, opacity), 1);
        context.DrawGeometry(null, border, outline);

        if (ReferenceEquals(node, graph.SelectedNode))
        {
            Rect ring = box;
            ring.Inflate(4, 4);
            context.DrawRoundedRectangle(null, CachedPen(highlightBrush, 2.5), ring, corner + 4, corner + 4);
        }
        else if (graph.HighlightedNodes.Contains(node))
        {
            Rect ring = box;
            ring.Inflate(2.5, 2.5);
            context.DrawRoundedRectangle(null, CachedPen(Faded(highlightBrush, opacity), 1.5), ring, corner + 2.5, corner + 2.5);
        }

        // Text is drawn at the box's own scale, and skipped when it would be too small to read.
        if (_scale < TreeTextScale) return;
        context.PushTransform(new MatrixTransform(_scale, 0, 0, _scale, box.Left, box.Top));
        FormattedText[] lines = TreeNameLines(node.Label, opacity < 1);
        double lineHeight = lines.Length == 0 ? 0 : lines[0].Height;
        double y = (TreeDependencyLayout.NameHeight - lineHeight * lines.Length) / 2;
        foreach (FormattedText line in lines)
        {
            context.DrawText(line, new Point((TreeDependencyLayout.BoxWidth - line.Width) / 2, y));
            y += lineHeight;
        }

        string status = node.IsPlaceholder ? "Missing" : DependencyGraphViewModel.StatusLabel(node.Status);
        FormattedText statusText = TreeStatusLabel(status, ContrastingText(bandBrush, node.IsPlaceholder), opacity < 1);
        double bandHeight = TreeDependencyLayout.BoxHeight - TreeDependencyLayout.NameHeight;
        context.DrawText(statusText, new Point((TreeDependencyLayout.BoxWidth - statusText.Width) / 2,
            TreeDependencyLayout.NameHeight + (bandHeight - statusText.Height) / 2));
        context.Pop();
    }

    /// <summary>Gets a name broken over at most three lines, cached per name.</summary>
    private FormattedText[] TreeNameLines(string name, bool dimmed)
    {
        string key = (dimmed ? "dim:" : "on:") + name;
        if (_treeNames.TryGetValue(key, out FormattedText[]? cached)) return cached;
        Typeface typeface = new(TextElementFontFamily(), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        Brush brush = dimmed ? Faded(LabelBrush ?? Brushes.Black, DimOpacity) : LabelBrush ?? Brushes.Black;
        FormattedText Measure(string text) =>
            new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 12, brush, pixelsPerDip);
        IReadOnlyList<string> lines = DependencyGraphNameWrapper.Wrap(name, text => Measure(text).WidthIncludingTrailingWhitespace,
            TreeDependencyLayout.BoxWidth - TreeNamePadding * 2, maxLines: 3);
        FormattedText[] result = lines.Select(Measure).ToArray();
        _treeNames[key] = result;
        return result;
    }

    private FormattedText TreeStatusLabel(string text, Brush brush, bool dimmed)
    {
        Brush shown = dimmed ? Faded(brush, DimOpacity) : brush;
        if (_treeStatusLabels.TryGetValue((text, shown), out FormattedText? cached)) return cached;
        FormattedText label = new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(TextElementFontFamily(), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            11, shown, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        _treeStatusLabels[(text, shown)] = label;
        return label;
    }

    /// <summary>Picks black or white text, whichever reads better on the status colour.</summary>
    private Brush ContrastingText(Brush background, bool placeholder)
    {
        if (placeholder || background is not SolidColorBrush { Color: var color })
            return LabelBrush ?? Brushes.Black;
        double luminance = (0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B));
        return luminance > 0.4 ? Brushes.Black : Brushes.White;

        static double Linear(byte channel)
        {
            double value = channel / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
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
        double radius = HalfSize(node).Width;
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
        if (Graph is null) return;
        if (PointerPressed(e.GetPosition(this), e.ClickCount)) CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        PointerMoved(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!PointerReleased()) return;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        PointerLeft();
    }

    /// <summary>Ends a drag or pan when the capture is lost, for example after Alt+Tab mid-drag.</summary>
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        PointerReleased();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        PointerWheel(e.GetPosition(this), e.Delta);
        e.Handled = true;
    }

    // The pointer methods below hold the interaction logic; the WPF handlers above only translate
    // mouse events into them, so tests can drive clicks, drags and zooms on a real canvas.

    /// <summary>Handles a press; returns whether a drag or pan started that needs the pointer captured.</summary>
    internal bool PointerPressed(Point point, int clickCount)
    {
        DependencyGraphViewModel? graph = Graph;
        if (graph is null) return false;
        DependencyGraphNode? node = HitTestNode(point);
        if (node is not null && clickCount == 2)
        {
            graph.SelectedNode = node;
            graph.OpenDetails(node);
            return false;
        }

        MarkInteraction();
        _isPointerDown = true;
        _pressPoint = _lastPoint = point;
        _hasMoved = false;
        _dragNode = node;
        _isPanning = node is null;
        if (node is not null) node.IsPinned = true;
        return true;
    }

    internal void PointerMoved(Point point)
    {
        if (_isPointerDown && (_dragNode is not null || _isPanning))
        {
            if (!_hasMoved && (point - _pressPoint).Length < DragThreshold) return;
            _hasMoved = true;
            MarkInteraction();
            if (_dragNode is not null && IsTree)
            {
                // The tree is a fixed arrangement: pressing a box only selects it.
                _lastPoint = point;
                return;
            }

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

    /// <summary>Handles a release; returns whether a press was in progress.</summary>
    internal bool PointerReleased()
    {
        if (!_isPointerDown) return false;
        _isPointerDown = false;
        DependencyGraphViewModel? graph = Graph;
        if (graph is not null && !_hasMoved)
        {
            // Clicking the selected entity again clears the selection.
            graph.SelectedNode = _dragNode is not null && ReferenceEquals(_dragNode, graph.SelectedNode) ? null : _dragNode;
        }
        if (_dragNode is not null)
        {
            _dragNode.IsPinned = false;

            // Only a real drag wakes the layout. A plain click must leave the map still, or the
            // entity drifts away from the pointer before the second click of a double-click.
            if (_hasMoved && !IsTree)
            {
                graph?.Layout.MoveAnchor(_dragNode);
                graph?.Layout.Reheat();
                UpdateAnimation();
            }
        }

        _dragNode = null;
        _isPanning = false;
        MarkInteraction();
        return true;
    }

    internal void PointerLeft()
    {
        if (_hoverNode is null) return;
        _hoverNode = null;
        MarkInteraction();
        Redraw();
    }

    internal void PointerWheel(Point point, int delta)
    {
        MarkInteraction();
        ZoomAt(point, Math.Pow(1.0015, delta));
    }

    /// <summary>Gets where an entity is drawn right now; used by tests and screenshots.</summary>
    internal Point ScreenPositionOf(DependencyGraphNode node) => ToScreen(node);

    internal DependencyGraphNode? HoveredNode => _hoverNode;

    internal bool IsAnimating => _isAnimating;

    /// <summary>Gets the content drawn into the cached map layer, so tests can inspect what is shown.</summary>
    internal DrawingGroup? MapDrawing => VisualTreeHelper.GetDrawing(_content);

    /// <summary>Gets the hover card layer's content; empty while no card is shown.</summary>
    internal DrawingGroup? HoverCardDrawing => VisualTreeHelper.GetDrawing(_overlay);

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
        _treeNames.Clear();
        _treeStatusLabels.Clear();
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
        if (e.PropertyName == nameof(DependencyGraphViewModel.View))
        {
            // Each view has its own arrangement; show the whole of the new one.
            _hoverNode = null;
            FitToView();
            UpdateAnimation();
            return;
        }

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
        Graph is { IsAnimationEnabled: true, HasSelection: false, HasNodes: true, IsTreeView: false } &&
        _hoverNode is null && !_isPointerDown &&
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
            if (graph.IsTreeView)
            {
                if (ScreenBox(node).Contains(point)) return node;
                continue;
            }

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

    /// <summary>The tree is never rotated; the solar system keeps its own angle for when it returns.</summary>
    private DependencyGraphCamera View => new(_scale, _offset, IsTree ? 0 : _angle);

    private bool IsTree => Graph?.IsTreeView == true;

    private Point PositionOf(DependencyGraphNode node) => Graph?.PositionOf(node) ?? new Point(node.X, node.Y);

    private Point ToScreen(DependencyGraphNode node)
    {
        Point position = PositionOf(node);
        return View.ToScreen(position.X, position.Y);
    }

    /// <summary>Gets half the drawn width and height of an entity: its box in the tree, its dot otherwise.</summary>
    private Size HalfSize(DependencyGraphNode node)
    {
        if (IsTree)
            return new Size(TreeDependencyLayout.BoxWidth / 2 * _scale, TreeDependencyLayout.BoxHeight / 2 * _scale);
        double radius = Math.Max(node.Radius * _scale, 2.5);
        return new Size(radius, radius);
    }

    private Rect ScreenBox(DependencyGraphNode node)
    {
        Rect box = Graph!.TreeLayout.BoxOf(node);
        return new Rect(View.ToScreen(box.Left, box.Top), View.ToScreen(box.Right, box.Bottom));
    }

    private Point ToWorld(Point point) => View.ToWorld(point);

    private static Pen Freeze(Pen pen)
    {
        // Theme brushes bound to dynamic colors cannot be frozen.
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }
}

using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using EntityTracker.Domain;
using EntityTracker.Wpf.ViewModels;
using EntityTracker.Wpf.ViewModels.DependencyGraph;

namespace EntityTracker.Wpf.Controls;

/// <summary>
/// Draws the dependency map and handles pan, zoom, node dragging, selection and double-click.
/// The layout keeps animating only until it settles.
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

    private const double MinScale = 0.15;
    private const double MaxScale = 4;
    private const double LabelScale = 1.1;
    private const double DimOpacity = 0.12;
    private const double RestingLinkOpacity = 0.25;
    private const double OrbitOpacity = 0.3;
    private const double DragThreshold = 3;

    private readonly Dictionary<string, FormattedText> _labelCache = new(StringComparer.Ordinal);
    private double _scale = 1;
    private Vector _offset;
    private bool _needsFit = true;
    private bool _isAnimating;
    private DependencyGraphNode? _hoverNode;
    private DependencyGraphNode? _dragNode;
    private Point _pressPoint;
    private Point _lastPoint;
    private bool _isPanning;
    private bool _hasMoved;

    public DependencyGraphCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        Loaded += (_, _) => StartAnimationIfNeeded();
        Unloaded += (_, _) => StopAnimation();
        SizeChanged += (_, _) => { if (_needsFit) FitToView(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) { if (_needsFit) FitToView(); StartAnimationIfNeeded(); } };
    }

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
            InvalidateVisual();
            return;
        }

        double left = nodes.Min(static node => node.X - node.Radius);
        double right = nodes.Max(static node => node.X + node.Radius);
        double top = nodes.Min(static node => node.Y - node.Radius);
        double bottom = nodes.Max(static node => node.Y + node.Radius + 18);
        const double padding = 40;
        double scale = Math.Min(
            (ActualWidth - padding * 2) / Math.Max(right - left, 1),
            (ActualHeight - padding * 2) / Math.Max(bottom - top, 1));
        _scale = Math.Clamp(scale, MinScale, 2);
        _offset = new Vector(
            ActualWidth / 2 - (left + right) / 2 * _scale,
            ActualHeight / 2 - (top + bottom) / 2 * _scale);
        _needsFit = false;
        InvalidateVisual();
    }

    public void CenterOn(DependencyGraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _scale = Math.Max(_scale, 1);
        _offset = new Vector(ActualWidth / 2 - node.X * _scale, ActualHeight / 2 - node.Y * _scale);
        _needsFit = false;
        InvalidateVisual();
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
            context.DrawRectangle(new VisualBrush(this) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                null, new Rect(0, 0, ActualWidth, ActualHeight));
        }

        RenderTargetBitmap bitmap = new(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    protected override void OnRender(DrawingContext context)
    {
        Rect bounds = new(0, 0, ActualWidth, ActualHeight);
        context.DrawRectangle(Background ?? Brushes.Transparent, null, bounds);
        DependencyGraphViewModel? graph = Graph;
        if (graph is null) return;

        bool hasSelection = graph.HasSelection;
        Brush edgeBrush = EdgeBrush ?? Brushes.Gray;
        Brush highlightBrush = HighlightBrush ?? Brushes.Black;
        Pen edgePen = Freeze(new Pen(edgeBrush, 1));
        Pen highlightPen = Freeze(new Pen(highlightBrush, 2));
        double arrowSize = Math.Clamp(5 * _scale, 3, 9);

        // Orbit guides: hubs sit in the centre, dependencies orbit further out.
        Pen orbitPen = Freeze(new Pen(edgeBrush, 1) { DashStyle = DashStyles.Dash });
        Point origin = new(_offset.X, _offset.Y);
        context.PushOpacity(OrbitOpacity);
        foreach (double ring in graph.Layout.RingRadii)
            context.DrawEllipse(null, orbitPen, origin, ring * _scale, ring * _scale);
        context.Pop();

        HashSet<DependencyGraphNode> hoverNeighbours = new(ReferenceEqualityComparer.Instance);
        foreach (DependencyGraphEdge edge in graph.Model.EssentialEdges)
        {
            if (!graph.IsVisible(edge)) continue;
            bool hovered = _hoverNode is not null &&
                (ReferenceEquals(edge.From, _hoverNode) || ReferenceEquals(edge.To, _hoverNode));
            if (hovered)
            {
                hoverNeighbours.Add(edge.From);
                hoverNeighbours.Add(edge.To);
            }

            bool emphasized = hovered || graph.HighlightedEdges.Contains(edge);
            double opacity = emphasized ? 1 : hasSelection ? DimOpacity : RestingLinkOpacity;
            if (opacity < 1) context.PushOpacity(opacity);
            DrawEdge(context, edge, emphasized ? highlightPen : edgePen,
                emphasized ? highlightBrush : edgeBrush, arrowSize);
            if (opacity < 1) context.Pop();
        }

        Pen outlinePen = Freeze(new Pen(edgeBrush, 1));
        Pen placeholderPen = Freeze(new Pen(PlaceholderBrush ?? Brushes.Gray, 1.5) { DashStyle = DashStyles.Dash });
        Pen selectedPen = Freeze(new Pen(highlightBrush, 2.5));
        Pen chainPen = Freeze(new Pen(highlightBrush, 1.5));
        foreach (DependencyGraphNode node in graph.Model.Nodes)
        {
            if (!graph.IsVisible(node)) continue;
            bool highlighted = graph.HighlightedNodes.Contains(node);
            bool dimmed = hasSelection && !highlighted && !hoverNeighbours.Contains(node);
            Point center = ToScreen(node);
            double radius = Math.Max(node.Radius * _scale, 2.5);
            if (dimmed) context.PushOpacity(DimOpacity);
            if (node.IsPlaceholder)
                context.DrawEllipse(Background, placeholderPen, center, radius, radius);
            else
                context.DrawEllipse(StatusBrush(node.Status), outlinePen, center, radius, radius);
            if (ReferenceEquals(node, graph.SelectedNode))
                context.DrawEllipse(null, selectedPen, center, radius + 4, radius + 4);
            else if (highlighted)
                context.DrawEllipse(null, chainPen, center, radius + 2.5, radius + 2.5);

            bool showLabel = _scale >= LabelScale || hoverNeighbours.Contains(node) ||
                ReferenceEquals(node, _hoverNode) || (hasSelection && highlighted);
            if (showLabel)
            {
                FormattedText label = Label(node.Label);
                context.DrawText(label, new Point(center.X - label.Width / 2, center.Y + radius + 3));
            }

            if (dimmed) context.Pop();
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
            if (_dragNode is not null)
            {
                Point world = ToWorld(point);
                _dragNode.X = world.X;
                _dragNode.Y = world.Y;
                Graph?.Layout.Reheat();
                StartAnimationIfNeeded();
            }
            else
            {
                _offset += point - _lastPoint;
            }

            _lastPoint = point;
            InvalidateVisual();
            return;
        }

        DependencyGraphNode? hover = HitTestNode(point);
        if (!ReferenceEquals(hover, _hoverNode))
        {
            _hoverNode = hover;
            Cursor = hover is null ? null : Cursors.Hand;
            ToolTip = hover is null ? null : hover.IsPlaceholder ? $"{hover.Label} (missing dependency)" : hover.Label;
            InvalidateVisual();
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
            if (_hasMoved) graph?.Layout.MoveAnchor(_dragNode);
            graph?.Layout.Reheat();
            StartAnimationIfNeeded();
        }

        _dragNode = null;
        _isPanning = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverNode is null) return;
        _hoverNode = null;
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
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
        if (e.Property == LabelBrushProperty) _labelCache.Clear();
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
        canvas.StartAnimationIfNeeded();
    }

    private void OnGraphPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DependencyGraphViewModel.Model)) return;
        _hoverNode = null;
        _dragNode = null;
        if (_needsFit) FitToView();
        StartAnimationIfNeeded();
        InvalidateVisual();
    }

    private void OnGraphVisualStateChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void OnCenterOnRequested(object? sender, DependencyGraphNode node) => CenterOn(node);

    private void OnFitRequested(object? sender, EventArgs e) => FitToView();

    private void StartAnimationIfNeeded()
    {
        if (_isAnimating || !IsLoaded || !IsVisible || Graph?.Layout.IsSettled != false) return;
        _isAnimating = true;
        CompositionTarget.Rendering += OnRendering;
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
        if (graph is null || !IsVisible)
        {
            StopAnimation();
            return;
        }

        graph.Layout.Step();
        InvalidateVisual();
        if (graph.Layout.IsSettled && _dragNode is null) StopAnimation();
    }

    private void ZoomAt(Point anchor, double factor)
    {
        double scale = Math.Clamp(_scale * factor, MinScale, MaxScale);
        Point world = ToWorld(anchor);
        _scale = scale;
        _offset = new Vector(anchor.X - world.X * scale, anchor.Y - world.Y * scale);
        _needsFit = false;
        InvalidateVisual();
    }

    private void DrawEdge(DrawingContext context, DependencyGraphEdge edge, Pen pen, Brush arrowBrush,
        double arrowSize)
    {
        Point from = ToScreen(edge.From);
        Point to = ToScreen(edge.To);
        Vector direction = to - from;
        double length = direction.Length;
        double fromRadius = Math.Max(edge.From.Radius * _scale, 2.5);
        double toRadius = Math.Max(edge.To.Radius * _scale, 2.5) + 1.5;
        if (length <= fromRadius + toRadius + 1) return;
        direction /= length;
        Point start = from + direction * fromRadius;
        Point tip = to - direction * toRadius;
        Point arrowBase = tip - direction * arrowSize;
        context.DrawLine(pen, start, arrowBase);
        Vector normal = new(-direction.Y, direction.X);
        StreamGeometry arrow = new();
        using (StreamGeometryContext geometry = arrow.Open())
        {
            geometry.BeginFigure(tip, true, true);
            geometry.LineTo(arrowBase + normal * arrowSize * 0.55, false, false);
            geometry.LineTo(arrowBase - normal * arrowSize * 0.55, false, false);
        }

        arrow.Freeze();
        context.DrawGeometry(arrowBrush, null, arrow);
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

    private Brush StatusBrush(DevelopmentStatus? status) =>
        TryFindResource($"Brush.Status.{status}") as Brush ?? Brushes.Gray;

    private FormattedText Label(string text)
    {
        if (_labelCache.TryGetValue(text, out FormattedText? cached)) return cached;
        FormattedText label = new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(TextElementFontFamily(), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            12, LabelBrush ?? Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        _labelCache[text] = label;
        return label;
    }

    private FontFamily TextElementFontFamily() =>
        (FontFamily)GetValue(System.Windows.Documents.TextElement.FontFamilyProperty);

    private Point ToScreen(DependencyGraphNode node) =>
        new(node.X * _scale + _offset.X, node.Y * _scale + _offset.Y);

    private Point ToWorld(Point point) =>
        new((point.X - _offset.X) / _scale, (point.Y - _offset.Y) / _scale);

    private static Pen Freeze(Pen pen)
    {
        // Theme brushes bound to dynamic colors cannot be frozen.
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }
}

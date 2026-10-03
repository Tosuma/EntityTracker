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
    public static readonly DependencyProperty CardBackgroundProperty = RegisterBrush(nameof(CardBackground));

    private const double MinScale = 0.15;
    private const double MaxScale = 4;
    private const double LabelScale = 1.1;
    private const double DimOpacity = 0.12;
    private const double RestingLinkOpacity = 0.25;
    private const double OrbitOpacity = 0.3;
    private const double DragThreshold = 3;

    private readonly Dictionary<(string Text, bool Bold), FormattedText> _labelCache = [];
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
            Draw(context, includeHoverCard: false);

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
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context) => Draw(context, includeHoverCard: true);

    private void Draw(DrawingContext context, bool includeHoverCard)
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
            DrawEdge(context, edge, emphasized ? highlightPen : edgePen);
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
            if (dimmed) context.Pop();
        }

        DrawLabels(context, graph, hoverNeighbours);
        if (includeHoverCard && _hoverNode is not null && !IsMouseCaptured && graph.IsVisible(_hoverNode))
            DrawHoverCard(context, graph, _hoverNode);
    }

    /// <summary>
    /// Draws names on top of the map. Landmarks keep the zoomed-out map readable; names that
    /// would overlap a more important one are skipped.
    /// </summary>
    private void DrawLabels(DrawingContext context, DependencyGraphViewModel graph,
        HashSet<DependencyGraphNode> hoverNeighbours)
    {
        bool hasSelection = graph.HasSelection;
        List<DependencyGraphLabelCandidate<(FormattedText Text, bool Dimmed)>> candidates = [];
        foreach (DependencyGraphNode node in graph.Model.Nodes)
        {
            if (!graph.IsVisible(node)) continue;
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
            FormattedText text = Label(node.Label, landmark);
            Point center = ToScreen(node);
            double radius = Math.Max(node.Radius * _scale, 2.5);
            Rect bounds = new(center.X - text.Width / 2, center.Y + radius + 3, text.Width, text.Height);
            bool dimmed = hasSelection && !highlighted && !hoverNeighbours.Contains(node);
            candidates.Add(new((text, dimmed), bounds, tier * 100_000 - node.DependentCount, tier <= 1));
        }

        foreach (DependencyGraphLabelCandidate<(FormattedText Text, bool Dimmed)> label in
                 DependencyGraphLabelPlacer.Place(candidates))
        {
            if (label.Item.Dimmed) context.PushOpacity(DimOpacity);
            context.DrawText(label.Item.Text, label.Bounds.TopLeft);
            if (label.Item.Dimmed) context.Pop();
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

            // Only a real drag wakes the layout. A plain click must leave the map still, or the
            // entity drifts away from the pointer before the second click of a double-click.
            if (_hasMoved)
            {
                graph?.Layout.MoveAnchor(_dragNode);
                graph?.Layout.Reheat();
                StartAnimationIfNeeded();
            }
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

    /// <summary>Draws a plain line between the two node edges; the orbits already show direction.</summary>
    private void DrawEdge(DrawingContext context, DependencyGraphEdge edge, Pen pen)
    {
        Point from = ToScreen(edge.From);
        Point to = ToScreen(edge.To);
        Vector direction = to - from;
        double length = direction.Length;
        double fromRadius = Math.Max(edge.From.Radius * _scale, 2.5);
        double toRadius = Math.Max(edge.To.Radius * _scale, 2.5);
        if (length <= fromRadius + toRadius) return;
        direction /= length;
        context.DrawLine(pen, from + direction * fromRadius, to - direction * toRadius);
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

    /// <summary>Gets a cached name label; landmark names are drawn semibold.</summary>
    private FormattedText Label(string text, bool bold = false)
    {
        if (_labelCache.TryGetValue((text, bold), out FormattedText? cached)) return cached;
        FormattedText label = new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(TextElementFontFamily(), FontStyles.Normal,
                bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            12, LabelBrush ?? Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        _labelCache[(text, bold)] = label;
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

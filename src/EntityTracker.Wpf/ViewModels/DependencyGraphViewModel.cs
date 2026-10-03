using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using EntityTracker.Domain;
using EntityTracker.Wpf.Commands;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels.DependencyGraph;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntityTracker.Wpf.ViewModels;

public sealed record DependencyGraphLegendItem(string Label, string BrushKey, bool IsPlaceholder);

/// <summary>What the hover card shows for an entity or missing dependency.</summary>
public sealed record DependencyGraphNodeInfo(string Title, IReadOnlyList<string> Lines);

/// <summary>State for the Tracker's dependency map: selection, highlight, filters and export.</summary>
public sealed class DependencyGraphViewModel : INotifyPropertyChanged
{
    private readonly Func<EntityId, bool> _openDetails;
    private readonly IProgressChartFilePicker? _filePicker;
    private readonly NotificationCenter? _notifications;
    private readonly ILogger _logger;
    private readonly RelayCommand _findCommand;
    private readonly RelayCommand _clearSelectionCommand;
    private readonly RelayCommand _exportPngCommand;
    private DependencyGraphModel _model = DependencyGraphModel.Empty;
    private DependencyGraphNode? _selectedNode;
    private HashSet<DependencyGraphNode> _highlightedNodes = new(ReferenceEqualityComparer.Instance);
    private HashSet<DependencyGraphEdge> _highlightedEdges = new(ReferenceEqualityComparer.Instance);
    private HashSet<DependencyGraphNode> _landmarks = new(ReferenceEqualityComparer.Instance);
    private bool _isFocusMode;
    private bool _hideUnconnected;
    private string _searchText = string.Empty;
    private string? _searchMessage;

    public DependencyGraphViewModel(
        Func<EntityId, bool> openDetails,
        IProgressChartFilePicker? filePicker = null,
        NotificationCenter? notifications = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(openDetails);
        _openDetails = openDetails;
        _filePicker = filePicker;
        _notifications = notifications;
        _logger = logger ?? NullLogger.Instance;
        Layout = new RadialDependencyLayout(_model);
        _findCommand = new RelayCommand(Find, () => !string.IsNullOrWhiteSpace(SearchText) && HasNodes);
        _clearSelectionCommand = new RelayCommand(() => SelectedNode = null, () => HasSelection);
        _exportPngCommand = new RelayCommand(ExportPng,
            () => HasNodes && _filePicker is not null && PngWriter is not null);
        FitToViewCommand = new RelayCommand(() => FitRequested?.Invoke(this, EventArgs.Empty), () => HasNodes);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when the drawing must be refreshed without the model changing.</summary>
    public event EventHandler? VisualStateChanged;
    public event EventHandler<DependencyGraphNode>? CenterOnRequested;
    public event EventHandler? FitRequested;

    public static IReadOnlyList<DependencyGraphLegendItem> Legend { get; } =
    [
        new("Not started", "Brush.Status.NotStarted", false),
        new("In progress", "Brush.Status.InProgress", false),
        new("Rework needed", "Brush.Status.ReworkNeeded", false),
        new("Reworking", "Brush.Status.Reworking", false),
        new("Blocked", "Brush.Status.Blocked", false),
        new("Dev. completed", "Brush.Status.DevelopmentCompleted", false),
        new("Reconciled", "Brush.Status.Reconciled", false),
        new("Missing dependency (outer ring)", "Brush.Text.Secondary", true)
    ];

    public DependencyGraphModel Model
    {
        get => _model;
        private set
        {
            if (!SetField(ref _model, value)) return;
            OnPropertyChanged(nameof(HasNodes));
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(HiddenLinksDescription));
            NotifyCommands();
        }
    }

    public RadialDependencyLayout Layout { get; private set; }

    public bool HasNodes => Model.Nodes.Count > 0;

    public string Summary
    {
        get
        {
            int entities = Model.Nodes.Count(static node => !node.IsPlaceholder);
            int missing = Model.Nodes.Count - entities;
            int links = Model.EssentialEdges.Count;
            string text = $"{entities} {(entities == 1 ? "entity" : "entities")} · " +
                $"{links} {(links == 1 ? "link" : "links")}";
            return missing == 0 ? text : $"{text} · {missing} missing";
        }
    }

    public string HiddenLinksDescription
    {
        get
        {
            int hidden = Model.Edges.Count - Model.EssentialEdges.Count;
            return hidden == 0
                ? "Every direct dependency is drawn."
                : $"{hidden} {(hidden == 1 ? "link is" : "links are")} hidden because a longer dependency chain already implies {(hidden == 1 ? "it" : "them")}.";
        }
    }

    public DependencyGraphNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (!SetField(ref _selectedNode, value)) return;
            UpdateHighlight();
            OnPropertyChanged(nameof(HasSelection));
            NotifyCommands();
            OnVisualStateChanged();
        }
    }

    public bool HasSelection => SelectedNode is not null;

    /// <summary>Gets the entities whose names stay visible at every zoom level.</summary>
    public IReadOnlySet<DependencyGraphNode> Landmarks => _landmarks;

    public IReadOnlySet<DependencyGraphNode> HighlightedNodes => _highlightedNodes;
    public IReadOnlySet<DependencyGraphEdge> HighlightedEdges => _highlightedEdges;

    public bool IsFocusMode
    {
        get => _isFocusMode;
        set { if (SetField(ref _isFocusMode, value)) OnVisualStateChanged(); }
    }

    public bool HideUnconnected
    {
        get => _hideUnconnected;
        set { if (SetField(ref _hideUnconnected, value)) OnVisualStateChanged(); }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetField(ref _searchText, value ?? string.Empty)) return;
            SearchMessage = null;
            _findCommand.NotifyCanExecuteChanged();
        }
    }

    public string? SearchMessage
    {
        get => _searchMessage;
        private set
        {
            if (SetField(ref _searchMessage, value)) OnPropertyChanged(nameof(HasSearchMessage));
        }
    }

    public bool HasSearchMessage => !string.IsNullOrEmpty(SearchMessage);

    /// <summary>Writes the current drawing as a PNG; supplied by the graph control.</summary>
    public Action<string>? PngWriter
    {
        get;
        set { field = value; _exportPngCommand.NotifyCanExecuteChanged(); }
    }

    public ICommand FindCommand => _findCommand;
    public ICommand ClearSelectionCommand => _clearSelectionCommand;
    public ICommand FitToViewCommand { get; }
    public ICommand ExportPngCommand => _exportPngCommand;

    public void Rebuild(IReadOnlyList<EntityOverviewRow> rows)
    {
        string? selectedKey = SelectedNode?.Key;
        DependencyGraphModel model = DependencyGraphBuilder.Build(rows, Model);
        Layout = new RadialDependencyLayout(model);
        Layout.Settle();
        _selectedNode = null;
        _landmarks = FindLandmarks(model);
        Model = model;
        SelectedNode = selectedKey is null ? null : model.Find(selectedKey);
        UpdateHighlight();
        OnVisualStateChanged();
    }

    public bool IsVisible(DependencyGraphNode node)
    {
        if (ReferenceEquals(node, SelectedNode)) return true;
        if (HideUnconnected && !node.IsConnected) return false;
        return !IsFocusMode || !HasSelection || _highlightedNodes.Contains(node);
    }

    /// <summary>Links implied by a longer chain are never drawn.</summary>
    public bool IsVisible(DependencyGraphEdge edge) =>
        edge.IsEssential && IsVisible(edge.From) && IsVisible(edge.To);

    /// <summary>Opens the entity details pane; missing dependencies have no details.</summary>
    public bool OpenDetails(DependencyGraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.EntityId is { } entityId && _openDetails(entityId);
    }

    /// <summary>Explains an entity's place on the map for the hover card.</summary>
    public DependencyGraphNodeInfo Describe(DependencyGraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.IsPlaceholder)
        {
            string[] neededBy = Model.Edges.Where(edge => ReferenceEquals(edge.From, node))
                .Select(static edge => edge.To.Label).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            return new DependencyGraphNodeInfo(node.Label,
            [
                "Missing dependency",
                neededBy.Length <= 3
                    ? $"Needed by: {string.Join(", ", neededBy)}"
                    : $"Needed by: {neededBy.Length} entities"
            ]);
        }

        List<string> lines =
        [
            $"Status: {StatusLabel(node.Status)}",
            $"Rank: {(node.Rank is { } rank ? rank.ToString(System.Globalization.CultureInfo.CurrentCulture) : "Unranked")}",
            node.Level switch
            {
                0 => "Ring: Foundation",
                DependencyGraphNode.UnconnectedLevel => "Ring: Unconnected",
                _ => $"Ring: Level {node.Level}"
            },
            $"Depends on: {Count(node.DependencyCount)}",
            $"Used by: {Count(node.DependentCount)} · unblocks {node.TransitiveDependentCount}"
        ];
        string[] missing = Model.Edges
            .Where(edge => ReferenceEquals(edge.To, node) && edge.From.IsPlaceholder)
            .Select(static edge => edge.From.Label).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (missing.Length > 0) lines.Add($"Missing: {string.Join(", ", missing)}");
        return new DependencyGraphNodeInfo(node.Label, lines);

        static string Count(int count) => $"{count} {(count == 1 ? "entity" : "entities")}";
    }

    private static string StatusLabel(DevelopmentStatus? status) =>
        Legend.FirstOrDefault(item => item.BrushKey == $"Brush.Status.{status}")?.Label ?? "Unknown";

    /// <summary>
    /// Picks the landmarks: the most used foundations plus the entities most others refer to,
    /// so the zoomed-out map always has a few readable names.
    /// </summary>
    private static HashSet<DependencyGraphNode> FindLandmarks(DependencyGraphModel model)
    {
        IEnumerable<DependencyGraphNode> ByImportance(IEnumerable<DependencyGraphNode> nodes) => nodes
            .OrderByDescending(static node => node.DependentCount)
            .ThenBy(static node => node.Rank ?? int.MaxValue)
            .ThenBy(static node => node.Label, StringComparer.Ordinal);
        DependencyGraphNode[] candidates = model.Nodes
            .Where(static node => !node.IsPlaceholder && node.Level >= 0).ToArray();
        HashSet<DependencyGraphNode> landmarks = new(ReferenceEqualityComparer.Instance);
        landmarks.UnionWith(ByImportance(candidates.Where(static node => node.Level == 0)).Take(8));
        landmarks.UnionWith(ByImportance(candidates.Where(static node => node.DependentCount >= 2)).Take(10));
        return landmarks;
    }

    private void Find()
    {
        string text = SearchText.Trim();
        DependencyGraphNode? match =
            Model.Nodes.FirstOrDefault(node => string.Equals(node.Label, text, StringComparison.OrdinalIgnoreCase)) ??
            Model.Nodes.Where(node => node.Label.Contains(text, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static node => node.Label, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        if (match is null)
        {
            SearchMessage = $"No entity matches “{text}”.";
            return;
        }

        SearchMessage = null;
        if (!IsVisible(match)) HideUnconnected = false;
        SelectedNode = match;
        CenterOnRequested?.Invoke(this, match);
    }

    private void ExportPng()
    {
        if (_filePicker is null || PngWriter is null) return;
        string? path = _filePicker.SelectPngPath($"EntityTracker-dependency-graph-{DateTime.Now:yyyy-MM-dd}.png");
        if (path is null) return;
        try
        {
            PngWriter(path);
            _notifications?.Show("Dependency graph export",
                $"Saved the dependency graph to {Path.GetFileName(path)}.", NotificationKind.Success);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Dependency graph export failed");
            _notifications?.Show("Dependency graph export",
                "The dependency graph could not be saved. Check the destination and try again.",
                NotificationKind.Failure);
        }
    }

    /// <summary>
    /// Highlights the selection and everything it depends on, following links upstream only.
    /// Implied links still count for reachability but are not highlighted, since they are not drawn.
    /// </summary>
    private void UpdateHighlight()
    {
        HashSet<DependencyGraphNode> nodes = new(ReferenceEqualityComparer.Instance);
        HashSet<DependencyGraphEdge> edges = new(ReferenceEqualityComparer.Instance);
        if (SelectedNode is not null)
        {
            ILookup<DependencyGraphNode, DependencyGraphEdge> incoming =
                Model.Edges.ToLookup(static edge => edge.To);
            Queue<DependencyGraphNode> pending = new([SelectedNode]);
            nodes.Add(SelectedNode);
            while (pending.TryDequeue(out DependencyGraphNode? node))
            {
                foreach (DependencyGraphEdge edge in incoming[node])
                {
                    if (edge.IsEssential) edges.Add(edge);
                    if (nodes.Add(edge.From)) pending.Enqueue(edge.From);
                }
            }
        }

        _highlightedNodes = nodes;
        _highlightedEdges = edges;
        OnPropertyChanged(nameof(HighlightedNodes));
        OnPropertyChanged(nameof(HighlightedEdges));
    }

    private void NotifyCommands()
    {
        _findCommand.NotifyCanExecuteChanged();
        _clearSelectionCommand.NotifyCanExecuteChanged();
        _exportPngCommand.NotifyCanExecuteChanged();
        ((RelayCommand)FitToViewCommand).NotifyCanExecuteChanged();
    }

    private void OnVisualStateChanged() => VisualStateChanged?.Invoke(this, EventArgs.Empty);

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

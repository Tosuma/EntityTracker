using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using EntityTracker.Application.Dependencies;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf.Commands;
using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels.DependencyGraph;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntityTracker.Wpf.ViewModels;

public sealed record DependencyGraphLegendItem(string Label, string BrushKey, bool IsPlaceholder);

/// <summary>One entry in the dependency graph's view dropdown.</summary>
public sealed record DependencyGraphViewOption(DependencyGraphView View, string Label);

public sealed record DependencyHighlightOption(DependencyHighlightMode Mode, string Label);

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
    private readonly RelayCommand<DependencyGraphNode> _chooseSuggestionCommand;
    private readonly RelayCommand _refreshSuggestionsCommand;
    private IReadOnlyList<DependencyGraphNode> _suggestions = [];
    private bool _isSuggestionsOpen;
    private bool _isChoosing;
    private readonly RelayCommand _clearSelectionCommand;
    private readonly RelayCommand _exportPngCommand;
    private DependencyGraphModel _model = DependencyGraphModel.Empty;
    private List<DependencyGraphNode> _selected = [];
    private DependencyHighlightMode _solarHighlightMode = DependencyHighlightMode.Dependencies;
    private DependencyHighlightMode _treeHighlightMode = DependencyHighlightMode.DirectLinks;
    private HashSet<DependencyGraphNode> _highlightedNodes = new(ReferenceEqualityComparer.Instance);
    private HashSet<DependencyGraphEdge> _highlightedEdges = new(ReferenceEqualityComparer.Instance);
    private HashSet<DependencyGraphNode> _landmarks = new(ReferenceEqualityComparer.Instance);
    private bool _isFocusMode;
    private bool _hideUnconnected;
    private bool _isAnimationEnabled = true;
    private bool _showRings;
    private DependencyGraphView _view = DependencyGraphView.SolarSystem;
    private TreeDependencyLayout? _treeLayout;
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
        _chooseSuggestionCommand = new RelayCommand<DependencyGraphNode>(ChooseSuggestion, node => node is not null);
        _refreshSuggestionsCommand = new RelayCommand(() => Suggestions = Matches(SearchText.Trim()));
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

    /// <summary>Gets the legend for the current view; only the solar system has an outer ring.</summary>
    public IReadOnlyList<DependencyGraphLegendItem> Legend => IsTreeView ? TreeLegend : SolarLegend;

    private static IReadOnlyList<DependencyGraphLegendItem> SolarLegend { get; } =
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

    // Declared after SolarLegend: static properties initialise in order, and this one copies it.
    private static IReadOnlyList<DependencyGraphLegendItem> TreeLegend { get; } =
        [.. SolarLegend.Where(static item => !item.IsPlaceholder), new("Missing dependency", "Brush.Text.Secondary", true)];

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

    /// <summary>
    /// Gets or sets the entity chosen last. Setting it replaces the whole selection with that one
    /// entity; null clears the selection.
    /// </summary>
    public DependencyGraphNode? SelectedNode
    {
        get => _selected.Count == 0 ? null : _selected[^1];
        set => SetSelection(value is null ? [] : [value]);
    }

    /// <summary>Gets every selected entity, oldest first; Ctrl+click adds and removes them.</summary>
    public IReadOnlyList<DependencyGraphNode> SelectedNodes => _selected;

    public bool HasSelection => _selected.Count > 0;

    public bool IsSelected(DependencyGraphNode node) => _selected.Any(selected => ReferenceEquals(selected, node));

    /// <summary>Adds an entity to the selection, or removes it if it is already selected.</summary>
    public void ToggleSelection(DependencyGraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        SetSelection(IsSelected(node)
            ? _selected.Where(selected => !ReferenceEquals(selected, node)).ToList()
            : [.. _selected, node]);
    }

    private void SetSelection(List<DependencyGraphNode> selection)
    {
        if (selection.SequenceEqual(_selected, ReferenceEqualityComparer.Instance)) return;
        _selected = selection;
        OnPropertyChanged(nameof(SelectedNode));
        OnPropertyChanged(nameof(SelectedNodes));
        UpdateHighlight();
        OnPropertyChanged(nameof(HasSelection));
        NotifyCommands();
        OnVisualStateChanged();
    }

    /// <summary>Gets the highlight choices the page offers.</summary>
    public static IReadOnlyList<DependencyHighlightOption> HighlightOptions { get; } =
    [
        new(DependencyHighlightMode.Dependencies, "Dependencies"),
        new(DependencyHighlightMode.Dependents, "Dependents"),
        new(DependencyHighlightMode.DirectLinks, "Direct links"),
    ];

    /// <summary>Gets or sets what selecting highlights in the solar system.</summary>
    public DependencyHighlightMode SolarHighlightMode
    {
        get => _solarHighlightMode;
        set => SetHighlightMode(ref _solarHighlightMode, value, affectsCurrent: !IsTreeView);
    }

    /// <summary>Gets or sets what selecting highlights in the tree.</summary>
    public DependencyHighlightMode TreeHighlightMode
    {
        get => _treeHighlightMode;
        set => SetHighlightMode(ref _treeHighlightMode, value, affectsCurrent: IsTreeView);
    }

    /// <summary>Gets or sets what selecting highlights in the current view; each view remembers its own.</summary>
    public DependencyHighlightMode HighlightMode
    {
        get => IsTreeView ? _treeHighlightMode : _solarHighlightMode;
        set
        {
            if (IsTreeView) TreeHighlightMode = value;
            else SolarHighlightMode = value;
        }
    }

    private void SetHighlightMode(ref DependencyHighlightMode field, DependencyHighlightMode value, bool affectsCurrent,
        [CallerMemberName] string? propertyName = null)
    {
        if (!Enum.IsDefined(value) || !SetField(ref field, value, propertyName)) return;
        if (!affectsCurrent) return;
        OnPropertyChanged(nameof(HighlightMode));
        UpdateHighlight();
        OnVisualStateChanged();
    }

    /// <summary>Gets the entities whose names stay visible at every zoom level.</summary>
    public IReadOnlySet<DependencyGraphNode> Landmarks => _landmarks;

    public IReadOnlySet<DependencyGraphNode> HighlightedNodes => _highlightedNodes;
    public IReadOnlySet<DependencyGraphEdge> HighlightedEdges => _highlightedEdges;

    public bool IsFocusMode
    {
        get => _isFocusMode;
        set { if (SetField(ref _isFocusMode, value)) OnVisualStateChanged(); }
    }

    /// <summary>Gets or sets whether the map slowly rotates around its centre while nobody interacts with it.</summary>
    public bool IsAnimationEnabled
    {
        get => _isAnimationEnabled;
        set { if (SetField(ref _isAnimationEnabled, value)) OnVisualStateChanged(); }
    }

    /// <summary>Gets the views the dropdown offers.</summary>
    public static IReadOnlyList<DependencyGraphViewOption> ViewOptions { get; } =
    [
        new(DependencyGraphView.Tree, "Tree"),
        new(DependencyGraphView.SolarSystem, "Solar system"),
    ];

    /// <summary>Gets or sets how the map is drawn: the solar system or the top-to-bottom tree.</summary>
    public DependencyGraphView View
    {
        get => _view;
        set
        {
            if (!Enum.IsDefined(value) || !SetField(ref _view, value)) return;
            OnPropertyChanged(nameof(IsTreeView));
            OnPropertyChanged(nameof(Legend));
            OnPropertyChanged(nameof(HighlightMode));
            UpdateHighlight();
            OnVisualStateChanged();
        }
    }

    public bool IsTreeView => View == DependencyGraphView.Tree;

    /// <summary>Gets the tree arrangement, built the first time the tree is shown after a rebuild.</summary>
    public TreeDependencyLayout TreeLayout => _treeLayout ??= new TreeDependencyLayout(Model);

    /// <summary>Gets where an entity sits in the current view's world coordinates.</summary>
    public System.Windows.Point PositionOf(DependencyGraphNode node) =>
        IsTreeView ? TreeLayout.CenterOf(node) : new System.Windows.Point(node.X, node.Y);

    /// <summary>Gets or sets whether the orbit rings are drawn behind the map.</summary>
    public bool ShowRings
    {
        get => _showRings;
        set { if (SetField(ref _showRings, value)) OnVisualStateChanged(); }
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
            if (_isChoosing) return;
            string text = _searchText.Trim();
            Suggestions = text.Length == 0 ? [] : Matches(text);
            IsSuggestionsOpen = Suggestions.Count > 0;
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

    /// <summary>Gets the entities matching the search, best match first, as the dependency search ranks them.</summary>
    public IReadOnlyList<DependencyGraphNode> Suggestions
    {
        get => _suggestions;
        private set => SetField(ref _suggestions, value);
    }

    /// <summary>Gets or sets whether the suggestion list under the search box is open.</summary>
    public bool IsSuggestionsOpen
    {
        get => _isSuggestionsOpen;
        set => SetField(ref _isSuggestionsOpen, value);
    }

    /// <summary>Selects a suggested entity and centres the map on it.</summary>
    public ICommand ChooseSuggestionCommand => _chooseSuggestionCommand;

    /// <summary>Fills the list when it is opened with the arrow keys; an empty search lists every entity.</summary>
    public ICommand RefreshSuggestionsCommand => _refreshSuggestionsCommand;

    /// <summary>How many suggestions the list shows at most, as in the dependency search.</summary>
    internal const int MaxSuggestions = 50;
    public ICommand ClearSelectionCommand => _clearSelectionCommand;
    public ICommand FitToViewCommand { get; }
    public ICommand ExportPngCommand => _exportPngCommand;

    public void Rebuild(IReadOnlyList<EntityOverviewRow> rows)
    {
        string[] selectedKeys = _selected.Select(static node => node.Key).ToArray();
        DependencyGraphModel model = DependencyGraphBuilder.Build(rows, Model);
        Layout = new RadialDependencyLayout(model);
        Layout.Settle();
        _treeLayout = null;
        _selected = [];
        _landmarks = FindLandmarks(model);
        Model = model;
        SetSelection(selectedKeys.Select(model.Find).OfType<DependencyGraphNode>().ToList());
        IsSuggestionsOpen = false;
        Suggestions = [];
        UpdateHighlight();
        OnVisualStateChanged();
    }

    public bool IsVisible(DependencyGraphNode node)
    {
        if (IsSelected(node)) return true;
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
                0 => $"{PlaceWord}: Foundation",
                DependencyGraphNode.UnconnectedLevel => $"{PlaceWord}: Unconnected",
                _ => $"{PlaceWord}: Level {node.Level}"
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

    /// <summary>The solar system places entities on rings; the tree places them on levels.</summary>
    private string PlaceWord => IsTreeView ? "Level" : "Ring";

    internal static string StatusLabel(DevelopmentStatus? status) =>
        SolarLegend.FirstOrDefault(item => item.BrushKey == $"Brush.Status.{status}")?.Label ?? "Unknown";

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

    /// <summary>
    /// Ranks entities with the same word-aware rule as the dependency search, so "cust pref",
    /// "CustPref" and "cust_pref" all find "customer_preference".
    /// </summary>
    private IReadOnlyList<DependencyGraphNode> Matches(string text) => Model.Nodes
        .Select(node => (Node: node, Priority: EntityNameWords.MatchPriority(node.Label, text)))
        .Where(static match => match.Priority < int.MaxValue)
        .OrderBy(static match => match.Priority)
        .ThenBy(static match => match.Node.Label, StringComparer.OrdinalIgnoreCase)
        .ThenBy(static match => match.Node.Label, StringComparer.Ordinal)
        .Select(static match => match.Node)
        .Take(MaxSuggestions)
        .ToArray();

    private void ChooseSuggestion(DependencyGraphNode? node)
    {
        if (node is null) return;
        // A rebuild replaces every node; follow the suggestion to the current one.
        node = Model.Find(node.Key) ?? node;
        IsSuggestionsOpen = false;
        _isChoosing = true;
        try { SearchText = node.Label; }
        finally { _isChoosing = false; }
        Show(node);
    }

    private void Find()
    {
        string text = SearchText.Trim();
        IsSuggestionsOpen = false;
        DependencyGraphNode? match = Matches(text).FirstOrDefault();
        if (match is null)
        {
            SearchMessage = $"No entity matches “{text}”.";
            return;
        }

        Show(match);
    }

    private void Show(DependencyGraphNode node)
    {
        SearchMessage = null;
        if (!IsVisible(node)) HideUnconnected = false;
        SelectedNode = node;
        CenterOnRequested?.Invoke(this, node);
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
    /// Highlights what the selected entities lead to, as the union over all of them:
    /// <list type="bullet">
    /// <item><b>Dependencies</b>: everything they depend on, following links upward.</item>
    /// <item><b>Dependents</b>: everything that depends on them, following links downward.</item>
    /// <item><b>Direct links</b>: their own drawn links and the entities at the other ends.</item>
    /// </list>
    /// Implied links still count for reaching entities but are never highlighted, since they are not drawn.
    /// </summary>
    private void UpdateHighlight()
    {
        HashSet<DependencyGraphNode> nodes = new(ReferenceEqualityComparer.Instance);
        HashSet<DependencyGraphEdge> edges = new(ReferenceEqualityComparer.Instance);
        foreach (DependencyGraphNode selected in _selected) nodes.Add(selected);
        if (HighlightMode == DependencyHighlightMode.DirectLinks)
        {
            foreach (DependencyGraphEdge edge in Model.EssentialEdges)
            {
                if (!IsSelected(edge.From) && !IsSelected(edge.To)) continue;
                edges.Add(edge);
                nodes.Add(edge.From);
                nodes.Add(edge.To);
            }
        }
        else if (_selected.Count > 0)
        {
            bool upward = HighlightMode == DependencyHighlightMode.Dependencies;
            ILookup<DependencyGraphNode, DependencyGraphEdge> links = upward
                ? Model.Edges.ToLookup(static edge => edge.To)
                : Model.Edges.ToLookup(static edge => edge.From);
            Queue<DependencyGraphNode> pending = new(_selected);
            while (pending.TryDequeue(out DependencyGraphNode? node))
            {
                foreach (DependencyGraphEdge edge in links[node])
                {
                    if (edge.IsEssential) edges.Add(edge);
                    DependencyGraphNode next = upward ? edge.From : edge.To;
                    if (nodes.Add(next)) pending.Enqueue(next);
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

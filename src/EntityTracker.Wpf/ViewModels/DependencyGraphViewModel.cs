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
        new("Missing dependency", "Brush.Text.Secondary", true)
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
            OnPropertyChanged(nameof(SelectionDescription));
            NotifyCommands();
            OnVisualStateChanged();
        }
    }

    public bool HasSelection => SelectedNode is not null;

    public string SelectionDescription => SelectedNode is null
        ? "Click an entity to highlight everything it depends on. Double-click to open its details."
        : SelectedNode.IsPlaceholder
            ? $"{SelectedNode.Label} is a missing dependency."
            : $"{SelectedNode.Label} depends on {_highlightedNodes.Count - 1} " +
              $"{(_highlightedNodes.Count == 2 ? "entity" : "entities")} in its chain.";

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
        OnPropertyChanged(nameof(SelectionDescription));
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

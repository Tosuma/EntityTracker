using System.Globalization;
using System.Windows;
using System.Windows.Media;

using EntityTracker.Application.Overview;
using EntityTracker.Domain;
using EntityTracker.Reporting.ProjectReports;
using EntityTracker.Wpf.Controls;

namespace EntityTracker.Wpf.ViewModels.DependencyGraph;

/// <summary>
/// Adds the dependency graph to Project reports, one graph per Tracker, laid out by the app's own
/// tree and solar-system layouts so the report looks like the app. It carries only what a client
/// may see: names, statuses and links, never notes or developers.
/// </summary>
public sealed class DependencyGraphReportSectionProvider : IReportSectionProvider
{
    private const int SettleIterations = 1500;
    private const double NamePadding = 8;
    private static readonly Typeface NameTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal,
        FontWeights.SemiBold, FontStretches.Normal);

    /// <summary>Gets every section the app's Project reports have: the built-in ones and this graph.</summary>
    public static IReadOnlyList<IReportSectionProvider> AppSections { get; } =
        ProjectReportBuilder.DefaultProvidersWith(new DependencyGraphReportSectionProvider());

    public ReportSection? Build(ReportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Section(context.Trackers.Select(tracker => (context.TrackerScopeKey(tracker),
            (IReadOnlyList<EntityOverviewRow>)context.Entities[tracker.Id].Select(MainWindowViewModel.CreateOverviewRow).ToArray())));
    }

    /// <summary>Builds the section from each Tracker's scope and Overview rows; Trackers without entities get no graph.</summary>
    internal static GraphSection? Section(IEnumerable<(string ScopeKey, IReadOnlyList<EntityOverviewRow> Rows)> trackers)
    {
        Dictionary<string, ReportGraph> graphs = new(StringComparer.Ordinal);
        foreach ((string scopeKey, IReadOnlyList<EntityOverviewRow> rows) in trackers)
        {
            if (rows.Count > 0) graphs[scopeKey] = Graph(rows);
        }

        return graphs.Count == 0
            ? null
            : new GraphSection("dependency-graph", "Dependency graph", ReportVisibility.Everyone, graphs);
    }

    /// <summary>Builds one Tracker's graph from its active entities' Overview rows.</summary>
    internal static ReportGraph Graph(IReadOnlyList<EntityOverviewRow> rows)
    {
        DependencyGraphModel model = DependencyGraphBuilder.Build(rows);
        TreeDependencyLayout tree = new(model);
        RadialDependencyLayout solar = new(model);
        solar.Settle(SettleIterations);
        IReadOnlySet<DependencyGraphNode> landmarks = DependencyGraphViewModel.FindLandmarks(model);
        Dictionary<EntityId, EntityOverviewRow> byId = new();
        foreach (EntityOverviewRow row in rows) byId.TryAdd(row.EntityId, row);
        Dictionary<DependencyGraphNode, int> index = new(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < model.Nodes.Count; i++) index[model.Nodes[i]] = i;

        ReportGraphNode[] nodes = model.Nodes.Select(node =>
        {
            EntityOverviewRow? row = node.EntityId is { } id ? byId.GetValueOrDefault(id) : null;
            Rect box = tree.BoxOf(node);
            return new ReportGraphNode(
                node.Label,
                row is null ? string.Empty : ReportLabels.Status(row.DevelopmentStatus),
                row is null ? string.Empty : ReportLabels.WorkStatus(row.WorkflowState),
                row is null ? string.Empty : string.Join(", ", row.ReadinessBlockers.Select(static blocker => blocker.SourceName)),
                node.IsPlaceholder,
                landmarks.Contains(node),
                Round(node.Radius),
                Round(node.X),
                Round(node.Y),
                box.IsEmpty ? 0 : Round(box.X),
                box.IsEmpty ? 0 : Round(box.Y),
                NameLines(node.Label));
        }).ToArray();

        ReportGraphLink[] links = model.Edges.Select(edge => new ReportGraphLink(
            index[edge.From],
            index[edge.To],
            edge.IsEssential,
            edge.IsEssential ? Route(tree, edge) : null)).ToArray();
        return new ReportGraph(nodes, links, solar.RingRadii.Select(Round).ToArray());
    }

    /// <summary>The points a drawn link passes in the tree, as x, y pairs.</summary>
    private static IReadOnlyList<double> Route(TreeDependencyLayout tree, DependencyGraphEdge edge)
    {
        IReadOnlyList<Point> points = tree.Routes.TryGetValue(edge, out IReadOnlyList<Point>? route) && route.Count >= 2
            ? route
            : [Bottom(tree.BoxOf(edge.From)), Top(tree.BoxOf(edge.To))];
        return points.SelectMany(static point => new[] { Round(point.X), Round(point.Y) }).ToArray();
    }

    private static Point Bottom(Rect box) => new(box.X + box.Width / 2, box.Bottom);

    private static Point Top(Rect box) => new(box.X + box.Width / 2, box.Top);

    /// <summary>Breaks a name over at most three lines of a tree box, as the app draws it.</summary>
    private static IReadOnlyList<string> NameLines(string name) =>
        DependencyGraphNameWrapper.Wrap(name, Measure, TreeDependencyLayout.BoxWidth - NamePadding * 2, maxLines: 3);

    private static double Measure(string text) => new FormattedText(text, CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight, NameTypeface, 12, Brushes.Black, 1.0).WidthIncludingTrailingWhitespace;

    private static double Round(double value) => Math.Round(value, 1);
}

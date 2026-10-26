using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using EntityTracker.Application.Workflow;
using EntityTracker.Domain;
using EntityTracker.Theme;

namespace EntityTracker.Reporting.ProjectReports;

/// <summary>
/// Writes a Project report as one self-contained HTML file: the data as embedded JSON, and the
/// styles and script that draw it. Nothing is loaded from the network, so the file works offline
/// wherever it is opened, and the browser can print it to PDF.
/// </summary>
public static partial class ProjectReportHtmlWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        // The default encoder escapes <, > and &, so the data can never close its script element.
    };

    /// <summary>Gets the creator named in every report's footer; the app's medium name form.</summary>
    public const string CreatorName = "Tobias Surland";

    public static string Write(ProjectReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        string title = $"{report.ProjectName} – {AudienceLabel(report.Audience)}";
        StringBuilder html = new();
        html.AppendLine("<!doctype html>");
        html.AppendLine("<html lang=\"en\">");
        html.AppendLine("<head>");
        html.AppendLine("<meta charset=\"utf-8\">");
        html.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        // The report never talks to the network: only its own inline styles and script may run.
        html.AppendLine("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; " +
                        "style-src 'unsafe-inline'; script-src 'unsafe-inline'; img-src data:\">");
        html.AppendLine("<meta name=\"generator\" content=\"EntityTracker\">");
        html.Append("<title>").Append(WebUtility.HtmlEncode(title)).AppendLine("</title>");
        html.Append("<style>").Append(Styles()).AppendLine("</style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine("<noscript><p class=\"noscript\">This report needs JavaScript to show its charts and search. " +
                        "Open it in a current web browser.</p></noscript>");
        html.Append("<main id=\"report\" data-creator=\"").Append(WebUtility.HtmlEncode(CreatorName)).AppendLine("\"></main>");
        html.Append("<script id=\"report-data\" type=\"application/json\">")
            .Append(JsonSerializer.Serialize(report, JsonOptions))
            .AppendLine("</script>");
        html.Append("<script id=\"report-colors\" type=\"application/json\">")
            .Append(JsonSerializer.Serialize(Colors(), JsonOptions))
            .AppendLine("</script>");
        html.Append("<script>").Append(Asset("report-search.js")).AppendLine("</script>");
        html.Append("<script>").Append(Asset("report-charts.js")).AppendLine("</script>");
        html.Append("<script>").Append(Asset("report-graph.js")).AppendLine("</script>");
        html.Append("<script>").Append(Asset("report.js")).AppendLine("</script>");
        html.AppendLine("</body>");
        html.AppendLine("</html>");
        return html.ToString();
    }

    /// <summary>Suggests a file name such as "Order platform – Client report – 2026-10-06.html".</summary>
    public static string SuggestFileName(ProjectReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        string name = $"{report.ProjectName} – {AudienceLabel(report.Audience)} – " +
                      $"{report.GeneratedAt.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.html";
        return string.Concat(name.Select(static character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
    }

    public static string AudienceLabel(ReportAudience audience) =>
        audience == ReportAudience.Client ? "Client report" : "Internal report";

    /// <summary>
    /// Gets the CSS variables report.css may use, from <see cref="ColorScheme"/>: every base colour (for
    /// example --green-100) and every entry of <see cref="ColorScheme.Report"/> (for example --ink).
    /// </summary>
    public static IReadOnlyDictionary<string, string> CssColors { get; } =
        ColorSchemeCatalog.Section(nameof(ColorScheme.Base))
            .Concat(ColorSchemeCatalog.Section(nameof(ColorScheme.Report)))
            .ToDictionary(static entry => ColorSchemeCatalog.Kebab(entry.Name), static entry => (string)entry.Value);

    /// <summary>
    /// Gets report.css with each colour variable replaced by its value, which also works where variables
    /// don't (the printed page's margins). Variables the script sets, such as --swatch, are left alone.
    /// </summary>
    public static string Styles() => CssVariable().Replace(Asset("report.css"), static match =>
        CssColors.TryGetValue(match.Groups[1].Value, out string? color) ? color : match.Value);

    /// <summary>The colours report.js draws with: status badges by label, and a few chart colours.</summary>
    public static ReportColors Colors() => new(
        Enum.GetValues<DevelopmentStatus>().ToDictionary(ReportLabels.Status, static status => Pair(status switch
        {
            DevelopmentStatus.NotStarted => ColorScheme.Status.NotStarted,
            DevelopmentStatus.InProgress => ColorScheme.Status.InProgress,
            DevelopmentStatus.ReworkNeeded => ColorScheme.Status.ReworkNeeded,
            DevelopmentStatus.Reworking => ColorScheme.Status.Reworking,
            DevelopmentStatus.Blocked => ColorScheme.Status.Blocked,
            DevelopmentStatus.DevelopmentCompleted => ColorScheme.Status.DevelopmentCompleted,
            DevelopmentStatus.Reconciled => ColorScheme.Status.Reconciled,
            DevelopmentStatus.NotRelevant => ColorScheme.Status.NotRelevant,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        })),
        new Dictionary<string, string[]>
        {
            [ReportLabels.WorkStatus(EntityWorkflowState.Ready)] = Pair(ColorScheme.ReportWorkStatus.Ready),
            [ReportLabels.WorkStatus(EntityWorkflowState.Blocked)] = Pair(ColorScheme.ReportWorkStatus.WaitingOnDependencies),
            [ReportLabels.WorkStatus(EntityWorkflowState.ManuallyBlocked)] = Pair(ColorScheme.ReportWorkStatus.Blocked),
            [ReportLabels.WorkStatus(EntityWorkflowState.InProgress)] = Pair(ColorScheme.ReportWorkStatus.InProgress),
            [ReportLabels.WorkStatus(EntityWorkflowState.DevelopmentCompleted)] = Pair(ColorScheme.ReportWorkStatus.Completed),
            [ReportLabels.WorkStatus(EntityWorkflowState.Reconciled)] = Pair(ColorScheme.ReportWorkStatus.Reconciled),
            [ReportLabels.WorkStatus(EntityWorkflowState.NotRelevant)] = Pair(ColorScheme.ReportWorkStatus.NotRelevant)
        },
        ColorScheme.Chart.Canvas,
        ColorScheme.Chart.Grid,
        ColorScheme.Chart.UnknownStatus,
        ColorScheme.Report.MissingSwatch);

    // [fill, text], and the dashed edge of a hollow status (Not relevant) as a third entry.
    private static string[] Pair(StatusColor color) =>
        color.Edge is { } edge ? [color.Fill, color.Text, edge] : [color.Fill, color.Text];

    [GeneratedRegex(@"var\(--([a-z0-9-]+)\)")]
    private static partial Regex CssVariable();

    /// <summary>Gets one of the report's embedded styles or scripts, for example "report-search.js".</summary>
    public static string Asset(string name)
    {
        Assembly assembly = typeof(ProjectReportHtmlWriter).Assembly;
        using Stream stream = assembly.GetManifestResourceStream($"EntityTracker.Reporting.ProjectReports.Assets.{name}")
            ?? throw new InvalidOperationException($"The report asset {name} is missing.");
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// The colours written into a report for report.js: [fill, text] (and [2] the dashed edge of a hollow status) per development status label
/// (<paramref name="Status"/>) and per work status label (<paramref name="Work"/>), and the chart colours it uses.
/// </summary>
public sealed record ReportColors(
    IReadOnlyDictionary<string, string[]> Status,
    IReadOnlyDictionary<string, string[]> Work,
    string Canvas,
    string Grid,
    string UnknownStatus,
    string MissingSwatch);

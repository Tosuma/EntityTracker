using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EntityTracker.Reporting.ProjectReports;

/// <summary>
/// Writes a Project report as one self-contained HTML file: the data as embedded JSON, and the
/// styles and script that draw it. Nothing is loaded from the network, so the file works offline
/// wherever it is opened, and the browser can print it to PDF.
/// </summary>
public static class ProjectReportHtmlWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        // The default encoder escapes <, > and &, so the data can never close its script element.
    };

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
        html.Append("<style>").Append(Asset("report.css")).AppendLine("</style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine("<noscript><p class=\"noscript\">This report needs JavaScript to show its charts and search. " +
                        "Open it in a current web browser.</p></noscript>");
        html.AppendLine("<main id=\"report\"></main>");
        html.Append("<script id=\"report-data\" type=\"application/json\">")
            .Append(JsonSerializer.Serialize(report, JsonOptions))
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

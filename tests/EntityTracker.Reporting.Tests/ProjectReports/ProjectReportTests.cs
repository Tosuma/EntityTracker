using System.Text.Json;
using System.Text.RegularExpressions;

using EntityTracker.Application.Dependencies;
using EntityTracker.Reporting.ProjectReports;

using Jint;

namespace EntityTracker.Reporting.Tests.ProjectReports;

public sealed class ProjectReportTests
{
    private const string Secret = "Do not tell the client: vendor contract expires";

    [Fact]
    public void AClientReportLosesInternalSectionsColumnsAndTheirValues()
    {
        ReportSection[] sections = Report().Sections
            .Select(section => ProjectReportBuilder.ForAudience(section, ReportAudience.Client))
            .OfType<ReportSection>()
            .ToArray();

        Assert.DoesNotContain(sections, section => section.Key == "internal-only");
        TableSection table = Assert.Single(sections.OfType<TableSection>());
        Assert.Equal(["entity", "filterActive"], table.Columns.Select(column => column.Key));
        Assert.All(table.Rows, row => Assert.Equal(["entity", "filterActive"], row.Keys.Order()));
    }

    [Fact]
    public void AnInternalReportKeepsEverything()
    {
        ReportSection[] sections = Report().Sections
            .Select(section => ProjectReportBuilder.ForAudience(section, ReportAudience.Internal))
            .OfType<ReportSection>()
            .ToArray();

        Assert.Equal(Report().Sections.Count, sections.Length);
        Assert.Contains("internalNotes", Assert.Single(sections.OfType<TableSection>()).Columns.Select(column => column.Key));
    }

    [Fact]
    public void TheClientFileNeverContainsInternalText()
    {
        ProjectReport full = Report();
        ProjectReport client = full with
        {
            Audience = ReportAudience.Client,
            Sections = full.Sections.Select(section => ProjectReportBuilder.ForAudience(section, ReportAudience.Client))
                .OfType<ReportSection>().ToArray()
        };

        string clientHtml = ProjectReportHtmlWriter.Write(client);
        string internalHtml = ProjectReportHtmlWriter.Write(full);

        Assert.DoesNotContain(Secret, clientHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Alice Brown", clientHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Internal notes", clientHtml, StringComparison.Ordinal);
        Assert.Contains("Client report", clientHtml, StringComparison.Ordinal);
        Assert.Contains(Secret, internalHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFileIsSelfContainedAndCannotReachTheNetwork()
    {
        string html = ProjectReportHtmlWriter.Write(Report());

        Assert.StartsWith("<!doctype html>", html, StringComparison.Ordinal);
        Assert.Contains("Content-Security-Policy", html, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'", html, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex("<(script|link|img|iframe)[^>]*\\s(src|href)=", RegexOptions.IgnoreCase), html);
        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThePrintedReportHasContentsPageNumbersAndRepeatsTheTableHeader()
    {
        string html = ProjectReportHtmlWriter.Write(Report());

        Assert.Contains("Print / Save as PDF", html, StringComparison.Ordinal);
        Assert.Contains("window.print()", html, StringComparison.Ordinal);
        Assert.Contains("className: \"contents\"", html, StringComparison.Ordinal);
        Assert.Contains("@page", html, StringComparison.Ordinal);
        Assert.Contains("counter(page) \" of \" counter(pages)", html, StringComparison.Ordinal);
        Assert.Contains("thead { display: table-header-group; }", html, StringComparison.Ordinal);
        Assert.Contains(".charts section.report-section { break-inside: avoid; }", html, StringComparison.Ordinal);
        Assert.Contains("#dependency-graph { break-before: page; }", html, StringComparison.Ordinal);
        Assert.Contains("print-note", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TreeBoxesLookLikeTheAppsWithANameAreaAboveAStatusBand()
    {
        string html = ProjectReportHtmlWriter.Write(Report());

        Assert.Contains("TREE_WIDTH = 150, TREE_HEIGHT = 81, TREE_NAME = 54", html, StringComparison.Ordinal);
        Assert.Contains("\"class\": \"card\"", html, StringComparison.Ordinal);
        Assert.Contains("\"class\": \"band\"", html, StringComparison.Ordinal);
        Assert.Contains("\"class\": \"outline\"", html, StringComparison.Ordinal);
        Assert.Contains(".graph .card { fill: var(--surface); }", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEmbeddedDataCannotCloseItsScriptElement()
    {
        ProjectReport report = Report() with { ProjectName = "</script><script>alert(1)</script>" };

        string html = ProjectReportHtmlWriter.Write(report);
        string data = Regex.Match(html, "<script id=\"report-data\" type=\"application/json\">(.*?)</script>",
            RegexOptions.Singleline).Groups[1].Value;

        Assert.DoesNotContain("</script", data, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(report.ProjectName, JsonDocument.Parse(data).RootElement.GetProperty("projectName").GetString());
        Assert.DoesNotContain("<title></script>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void FileNamesAreSafeAndSayWhoTheReportIsFor()
    {
        ProjectReport report = Report() with
        {
            ProjectName = "Order/platform: phase 2",
            GeneratedAt = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero)
        };

        string name = ProjectReportHtmlWriter.SuggestFileName(report with { Audience = ReportAudience.Client });

        Assert.EndsWith(".html", name, StringComparison.Ordinal);
        Assert.Contains("Client report", name, StringComparison.Ordinal);
        Assert.Contains("2026-10-06", name, StringComparison.Ordinal);
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain(':', name);
    }

    public static TheoryData<string, string> SearchCases => new()
    {
        { "customer_preference", "CUSTOMER_PREFERENCE" },
        { "customer_preference", "custom" },
        { "customer_preference", "cust pref" },
        { "customer_preference", "CustPref" },
        { "customer_preference", "cust_pref" },
        { "CustomerPreference", "customer preference" },
        { "customerPreference", "cust-pref" },
        { "sales order line", "order line" },
        { "HTTPServerConfig", "server conf" },
        { "invoice2024Line", "2024" },
        { "customer_preference", "pref cust" },
        { "customer_preference", "ustomer" },
        { "invoice", "voice" },
        { "order-line", "-" },
        { "customer_preference", "__" },
        { "legal entity", "legalentity" },
        { "legalEntity", "legalentity" },
        { "legal_entity_type", "entitytype" },
        { "legal_entity", "galentity" },
        { "legal_entity", "legalentityx" },
        { "Ærø_Kommune", "ærø kom" },
        { "", "a" }
    };

    [Theory]
    [MemberData(nameof(SearchCases))]
    public void TheReportSearchRanksNamesExactlyLikeTheApp(string name, string query)
    {
        Engine engine = new();
        engine.Execute(ProjectReportHtmlWriter.Asset("report-search.js"));
        double noMatch = engine.Evaluate("EntityTrackerSearch.NO_MATCH").AsNumber();
        engine.SetValue("name", name).SetValue("query", query);

        double report = engine.Evaluate("EntityTrackerSearch.matchPriority(name, query)").AsNumber();
        int app = EntityNameWords.MatchPriority(name, query);

        Assert.Equal(app == int.MaxValue ? noMatch : app, report);
    }

    [Theory]
    [InlineData("HTTPServerConfig")]
    [InlineData("customer_preference")]
    [InlineData("invoice2024Line")]
    [InlineData("__weird..name__")]
    [InlineData("sales order line")]
    public void TheReportSplitsWordsLikeTheApp(string name)
    {
        Engine engine = new();
        engine.Execute(ProjectReportHtmlWriter.Asset("report-search.js"));
        engine.SetValue("name", name);

        string words = engine.Evaluate("EntityTrackerSearch.words(name).join('|')").AsString();

        Assert.Equal(string.Join('|', EntityNameWords.Words(name)), words);
    }

    private static ProjectReport Report() => new(
        "Commerce modernization",
        ReportAudience.Internal,
        new DateTimeOffset(2026, 10, 6, 9, 0, 0, TimeSpan.Zero),
        new DateOnly(2026, 5, 1),
        new DateOnly(2026, 10, 6),
        [new ReportScope(ReportScope.AllKey, "Core schema")],
        [
            new SummarySection("summary", "Summary", ReportVisibility.Everyone,
                new Dictionary<string, IReadOnlyList<SummaryCard>> { [ReportScope.AllKey] = [new("Active entities", 2)] }),
            new ChartSection("current-work-status", "Entities by current work status", ReportVisibility.Everyone,
                ReportChartType.Donut,
                new Dictionary<string, ReportChart>
                {
                    [ReportScope.AllKey] = new(["Not started"], [new("Entities", "#A0AFAF", [2], ["#A0AFAF"])])
                }),
            new SummarySection("internal-only", "Team load", ReportVisibility.InternalOnly,
                new Dictionary<string, IReadOnlyList<SummaryCard>> { [ReportScope.AllKey] = [new("Open items", 7)] }),
            new TableSection("entities", "Entities", ReportVisibility.Everyone,
                [
                    new ReportColumn("entity", "Entity", Searchable: true),
                    new ReportColumn("filterActive", "Filter active"),
                    new ReportColumn("internalNotes", "Internal notes", ReportVisibility.InternalOnly),
                    new ReportColumn("developers", "Responsible developers", ReportVisibility.InternalOnly)
                ],
                [
                    new Dictionary<string, string>
                    {
                        ["entity"] = "customer", ["filterActive"] = "Y",
                        ["internalNotes"] = Secret, ["developers"] = "Alice Brown"
                    },
                    new Dictionary<string, string>
                    {
                        ["entity"] = "invoice", ["filterActive"] = "N",
                        ["internalNotes"] = "", ["developers"] = ""
                    }
                ])
        ]);
}

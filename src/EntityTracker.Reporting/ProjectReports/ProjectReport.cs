using System.Text.Json.Serialization;

using EntityTracker.Domain;

namespace EntityTracker.Reporting.ProjectReports;

/// <summary>Who a report is for: the client, or us.</summary>
public enum ReportAudience
{
    Client,
    Internal
}

/// <summary>Who may see a section or column: everyone, or only the internal report.</summary>
public enum ReportVisibility
{
    Everyone,
    InternalOnly
}

/// <summary>What to put in a Project report.</summary>
public sealed record ProjectReportRequest(
    ProjectId ProjectId,
    IReadOnlyList<TrackerId> TrackerIds,
    ReportAudience Audience,
    ProgressDateRange Range);

/// <summary>
/// One Tracker in the report, or the combination of all selected Trackers. Sections hold their
/// data per scope, keyed by <see cref="Key"/>, so the report can switch between them.
/// </summary>
public sealed record ReportScope(string Key, string Name)
{
    public const string AllKey = "all";
}

/// <summary>
/// A finished Project report: the same model is shown in the app and written to HTML, and it
/// holds only what its audience may see.
/// </summary>
public sealed record ProjectReport(
    string ProjectName,
    ReportAudience Audience,
    DateTimeOffset GeneratedAt,
    DateOnly? DataFrom,
    DateOnly? DataTo,
    IReadOnlyList<ReportScope> Scopes,
    IReadOnlyList<ReportSection> Sections);

/// <summary>A part of the report. New kinds of content are new sections.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SummarySection), "summary")]
[JsonDerivedType(typeof(ChartSection), "chart")]
[JsonDerivedType(typeof(TableSection), "table")]
public abstract record ReportSection(string Key, string Title, ReportVisibility Visibility);

/// <summary>Headline numbers, per scope.</summary>
public sealed record SummarySection(
    string Key,
    string Title,
    ReportVisibility Visibility,
    IReadOnlyDictionary<string, IReadOnlyList<SummaryCard>> ByScope)
    : ReportSection(Key, Title, Visibility);

public sealed record SummaryCard(string Label, int Value, bool Attention = false);

public enum ReportChartType
{
    Donut,
    Line,
    Bars
}

/// <summary>A chart, per scope, as plain data that any renderer can draw.</summary>
public sealed record ChartSection(
    string Key,
    string Title,
    ReportVisibility Visibility,
    ReportChartType ChartType,
    IReadOnlyDictionary<string, ReportChart> ByScope)
    : ReportSection(Key, Title, Visibility);

/// <summary>Labels along the x axis (or slices) and one or more series of values.</summary>
public sealed record ReportChart(IReadOnlyList<string> Labels, IReadOnlyList<ReportSeries> Series)
{
    public bool HasData => Series.Any(series => series.Values.Count > 0 && series.Values.Any(value => value != 0));
}

/// <summary>A series; <see cref="PointColors"/> gives each slice its own colour in a donut.</summary>
public sealed record ReportSeries(
    string Name,
    string Color,
    IReadOnlyList<double> Values,
    IReadOnlyList<string>? PointColors = null);

/// <summary>A searchable table; every cell is text, keyed by its column.</summary>
public sealed record TableSection(
    string Key,
    string Title,
    ReportVisibility Visibility,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Rows)
    : ReportSection(Key, Title, Visibility);

/// <summary>
/// A table column. <see cref="Filter"/> columns get a dropdown filter; <see cref="Searchable"/>
/// columns take part in the search box; <see cref="Scope"/> marks the column that says which
/// Tracker a row belongs to. <see cref="Options"/> fixes a filter's choices and their order, so
/// a value is offered even when no row has it today; without it, the filter offers the values
/// that occur.
/// </summary>
public sealed record ReportColumn(
    string Key,
    string Header,
    ReportVisibility Visibility = ReportVisibility.Everyone,
    bool Searchable = false,
    bool Filter = false,
    bool Scope = false,
    IReadOnlyList<string>? Options = null);

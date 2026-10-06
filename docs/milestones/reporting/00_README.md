# RPT roadmap — Client and internal Project reports

**Status: RPT-01–RPT-04 completed.**

The RPT series lets us hand the client, for whom we perform the migration, a report of a Project's
progress. The client never has EntityTracker, so a report is exported as **one self-contained HTML
file** that works offline, can be searched and clicked, and can be printed to PDF from the browser.
The file is uploaded to SharePoint by hand; SharePoint Online usually downloads `.html` files rather
than showing them, and the downloaded file opens in any browser.

A report covers one Project and the Trackers chosen for it (one, several or all), for one of two
audiences: a **Client report** or an **Internal report**.

## Execution order

1. [RPT-01 — Project Report and HTML Export](rpt_01_project_report_html_export.md) — completed.
2. [RPT-01.1 — Report Filters and Chart Hover](rpt_01_1_report_filters_and_chart_hover.md) — completed.
3. [RPT-02 — Interactive Dependency Graph in the Report](rpt_02_interactive_dependency_graph.md) — completed.
4. [RPT-03 — Internal Notes and Shared Notes](rpt_03_internal_and_shared_notes.md) — completed.
5. [RPT-04 — Print Polish and Tracker Reports Retirement](rpt_04_print_polish_and_tracker_reports_retirement.md) — completed.

RPT-01.1, RPT-02 and RPT-03 build only on RPT-01 and may be done in any order; RPT-04 comes last.
Complete and verify each milestone before beginning the next.

## Shared contract

These rules hold for every RPT milestone.

- **One report model, many renderers.** `ProjectReportBuilder`
  (`src/EntityTracker.Reporting/ProjectReports/`) builds a renderer-neutral `ProjectReport`. The app's
  Project Report page and `ProjectReportHtmlWriter` both render it. New content is added as an
  `IReportSectionProvider` registered in `ProjectReportBuilder.DefaultProviders`, so it appears in the
  app and in every export without further wiring.
- **Audience filtering happens on the model.** Every section and table column has a
  `ReportVisibility`. For a client report, internal-only sections and columns are removed before any
  rendering, so internal data never reaches the client file, not even hidden. A test asserts this on
  the produced HTML.
- **The client report shows:** progress (summary and every chart), current development and work
  status, Filter active, dependencies, and (from RPT-03) Shared notes.
- **The client report leaves out:** internal notes, developer names and responsibility history,
  technical fields such as origin and missing-reference details, and each entity's status-change
  timeline.
- **The HTML file is self-contained.** Styles, scripts and data are embedded; a strict
  Content-Security-Policy blocks all network access. No external libraries are loaded at runtime;
  the scripts are plain JavaScript embedded from `ProjectReports/Assets/`.
- **Search matches the app.** The report's name matching (`report-search.js`) follows
  `EntityNameWords.MatchPriority` exactly (word-aware, written without spaces, mid-word); a Jint
  test compares both.
- **Screenshots and README** are updated with every milestone that changes a page or the report.

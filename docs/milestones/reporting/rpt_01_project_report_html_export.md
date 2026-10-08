# RPT-01 — Project Report and HTML Export

**Status: completed.**

## Goal and user-facing outcome

From the Project dashboard, open **Project report**, choose the Trackers, the audience (Client or
Internal) and the progress period, then **Export HTML…** or **Preview in browser**. The exported
file works offline and holds the summary, all progress charts (for all selected Trackers together or
one at a time), and a searchable, filterable, sortable entity table.

## What was built

- `EntityTracker.Reporting/ProjectReports/`: `ProjectReport` model (summary, chart and table
  sections, per scope), `ProjectReportBuilder` with registered `IReportSectionProvider`s
  (`SummarySectionProvider`, `ProgressChartSectionProvider` × 4, `EntityTableSectionProvider`),
  audience filtering (`ForAudience`), and `ProjectReportHtmlWriter` with embedded `report.css`,
  `report-search.js` and `report.js`.
- `AggregateProgressReportingService.GetTrackersReportAsync` combines exactly the selected Trackers.
- WPF: `ShellDestination.ProjectReport`, `ProjectReportViewModel`, `ProjectReportView`, a
  **Project report** button on the Project dashboard, and `IProjectReportFiles` (save dialog and
  browser preview).
- The entity table's internal-only columns are Internal notes (today's `Notes`), Responsible
  developers, Missing dependencies and Origin.
- Screenshot tool: `project-report.png`, plus client and internal sample reports in
  `artifacts/report-samples/` for review.

## Completion evidence

- Reporting tests: client export never contains internal text; the file loads nothing from the
  network and its data cannot close its script element; safe file names; the report's search equals
  `EntityNameWords.MatchPriority` for 22 cases and word splitting for 5 (run in Jint).
- Wpf tests: an end-to-end export through the real services with chosen Trackers (client file has
  Filter active but no internal notes; internal preview has them) and the "at least one Tracker" rule.
- Samples rendered in headless Edge and reviewed. All test projects passed.

## Known gaps, handled by later milestones

- Filters list only values that occur, so for example "Reworking" is missing when no entity has it,
  and charts only show the browser's built-in tooltip → RPT-01.1.
- No dependency graph in the report → RPT-02.
- No client-visible notes; "Notes" is not labelled as internal → RPT-03.
- Print layout is basic and the old per-Tracker Reports tab still exists → RPT-04.

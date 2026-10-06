# RPT-04 — Print Polish and Tracker Reports Retirement

**Status: planned.** Do after RPT-01.1, RPT-02 and RPT-03.

## Goal and user-facing outcome

- Printing the report from the browser (Save as PDF) gives a clean, readable document.
- Reporting lives in one place: the per-Tracker **Reports** tab is removed; its charts are in the
  Project report.

## Starting point and boundaries

RPT-01–RPT-03 are complete. `report.css` has a basic `@media print` block. The Tracker sidebar still
has **Reports** (`ShellDestination.Reports`, `MainWindowTab.Reports`, `ProgressDashboardViewModel` and
its view), with chart PNG save/copy.

## Required implementation

- **Print layout:**
  - A cover block (Project, audience, Trackers, generated date, progress period) and a short
    contents list of the sections.
  - Charts two per row, never split across pages; the summary on the first page.
  - The entity table repeats its header on every page, uses a print-friendly font size, and prints
    the rows currently shown (search and filters applied), with a line saying which filters are
    active. When nothing is filtered, all rows print.
  - Interactive parts print in their static state: no toolbars, no hover cards; the dependency graph
    (RPT-02) prints fitted to the page.
  - Page numbers and the report title in the page margin where browsers support it (`@page`).
  - A **Print / Save as PDF** button in the report that calls `window.print()`.
- **Retire the Tracker Reports tab:**
  - Remove the sidebar item, `ShellDestination.Reports`, `MainWindowTab.Reports` and the Reports tab
    view; move anything still unique (chart PNG save/copy) to the Project report page or drop it.
  - Navigation and settings that point to Reports go to the Project report instead.
  - Update screenshots (remove the old Reports captures from the manifest) and the README.

## Tests and acceptance

- Reporting: the HTML contains the print stylesheet rules for header repetition and page breaks, and
  the Print button.
- Print the demo client and internal reports to PDF with headless Edge (`--print-to-pdf`) and review
  them; check that the table header repeats and charts are not split.
- Wpf: navigation no longer offers Reports; anything relying on `ShellDestination.Reports` is
  updated; markup tests updated.
- Build and run all test projects; regenerate and publish screenshots; update the README.

## Agent planning prompt

```text
Plan RPT-04 only. Read docs/milestones/reporting/00_README.md and this milestone; inspect report.css/report.js print handling and every use of ShellDestination.Reports, MainWindowTab.Reports and ProgressDashboardViewModel. Specify the print layout, what moves to the Project report, the removals and the tests. Do not edit files or broaden the scope.
```

## Agent implementation prompt

```text
Implement RPT-04 according to its approved plan and this milestone. Polish the report's print layout for Save as PDF and remove the per-Tracker Reports tab, moving anything still needed to the Project report. Build and test the complete solution, regenerate screenshots, and update the README.
```

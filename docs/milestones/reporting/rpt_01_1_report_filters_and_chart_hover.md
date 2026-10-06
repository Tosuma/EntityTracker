# RPT-01.1 — Report Filters and Chart Hover

**Status: planned.**

## Goal and user-facing outcome

- The report's status filters always offer every status, so the reader can filter on, for example,
  "Reworking" even when no entity currently has it.
- The report's charts react to the mouse like the app's charts: a value card while hovering, and a
  short entry animation.

## Starting point and boundaries

RPT-01 is complete. The report filters list only the values present in the exported rows, sorted
alphabetically (`report.js`, `tableSection`), and the charts only carry SVG `<title>` tooltips. Keep
the shared RPT contract: no external libraries, no network access, audience filtering unchanged.

## Required implementation

- **Full status filters.** Give `ReportColumn` an optional `Options` list (fixed values, display
  order). `EntityTableSectionProvider` sets it for:
  - Development status: Not started, In progress, Rework needed, Reworking, Blocked, Dev. completed,
    Reconciled (the app's order).
  - Work status: Ready, Waiting on dependencies, Blocked, In progress, Completed, Reconciled.

  `report.js` builds those filters from `Options`, shows each value with its count ("Reworking (0)"),
  and keeps 0-count values. Columns without `Options` (Tracker, group) keep the "values that occur" list.
- **Hover card**, one shared tooltip that follows the pointer, styled like the app's chart tooltips:
  - Line charts: a vertical guide at the nearest date, highlighted points, and a card with the date
    and every series value ("Jun 11, 2026 · Implemented 23").
  - Bars: the hovered bar is highlighted; the card shows the week and value.
  - Donut: the hovered slice moves slightly outward; the card shows status, count and share
    ("Rework needed · 14 · 5.6%"). Legend entries and chart parts highlight each other on hover.
- **Keyboard:** charts are focusable; left and right arrows move the guide or slice and show the card.
- **Entry motion:** lines draw in, bars grow from the axis, donut slices sweep in (about 600 ms),
  replayed when "Show progress for" changes. Skipped under `prefers-reduced-motion` and in print.
- Put the pure chart helpers (nearest index, tooltip text, donut shares) in their own embedded
  script, `report-charts.js`, so they can be tested in Jint like `report-search.js`.

## Tests and acceptance

- Reporting: both status filters carry every value in app order; the HTML contains the options even
  when a status has no entities.
- Jint: nearest-index lookup, tooltip text, donut share percentages.
- Render the samples in headless Edge and check hover and keyboard by hand; filter on Reworking.
- Build and run all test projects; regenerate the sample reports.

## Agent planning prompt

```text
Plan RPT-01.1 only. Read docs/milestones/reporting/00_README.md and this milestone, then inspect src/EntityTracker.Reporting/ProjectReports (model, providers, HTML writer and Assets). Specify the column Options change, the hover/keyboard/motion behaviour per chart type, the report-charts.js helpers and the tests. Do not edit files or broaden the scope.
```

## Agent implementation prompt

```text
Implement RPT-01.1 according to its approved plan and this milestone. Add fixed status filter options with counts, live chart hover cards, keyboard navigation and short entry motion (respecting reduced motion and print) to the exported report, with Jint tests for the chart helpers. Keep the report self-contained and the audience filtering unchanged. Build and test the complete solution and regenerate the sample reports.
```

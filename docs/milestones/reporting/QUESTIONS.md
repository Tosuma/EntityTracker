# Questions raised during the RPT run

Questions that came up while implementing the reporting milestones without stopping to ask. Each
one records the choice made and how to change it. Send corrections and they will be applied.

## RPT-01.1 — Report Filters and Chart Hover

1. **Order of the development statuses in the filter.**
   The milestone listed Not started, In progress, Rework needed, Reworking, Blocked, Dev. completed,
   Reconciled, and called that the app's order. The app's Overview and bulk status picker actually
   use Not started, Blocked, In progress, Rework needed, Reworking, Dev. completed, Reconciled.
   - **Choice:** the app's real order, so the report matches what you see in EntityTracker.
   - **To change:** reorder `ReportLabels.StatusOrder` in `src/EntityTracker.Reporting/ProjectReports/ReportLabels.cs`.

2. **Counts on every filter, not only the status filters.**
   The milestone asked for counts such as "Reworking (0)" on the status filters.
   - **Choice:** every filter shows counts (Tracker, Group, and the internal report's developer and
     origin filters too), so the dropdowns look and behave the same.
   - **To change:** in `report.js` (`tableSection`), only add the count when `column.options` is set.

3. **The summary's "Blocked" card (still open from RPT-01).**
   The card counts entities waiting on dependencies (134 in the demo), while the status chart's
   "Blocked" means entities manually set to Blocked (1 in the demo). A client may read these as
   the same thing.
   - **Choice:** left unchanged, because it was asked before the run and not answered, and renaming
     changes what the client sees.
   - **Suggested:** rename the card "Waiting on dependencies" in `SummarySectionProvider`
     (`ReportSectionProviders.cs`). Say the word and it will be done.

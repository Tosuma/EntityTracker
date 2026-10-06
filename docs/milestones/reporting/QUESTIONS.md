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

## RPT-02 — Interactive Dependency Graph in the Report

1. **Where the graph data is built.**
   The app's graph builder, layouts and highlight rules live in the WPF project, which the Reporting
   library cannot use.
   - **Choice:** a `DependencyGraphReportSectionProvider` in the WPF project builds the section with
     the app's own code, and is added to every report through `DependencyGraphReportSectionProvider.AppSections`.
     Nothing was moved or duplicated, so the report always draws what the app draws.
   - **To change:** move the graph model and layouts out of WPF into a shared library first.

2. **One graph per Tracker, not one combined graph.**
   Entity names are only unique within a Tracker, and dependencies are resolved per Tracker, so a
   merged graph would link entities that are not related.
   - **Choice:** when several Trackers are selected, the graph has its own Tracker picker, and it also
     follows "Show progress for" when that names a Tracker.
   - **To change:** say if a combined view is wanted, for example side by side.

3. **Which view the graph opens in.**
   - **Choice:** the tree, because it reads top to bottom without explanation; the solar system is one
     click away. Highlight defaults match the app: Direct links in the tree, Dependencies in the
     solar system.
   - **To change:** `view: "tree"` in `graphSection` in `report.js`.

4. **Orbit rings in the solar system.**
   - **Choice:** hidden by default with a "Show orbits" checkbox, as in the app, where rings are off
     unless turned on in Settings. The report does not carry your Settings choice.

5. **Overlapping names in the solar system.**
   The app moves names that would overlap; the report shows the landmark names (and, when zoomed in,
   all names) without that step, so a few can overlap until you zoom.
   - **Choice:** left as it is; porting the label placer can be done later if it bothers readers.

6. **"Depends on" counts in the hover card.**
   - **Choice:** they count every link, including links implied by a longer chain that are not drawn,
     the same as the app's dot size and Overview dependency count.

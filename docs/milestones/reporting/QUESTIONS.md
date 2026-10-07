# Questions raised during the RPT run

Questions that came up while implementing the reporting milestones without stopping to ask. Each
one records the choice made and how to change it. Send corrections and they will be applied.

## Decisions, 7 October 2026

All questions below were answered. What changed as a result:

- **Summary card:** "Blocked" is now "Waiting on dependencies" (RPT-01.1, question 3).
- **Dependency tree only:** the report no longer has a solar-system view, orbit rings or zoom and
  pan. The tree is fitted to the width of the page at a fixed size, its boxes show only the status
  colour (no names), and hovering names the entity. It prints exactly as shown (RPT-02, questions
  3–5; RPT-04, question 3).
- **Hover-card counts:** count only the links drawn in the tree (RPT-02, question 6).
- **Internal notes helper text:** "Never shown in client report".
- **Demo notes:** `Seed-ProgressDemo.ps1 -DemoNotes` gives some entities without notes example
  Internal and Shared notes; the screenshot tool uses it, so samples and screenshots show both
  (RPT-03, question 4).
- **Custom date range:** the Project report page has a "Custom range" with From and To dates
  (RPT-04, question 1).
- **Flaky backup test:** a just-written backup can be locked for a moment (for example by a virus
  scanner); moving it into its version folder now retries briefly, and a test holds a lock to prove
  it (RPT-04, question 5).

Kept as built: the app's status order, counts on every filter, one Tracker at a time in the tree,
Shared notes only where they are now, sync format 5, friendly field names in merge review, the
Project report on the dashboard only, printing the rows on screen, and the graph built in the WPF
project.

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

## RPT-03 — Internal Notes and Shared Notes

1. **Keeping the sync change small.**
   The snapshot format moves from 4 to 5, as planned, because an older app would otherwise drop
   Shared notes without noticing and erase them on its next sync.
   - **Choice:** empty Shared notes are left out of the entity files, so upgrading does not rewrite
     every entity file in the Project repository; only entities with Shared notes change. Colleagues
     on an older app are asked to update once the Project is synced in format 5.

2. **Conflict titles use the same field names as the conflict details.**
   Merge-conflict titles said "Notes" while their details already used the friendly labels. Both now
   say "Internal notes" and "Shared notes". As a side effect, titles also use "Name" and "State"
   for the source name and lifecycle state, as the details already did.
   - **To change:** `Title` in `ProjectMergeConflictPresenter`.

3. **Where Shared notes appear.**
   - **Choice:** the editor, the details pane, the Overview export (CSV and Excel), merge review, and
     both Project reports (searchable). As the milestone said, the Overview table itself has no new
     column, and an archived entity's read-only summary still shows only its internal notes.
   - **To change:** say if the Overview should get a Shared notes column or search.

4. **Demo data.**
   The demo Project has no notes, so the sample reports show an empty Shared notes column. The
   tests cover Shared notes reaching the client report.

## RPT-04 — Print Polish and Tracker Reports Retirement

1. **What happened to the Tracker Reports tab's features.**
   - **Moved:** chart PNG save and copy now live under **Chart images** on the Project report page.
     They use the chosen Trackers together and the chosen progress period (one Tracker gives exactly
     what the old tab gave).
   - **Dropped:** the in-app charts themselves (they are in the report, and the Project dashboard
     still shows the Project's progress) and the **custom date range**; the Project report offers
     All history and the last 30, 60 or 90 days.
   - **To change:** say if a custom date range is wanted on the Project report page.

2. **Where the Project report is opened.**
   The sidebar had "Reports" under Tracker; it is gone. The Project report stays on the Project
   dashboard's **Project report** button, as agreed when RPT-01 was planned.
   - **To change:** a "Project report" sidebar entry can be added next to Project dashboard.

3. **The dependency graph on paper.**
   - **Choice:** the graph gets its own page and is fitted to it when printing starts, then the view
     on screen comes back. In large Projects the names in a printed tree are too small to read; the
     entity table that follows lists every entity with its dependencies.
   - **Seen in testing:** headless Edge does not send the "before print" signal, so its PDFs show the
     view as it was on screen, scaled to the page. Browsers printing for a person do send it.

4. **Which rows the table prints.**
   - **Choice:** the rows currently shown, so a reader can search or filter before printing; a line
     above the table says which search and filters chose them, or "All N entities".

5. **A timing-sensitive backup test.**
   `SqliteBackupServiceTests.Startup_OrganizesLegacyBackupsByDatabaseSchemaRatherThanFilename`
   failed once during a full parallel test run and passed in three separate runs and a full rerun of
   the Infrastructure project. It is unrelated to the report work; worth a look if it fails again.

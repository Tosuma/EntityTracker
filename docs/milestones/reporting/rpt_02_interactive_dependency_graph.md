# RPT-02 — Interactive Dependency Graph in the Report

**Status: completed.**

## Goal and user-facing outcome

The exported report contains the dependency graph, interactive like in the app, so the client can
see how entities depend on each other without EntityTracker.

## Starting point and boundaries

RPT-01 is complete. The app's graph lives in WPF (`DependencyGraphCanvas`,
`DependencyGraphViewModel`), built from `DependencyGraphBuilder` with the solar-system
(`RadialDependencyLayout`) and tree (`TreeDependencyLayout`) layouts. The report must stay
self-contained (no libraries, no network) and keep the audience filtering.

## Required implementation

- A `DependencyGraphSectionProvider` adds a graph section, per scope (all selected Trackers and each
  Tracker): nodes (name, Tracker, development status, colour, missing-dependency placeholders) and
  only the drawn (non-implied) links, plus precomputed positions from the existing layouts so the
  report looks like the app. Prefer the tree layout for the report, with a Solar system / Tree switch
  if both fit within a sensible file size.
- In `report.js`: an SVG graph with zoom (wheel, buttons), pan (drag), fit to view, a status legend,
  hover card (status, work status, dependencies, waiting on), and click to highlight with the app's
  three choices: Dependencies, Dependents, Direct links. Ctrl+click adds to the selection.
- The report search highlights matching entities in the graph and offers "show in graph" from a table
  row.
- Performance: draw efficiently for a few hundred entities (one path per link batch, no per-frame
  layout); keep the file small (positions rounded, names stored once).
- Audience: the graph shows only client-visible facts; it never carries internal notes or developer
  names.

## Tests and acceptance

- Reporting: the graph section holds every entity of the scope, only essential links, and missing
  dependencies as placeholders; a client report's graph data contains no internal fields.
- Jint: highlight rules (Dependencies, Dependents, Direct links, union for several) on a small graph,
  compared with the app's `DependencyGraphViewModel` results.
- Render the demo report in headless Edge; check zoom, pan, hover, highlight and search by hand.
- Build and run all test projects; regenerate screenshots and sample reports; update the README.

## Completion evidence

- `GraphSection`, `ReportGraph`, `ReportGraphNode` and `ReportGraphLink` hold one graph per Tracker
  (the "all" scope when the report has one Tracker). `ReportContext.TrackerScopeKey` and
  `ProjectReportBuilder.DefaultProvidersWith` let the app add the section before the entity table.
- `DependencyGraphReportSectionProvider` (WPF) builds it with the app's own `DependencyGraphBuilder`,
  `TreeDependencyLayout`, `RadialDependencyLayout` (settled), landmarks and name wrapping. Nodes
  carry only name, status, work status, waiting-on names, missing flag and positions. Registered
  through `AppSections` in the app, the screenshot tool and the Wpf test harness.
- The report draws an SVG graph with Tree and Solar system views, "Show orbits", Highlight
  (Dependencies, Dependents, Direct links), Ctrl+click multi-select, hover cards, drag to pan,
  wheel/buttons/keys to zoom, Fit to view, Esc to clear, a status legend, and its own Tracker picker
  that also follows "Show progress for". The table search marks matching entities in the graph,
  and entity names in the table open the entity in the graph. The pure highlight and camera rules
  are in the new embedded `report-graph.js`.
- Tests: highlight parity with the app's `DependencyGraphViewModel` for 6 selections across all
  three modes (Jint); graph contents, implied links, missing placeholders, name wrapping, per-Tracker
  scopes, no internal fields; section order; JSON shape; camera fit/zoom/centre; the end-to-end
  export holds a graph for each chosen Tracker and none for the unchosen one.
- Checked in headless Edge on the demo report: tree, solar system, hover, selection, search marks
  and "Show in graph" in both views. The client sample grew from 116 KB to 248 KB.
- `dotnet build -c Release` succeeded. `dotnet test -c Release` passed all 1,043 tests: Domain 61,
  Application 249, DemoData 8, Reporting 86, Screenshots 12, Wpf 376, Infrastructure 251.
- No app page changed, so the README screenshots were not republished; the README describes the graph.

## Agent planning prompt

```text
Plan RPT-02 only. Read docs/milestones/reporting/00_README.md and this milestone; inspect the report model, providers and Assets, and the app's dependency graph (DependencyGraphBuilder, RadialDependencyLayout, TreeDependencyLayout, DependencyGraphViewModel highlight rules). Specify the section data, the layout used, the JavaScript interaction, file-size limits and the tests. Do not edit files or broaden the scope.
```

## Agent implementation prompt

```text
Implement RPT-02 according to its approved plan and this milestone. Add a dependency graph section to the Project report with precomputed layout, and an interactive SVG graph in the exported HTML (zoom, pan, hover, highlight modes, search integration) that carries only client-visible data in client reports. Build and test the complete solution, regenerate screenshots and samples, and update the README.
```

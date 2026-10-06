# RPT-02 — Interactive Dependency Graph in the Report

**Status: planned.**

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

## Agent planning prompt

```text
Plan RPT-02 only. Read docs/milestones/reporting/00_README.md and this milestone; inspect the report model, providers and Assets, and the app's dependency graph (DependencyGraphBuilder, RadialDependencyLayout, TreeDependencyLayout, DependencyGraphViewModel highlight rules). Specify the section data, the layout used, the JavaScript interaction, file-size limits and the tests. Do not edit files or broaden the scope.
```

## Agent implementation prompt

```text
Implement RPT-02 according to its approved plan and this milestone. Add a dependency graph section to the Project report with precomputed layout, and an interactive SVG graph in the exported HTML (zoom, pan, hover, highlight modes, search integration) that carries only client-visible data in client reports. Build and test the complete solution, regenerate screenshots and samples, and update the README.
```

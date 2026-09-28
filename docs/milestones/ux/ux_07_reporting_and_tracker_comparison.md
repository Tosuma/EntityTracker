# UX-07 — Reporting and Tracker Comparison

## Context

Tracker-level reporting currently provides status distribution, implementation history, readiness
history, weekly throughput, PNG export, and clipboard copy. UX-01 scopes history per Tracker, and
UX-03 introduces basic Portfolio and Project dashboards.

## Goal

Turn Reports and the dashboards into a coherent manager reporting experience with honest aggregate
progress and a detailed comparison of related Trackers.

## User-facing outcome

A manager can understand total portfolio/project progress, compare related tracks at a glance,
inspect entity-level differences, and export a selected Tracker's reporting charts.

## Design decisions

- Rename the Tracker destination from Progress to Reports.
- Keep detailed charts and exports tracker-scoped.
- Add aggregate charts to both Portfolio and Project dashboards.
- Aggregate progress by active entity instances, not equal Tracker averages.
- Show per-Tracker context beside aggregate totals.
- Compare entities only within a Project and match them by normalized source key.

## Required implementation

### Reporting query models

- Extend Application/Reporting with Project and Portfolio dashboard query models.
- Aggregate only active Projects, active Trackers, and active entities.
- Provide entity-weighted current counts/percentages and persisted historical trends.
- Preserve the distinction between Dev. completed, Reconciled, Ready, Blocked, Rework needed, and
  unresolved references.
- Provide last-activity timestamps from persisted history rather than WPF observation.
- Provide a Project entity-comparison query keyed by normalized source key.
- Keep all calculations deterministic and testable without WPF/chart controls.

### Tracker Reports

- Rename and restyle the existing destination while retaining date-range behavior.
- Preserve current status distribution, cumulative implemented trend, Ready/Blocked trend, weekly
  net change, manager summary, PNG export, clipboard copy, and readable date-axis behavior.
- Keep export colors/labels stable and accessible independent of the live theme.

### Portfolio dashboard

- Show global entity-weighted implementation/reconciliation progress across active Trackers.
- Show current status distribution and implemented-over-time trend.
- Present Project cards with active Tracker/entity counts, implemented percentage, Ready, Blocked,
  Rework, unresolved, and last activity.
- Drill into the selected Project rather than mixing unrelated entities in one matrix.

### Project dashboard

- Show the same aggregate progress/trend for the Project.
- Present comparable Tracker cards side by side.
- Add a virtualized entity matrix: rows are normalized entity keys, columns are active Trackers, and
  cells show labeled development/work status plus issue indication.
- Show an explicit `Not present` state when a Tracker lacks an entity.
- Default the matrix to actionable differences and provide an explicit Show all option.
- Allow navigation from a matrix cell to that Tracker's Overview/details pane without changing data.

## Interaction behavior

- Dashboard filters/date ranges affect presentation/query parameters only.
- Selecting a Project/Tracker card changes navigation context predictably.
- Opening a comparison cell switches context only after normal dirty-state protection.
- Recycled Projects/Trackers and archived entities never contribute to active progress totals.
- Empty Projects and Trackers show clear zero/empty states rather than invalid percentages.
- Exports remain available from Tracker Reports; portfolio/project export is not required here unless
  it can reuse the established exporter without delaying the milestone.

## Architectural constraints

- No aggregate, history reconstruction, entity matching, or status calculation in WPF.
- Do not persist derived percentages or comparison matrices.
- Do not merge histories from unrelated Trackers as though they were one Tracker.
- Do not change status/readiness definitions.
- Do not implement Git synchronization, automatic publishing, PDF, or Word generation.

## Tests / verification

Automated tests must cover entity-weighted aggregation with differently sized Trackers, exclusion of
archived/recycled data, historical aggregation, no-data behavior, per-Tracker summaries, last
activity, normalized-key matrix matching, missing cells, actionable-difference filtering,
deterministic ordering, cell navigation state, existing tracker report regression, and PNG/clipboard
behavior.

Manual verification must cover one Project with three same-schema Trackers, unrelated Projects,
differently sized Trackers, divergent/missing entities, many matrix rows/columns, readable dates,
chart tooltips/legends, theme changes, resizing, DPI, keyboard navigation, and export readability.

## Acceptance criteria

- [x] Progress is renamed Reports without losing tracker-level charts or export.
- [x] Portfolio and Project dashboards show entity-weighted aggregate progress charts.
- [x] Per-Tracker context remains visible beside aggregates.
- [x] Recycled/archived state is excluded consistently.
- [x] Project comparison aligns sibling entities deterministically.
- [x] Missing entities and actionable differences are explicit.
- [x] The matrix defaults to actionable differences and can show all.
- [x] Dashboard/history values come from persisted Application/Reporting data.
- [x] Reports remain testable without WPF rendering.
- [x] No Git synchronization or new document-export format is introduced.
- [x] The complete solution builds and all tests pass.

## Out of scope

- comparing unrelated Projects entity by entity;
- custom formulas or weighting modes;
- saved dashboard configurations;
- portfolio/project PNG export unless trivially reusable;
- PDF/Word generation;
- Git synchronization or publishing.

## Agent planning prompt

```text
Plan UX-07 — Reporting and Tracker Comparison.

Read the architecture, both design guides, UX-01 through UX-06, Milestones 9 and 10, current
Reporting/Application query models, history persistence, charts/export, dashboards, and tests.
Produce a repository-specific UX-07-only plan. Define entity-weighted aggregation and normalized-key
comparison outside WPF. Do not modify the repository or design Git synchronization.
```

## Agent implementation prompt

```text
Implement UX-07 according to this milestone and the approved plan. Add tracker-scoped Reports,
entity-weighted Portfolio/Project charts, Tracker summaries, and the Project entity matrix. Keep all
calculations outside WPF, preserve existing chart/export behavior, add automated and screenshot
coverage, build the solution, and run all tests. Do not begin UX-08 or Git synchronization.
```

# UX-04 — Overview and Entity Details

## Context

UX-03 provides the Fluent shell and tracker context. The accepted Overview supports status summary
shortcuts, lazy search, bulk status changes, dependency issue indicators, and PF-05 header filtering
and status sorting, but its table exposes more fields than a manager needs at once.

## Goal

Redesign active and archived entity presentation around manager essentials, accessible status cues,
and a read-only details pane while preserving every accepted selection/filtering behavior.

## User-facing outcome

Managers can scan priorities, readiness, assignments, blockers, and progress quickly, then inspect
full entity context without entering edit mode or losing bulk selection.

## Design decisions

- Use hybrid admin density.
- Default active columns are Priority, Rank, Entity, Work status, Development status, Responsible
  developer, Group, Blockers, and Actions.
- Use labeled status badges; color is not the only signal.
- Open details from the entity name or an explicit View details action.
- Keep details read-only and editing kebab-only.
- Keep filters in column-header flyouts only; do not add a filter pane or chips.

## Required implementation

- Recompose the active table with the approved default columns and deterministic priority/rank
  ordering.
- Present Development status and Work status as distinct labeled badges.
- Present graph issues beside the entity name with accessible text, tooltip, and detailed issue
  content.
- Label the readiness dependency column `Blockers` and explain that it contains unresolved or not-yet-
  implemented direct dependencies.
- Retain compact summary cards and their single-status filter shortcuts.
- Retain the layered implementation progress summary with accessible legend text.
- Modernize PF-05 header flyouts without changing staged Apply/Clear, available-value, OR/AND,
  `(Blank)`, and typed status-sort semantics.
- Retain lazy entity/dependency search, Search button, `Ctrl+F`, clear/close, and result summary.
- Retain extended bulk selection, status selector, atomic Apply, outside-click deselection, and
  Escape deselection. Interacting with the bulk toolbar must not clear selection.
- Add a read-only right-side details pane that can show provenance, full notes, requested/effective
  priority, rank, development/work status, responsible developer, group, dependency counts, complete
  effective dependencies, incomplete/unresolved blockers, and available audit timestamps.
- Opening/closing the pane must not alter bulk row selection. Multi-selection may close or suppress
  the pane rather than ambiguously choosing one selected entity.
- Keep Archived as a dedicated destination with its independent search/filter/sort state, read-only
  details, and existing restore action.
- Preserve empty, filtered-empty, warning, operation-success, and error states.

## Interaction behavior

- Clicking an entity name opens its details; it does not open editing.
- Editing remains available only from the row kebab.
- Escape closes the nearest transient surface in order: column flyout/search/details, then row
  selection, without canceling unrelated work.
- Changing search, filter, sort, tracker, or destination clears hidden bulk selection.
- Total clears filters/sort but retains accepted text-search behavior.
- Summary counts describe the complete active Tracker, not only visible filtered rows.

## Architectural constraints

- Use existing Application overview/readiness results; do not recalculate blockers or priorities in
  WPF.
- Extend an Application query model only when the details pane needs persisted/domain information
  that is not currently projected.
- Filtering and presentation sorting remain WPF presentation state as accepted by PF-05.
- Do not add editable fields to the details pane.
- Do not change status definitions, ranking, readiness, or persistence.

## Tests / verification

Automated tests must preserve and extend coverage for summary shortcuts, search debounce,
entity/dependency search, filter staging/composition, typed sort order, default ordering restoration,
selection clearing, toolbar interaction, details-pane open/close/projection, graph issue labels,
archived independence, and supported filterable-column XAML configuration.

Manual verification must cover 125+ rows, multiple selection, scrolling, keyboard search/filtering,
long blockers/notes, narrow/wide windows, details-pane focus and closure, archived restore, and all
themes.

## Acceptance criteria

- [ ] The default table shows only the approved manager-essential columns.
- [ ] Development and Work status remain distinct and use labeled accessible badges.
- [ ] Blockers include every accepted incomplete or unresolved direct dependency.
- [ ] Full entity context is available in a read-only side pane.
- [ ] Details inspection does not become an alternate editing path.
- [ ] Bulk selection and deselection behavior remains correct.
- [ ] PF-05 filter/sort semantics remain unchanged and use header flyouts only.
- [ ] Search and keyboard shortcuts remain responsive and predictable.
- [ ] Archived inspection/restore remains available in its dedicated destination.
- [ ] No graph/readiness/business calculation is moved into WPF.
- [ ] The complete solution builds and all tests pass.

## Out of scope

- editable details pane or inline editing;
- user-configurable/reorderable columns;
- new filtering semantics or saved filters;
- import, creation, editor, or report redesign;
- Git synchronization.

## Agent planning prompt

```text
Plan UX-04 — Overview and Entity Details.

Read the architecture, design direction, UX-01 through UX-03, PF-01 through PF-05, Milestone 8, and
this milestone. Inspect the implemented overview/archived Views, table/filter ViewModels, row query
models, bulk selection code, graph issue presentation, and tests. Produce a repository-specific plan
for UX-04 only. Preserve accepted behavior and keep the details pane read-only. Do not modify the
repository or plan later UX milestones.
```

## Agent implementation prompt

```text
Implement UX-04 according to the approved design and plan. Redesign active/archived tables, status
badges, graph issue cues, and the read-only details pane while preserving PF filtering/sorting,
search, bulk selection, ranking, readiness, and restore semantics. Update automated and screenshot
coverage, build the solution, and run all tests. Do not begin UX-05.
```


# UX-06 — Entity Creation and Editing

## Context

EntityTracker supports manual creation, dependency search, deliberate unresolved dependencies,
manual dependency overrides, requested priority, assignment, groups, status/notes editing,
archive/restore, and cycle validation. UX-03 provides the shell and UX-04 adds a separate read-only
details pane.

## Goal

Make creation and editing focused, consistent Fluent workflows while preserving the accepted
distinction between inspection, creation, editing, archive, and restore.

## User-facing outcome

Users can create or correct entities with clear dependency suggestions and validation, while
destructive lifecycle actions remain difficult to trigger accidentally.

## Design decisions

- Add Entity remains a dedicated destination.
- The right-side details pane remains read-only.
- Editing remains accessible only through the entity row's kebab action.
- Editing uses a modal rather than inline cells or a persistent edit page.
- Archive remains a Coral danger action inside the edit modal and requires confirmation.

## Required implementation

### Add Entity

- Recompose the form into clear Identity/Planning and Dependencies sections.
- Preserve entity-name normalization and active/archived duplicate handling.
- Keep responsible developer, group, and requested priority optional.
- Use a Fluent suggestion popup for existing groups while allowing unmatched free text.
- Use a Fluent searchable suggestion popup for active dependency entities.
- Keep unmatched dependency text as an explicit `Add as unresolved` action, never an implicit
  placeholder entity.
- Show selected resolved and unresolved dependencies as labeled rows with accessible Remove actions.
- Show warnings and errors in compact scrollable surfaces without consuming the page.
- Keep Create disabled only for fatal validation; unresolved references remain warnings.
- Successful creation refreshes tracker/project summaries and navigates to the created entity in
  Overview. Cancel changes nothing.

### Entity editor

- Keep the modal focused and resizable/scrollable at ordinary window sizes.
- Group status/notes, assignment/group, requested/effective priority, and dependency overrides by
  task.
- Distinguish imported facts, manual additions, suppressions, resolved references, and unresolved
  names with text labels in addition to icons/color.
- Preserve searchable dependency/group suggestions and cycle/validation feedback.
- Keep priority impact preview dependency-safe and show unresolved names affecting the plan.
- Keep Save and Cancel consistently placed and restore focus to the invoking kebab after closure.
- Keep Archive in the modal's danger area. Its confirmation names the entity and explains that the
  action is reversible.
- Keep archived inspection read-only except for explicit Restore.

## Interaction behavior

- Opening the details pane never opens edit mode.
- Only the kebab Edit action opens the editor.
- Escape closes suggestions first, then confirmation/modal according to the established guarded
  behavior.
- Closing/canceling creation or editing persists nothing.
- Selecting a suggestion uses canonical stored casing; free text retains normalized accepted value.
- Creating a name that matches an archived entity offers the existing Restore path and cannot create
  a duplicate.
- Successful archive/restore refreshes Overview, Archived, Reports, and project summaries.

## Architectural constraints

- WPF gathers requests and presents results; validation, matching, dependency resolution, cycle
  detection, priority propagation, persistence, archive, and restore remain Application behavior.
- Reuse existing normalization and suggestion services; do not add UI-specific copies.
- Do not add inline editing, name editing, hard entity deletion, or bulk editing beyond accepted
  bulk status.
- Do not change dependency override or provenance semantics.
- Do not add Project/Tracker editing to entity forms.

## Tests / verification

Automated tests must preserve creation/editor Application tests and extend presentation tests for
suggestion staging, canonical group selection, explicit unresolved addition, duplicate/self/cycle
validation, dependency removal before Create, priority preview, modal open/close/focus state,
save/cancel, archive confirmation, archived restoration, and selected-Tracker refresh.

Manual verification must cover long entity/dependency names, many suggestions, keyboard-only
creation/editing, warnings/errors, modal sizing, nested scrolling, archive confirmation, restore,
Light/Dark/System, and DPI scaling.

## Acceptance criteria

- [ ] Add Entity remains a clear dedicated destination.
- [ ] Existing and unresolved dependencies are easy to distinguish and manage.
- [ ] Unresolved warnings remain non-fatal and deliberate.
- [ ] Group, assignment, and priority behavior is preserved.
- [ ] The editor remains kebab-only and modal.
- [ ] The details pane remains read-only.
- [ ] Imported facts and manual overrides remain distinguishable.
- [ ] Archive remains guarded and Restore preserves stable identity.
- [ ] Canceling any flow modifies nothing.
- [ ] No business validation or dependency logic moves into WPF.
- [ ] No new entity-editing product behavior is introduced.
- [ ] The complete solution builds and all tests pass.

## Out of scope

- inline editing;
- editing entity source identity/name;
- hard entity deletion;
- bulk dependency/priority/group/assignment editing;
- reusable entity templates or live linked Trackers;
- Git synchronization.

## Agent planning prompt

```text
Plan UX-06 — Entity Creation and Editing.

Read the architecture, Fluent direction, UX-01 through UX-05, Milestones 6.1, 7, and 8, PF-02
through PF-04, and this milestone. Inspect implemented creation/editor Views, ViewModels,
Application services, validation, suggestions, priority preview, archive/restore, and tests. Produce
a repository-specific UX-06-only plan. Preserve the read-only details/edit-modal boundary. Do not
modify the repository or plan later milestones.
```

## Agent implementation prompt

```text
Implement UX-06 according to this milestone and the approved plan. Modernize the Add Entity page and
kebab-only editor modal without changing normalization, unresolved dependencies, overrides,
priority, archive, restore, or persistence semantics. Update automated and screenshot coverage,
build the solution, and run all tests. Do not begin UX-07.
```


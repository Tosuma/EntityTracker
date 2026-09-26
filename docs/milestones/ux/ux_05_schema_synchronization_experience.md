# UX-05 — Schema Synchronization Experience

## Context

The accepted synchronization workflow already distinguishes Complete and Partial imports, builds a
candidate post-synchronization state, preserves unresolved references, reviews actionable changes,
and commits transactionally. UX-03 places the workflow inside the Fluent shell; this milestone
redesigns its information hierarchy and interaction.

## Goal

Make schema synchronization easier to understand and safer to review without changing any import,
matching, dependency, archive, or commit semantics.

## User-facing outcome

A manager can choose the correct import mode, review exactly what will change, recognize dangerous
removals and non-fatal unresolved references, and confidently Apply or Cancel.

## Design decisions

- Schema Synchronization remains a dedicated tracker-scoped destination.
- Complete remains default; Partial requires an explicit choice.
- The primary review shows actionable differences only.
- Missing/possibly removed uses Coral styling.
- Retained unresolved dependencies use the centralized yellow warning palette.
- SQL help remains independently accessible and secondary to import.

## Required implementation

- Present latest-import context and the selected Tracker clearly.
- Replace long mode descriptions with concise plain-language explanations:
  - Complete: absent existing entities will be proposed for archive.
  - Partial: absent existing entities are left unchanged.
- Make disabled mode states self-explanatory rather than merely non-interactive.
- Present Choose CSV and Review as the primary entry action and Get SQL Query as a secondary utility
  link/action.
- Preserve file-picker defaults, recent-import information, diagnostics, and CSV contract guidance.
- Recompose review content into clear New, Changed, Missing/possibly removed, Unresolved, and
  progress-impact sections with counts.
- Keep unchanged entities collapsed/summarized and optional to reveal.
- Show dependency additions and removals directly rather than side-by-side raw lists.
- Make archive impact and unusually large Complete-import removals prominent before Apply.
- Preserve the ability to stage dependency corrections for review rows using the existing editor
  workflow.
- Keep unresolved warnings non-fatal and clearly distinguish directly missing from upstream-blocked
  effects where available.
- Keep progress-impact choices understandable and grouped with the affected entities.
- Place Apply Changes and Cancel consistently, with clear busy/success/failure feedback.
- Retain deliberate low-speed scrolling for nested review content and support large imports without
  oversized warning boxes.

## Interaction behavior

- Choosing a CSV or building a plan changes no persisted state.
- Cancel discards the complete candidate/review and changes nothing.
- Apply remains disabled until the plan and all required review decisions are valid.
- Switching Tracker/destination with an unapplied review requires discard confirmation from UX-03.
- Complete/Partial selection is captured when the review is built; changing mode afterward requires
  rebuilding rather than silently reinterpreting the existing plan.
- Successful Apply refreshes the selected Tracker's Overview, Reports, and project summaries.

## Architectural constraints

- WPF presents `SchemaSynchronizationPlan`; it does not re-diff or resolve dependencies.
- Keep CSV parsing and diagnostics in Infrastructure/Application as currently established.
- Keep transactional application, candidate-state resolution, matching, archive decisions, and
  status impact outside WPF.
- Do not change Complete/Partial semantics, unknown-dependency behavior, identity preservation, or
  rank calculation.
- Do not implement cross-Tracker imports or Git synchronization.

## Tests / verification

Automated tests must retain all synchronization planner/service tests and cover ViewModel state for
mode selection, review-section visibility/counts, unchanged expansion, Apply/Cancel enablement,
discard confirmation, progress decisions, staged edits, operation feedback, and selected-Tracker
refresh.

Manual verification must cover no-snapshot/empty states, Complete and Partial reviews, identical
no-op import, large removal, mixed dependency changes, unresolved warnings, fatal diagnostics,
nested scrolling, cancellation, apply failure, keyboard operation, and all themes.

## Acceptance criteria

- [ ] Complete and Partial are explained plainly and preserve accepted semantics.
- [ ] Complete remains the default and Partial is deliberate.
- [ ] The review emphasizes actionable changes and summarizes unchanged entities.
- [ ] Dependency additions/removals are immediately understandable.
- [ ] Missing/removal and unresolved-warning presentation are visually distinct and accessible.
- [ ] Dangerous archive impact is prominent before Apply.
- [ ] Apply and Cancel retain transactional/no-change guarantees.
- [ ] Review corrections and progress decisions remain functional.
- [ ] Large reviews remain usable and scroll predictably.
- [ ] WPF contains no CSV, diff, matching, resolution, or persistence logic.
- [ ] No synchronization product behavior changes.
- [ ] The complete solution builds and all tests pass.

## Out of scope

- new import modes;
- automatic imports or scheduled synchronization;
- cross-Tracker batch imports;
- entity rename detection not already accepted;
- Git serialization, pull, push, merge, or conflict review;
- Add Entity or general editor redesign.

## Agent planning prompt

```text
Plan UX-05 — Schema Synchronization Experience.

Read the architecture, Fluent direction, UX-01 through UX-04, Milestones 5.1 and 6, the current
schema synchronization Views/ViewModels, confirmation service, diagnostics, scrolling code, and
tests. Produce a repository-specific plan for UX-05 only. Preserve every accepted synchronization
semantic and keep logic outside WPF. Do not modify the repository or plan Git synchronization.
```

## Agent implementation prompt

```text
Implement UX-05 according to this milestone and the approved plan. Redesign only the WPF
synchronization/import/review experience while preserving candidate-state resolution, Complete and
Partial behavior, staged corrections, transactional Apply, and cancel safety. Update automated and
screenshot coverage, build the solution, and run all tests. Do not begin UX-06 or Git sync.
```


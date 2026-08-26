# Product Feedback Milestone PF-01 — Bulk Status Updates

## Goal

Allow a Team Lead to select several entities in the overview and change their development status
in one operation.

## Behavior

- Reuse the overview grid's existing extended row selection. This milestone does not introduce a
  second selection model.
- Show the number of selected active entities together with a target-status selector and an
  explicit `Apply status` action.
- Support every persisted `DevelopmentStatus`: `Not started`, `In progress`, `Rework needed`,
  `Dev. completed`, and `Reconciled`.
- Apply the chosen status to the complete selection or make no changes. Archived, missing, or stale
  entity selections must fail validation before persistence rather than producing a partial update.
- Clear the selection when a refresh, search, or view change would otherwise leave hidden entities
  selected.
- Disable the bulk action while no active entities are selected or another modifying operation is
  running.

## Application and persistence

- Add a focused application operation that accepts a unique collection of `EntityId` values and
  one target `DevelopmentStatus`.
- Load and validate the selected entities before constructing one `TrackedStateChangeSet`.
- Include only entities whose status actually changes in `EntitiesWithProgressToUpdate`.
- Persist one status-history transition per changed entity with the operation timestamp, then
  calculate and persist one progress snapshot for the final batch state.
- Recompute readiness, blockers, progress counts, and the overview after the transaction commits.
- Return a result that reports the changed and unchanged counts so the UI can confirm what happened.

## Tests and verification

- Change three and five selected entities from mixed starting statuses to `In progress`.
- Verify every changed entity receives exactly one history transition and already-matching entities
  receive none.
- Verify readiness and blockers change correctly when several dependencies become completed or are
  moved back to an incomplete status together.
- Verify one final progress snapshot represents the whole operation rather than intermediate states.
- Verify duplicate IDs are rejected or normalized before persistence without duplicate history.
- Verify archived, removed, or stale selections cause no partial writes.
- Verify selection is cleared when filtering, searching, refreshing, or switching manager views.

## Acceptance criteria

- A user can select multiple active overview rows and set one status for all of them.
- The batch is atomic and status history remains complete and non-duplicated.
- Derived workflow and progress information reflects the final batch immediately.
- Archived entities and hidden stale selections cannot be modified accidentally.

## Out of scope

- Bulk editing notes, dependencies, priority, responsible developer, or group.
- Persisted selection sets or saved batch templates.
- Changes to the list of development statuses.

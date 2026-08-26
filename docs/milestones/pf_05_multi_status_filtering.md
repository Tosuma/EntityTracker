# Product Feedback Milestone PF-05 — Multi-Status Filtering

## Goal

Let a Team Lead filter the overview to several development status codes at once, such as
`Not started` together with `Rework needed`.

## Filter behavior

- Replace the active overview's single persisted-status choice with a multi-select status filter for
  `Not started`, `In progress`, `Rework needed`, `Dev. completed`, and `Reconciled`.
- Treat selected statuses as an OR filter: an entity is shown when its persisted status matches any
  selected value.
- Treat an empty selection as no status restriction and label that state `All statuses`.
- Apply entity-name or dependency-name search after the status filter, using AND semantics between
  the search and selected statuses.
- Keep `Ready`, `Blocked`, and `Archived` as separate manager views because the first two are derived
  workflow classifications and the last changes lifecycle scope rather than development status.
- Ignore the status selection while one of those separate views is active, remember it for the
  current application session, and reapply it when the user returns to the active status view.
- Keep summary counts based on the complete underlying active set so filtering does not change the
  headline totals.
- Reapply the current filter after refresh or a successful edit. Do not persist filter choices to the
  database or settings file in this milestone.

## Tests and verification

- Select only one status and verify behavior matches the existing single-status view.
- Select `Not started` and `Rework needed` and verify the result is the deduplicated union of both
  statuses.
- Select all statuses and select none; both must show every active entity.
- Combine multi-status filtering with entity-name and dependency-name search.
- Switch to `Ready`, `Blocked`, and `Archived`, then return and verify the prior status selection is
  restored.
- Change an entity's status and verify it enters or leaves the filtered result after refresh.
- Verify summary counts remain stable while the visible result count changes.

## Acceptance criteria

- A Team Lead can see any chosen combination of persisted status codes in one overview.
- Status combinations use predictable OR semantics and compose correctly with search.
- Derived readiness and archived views remain understandable and do not silently alter the selected
  status filter.
- Filtering changes presentation only and never modifies persisted entities.

## Out of scope

- Saved or shared filter presets.
- Filtering by priority, responsible developer, or group.
- Changes to status definitions or readiness rules.

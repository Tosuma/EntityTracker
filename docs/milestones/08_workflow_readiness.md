# Milestone 8 — Status, Readiness, and Blockers

## Goal
Turn the dependency graph into an implementation-planning tool.

## Tasks
- Finalize workflow statuses.
- Make `Ready` a derived value, not a manually maintained field.
- An entity becomes ready when all effective dependencies meet the configured completion rule.
- Change the overview's `Missing Dependencies` column to show every effective direct dependency
  that has not been implemented, rather than only unresolved references.
- Include unresolved dependency references in `Missing Dependencies` because they cannot be
  considered implemented while no active tracked entity resolves them.
- Show incomplete blockers for blocked entities.
- Recompute readiness after status/dependency changes.
- Add manager filters such as Ready, Blocked, In Progress, Completed, and Archived.
- Optionally show how many downstream entities each entity blocks.

## Missing Dependencies semantics

For this milestone, `Missing Dependencies` is an implementation-readiness view over an entity's
effective direct dependencies after manual additions and suppressions have been applied.

- A resolved dependency is included while its tracked entity does not meet the configured
  completion rule.
- A resolved dependency is omitted once its tracked entity meets the completion rule.
- An unresolved dependency name is always included. It has no tracked development status and
  therefore cannot satisfy the completion rule.
- Mandatory and optional effective dependencies are both included. A dependency kind does not
  mean that the dependency has already been implemented.
- Results should be deterministic and should not contain duplicate dependency names.
- Readiness requires this list to be empty.

The persisted dependency and unresolved-reference models remain distinct. Completing a dependency
changes readiness and the contents of this column; it does not remove the relationship from the
effective graph. For a direct dependency that is itself blocked, show that direct entity as the
blocker. Do not replace it with all of its transitive root causes in this column.

## Acceptance criteria
- Completing a dependency updates affected readiness automatically.
- Users can see exactly why an entity is blocked.
- `Missing Dependencies` includes every effective direct dependency that has not met the
  completion rule.
- `Missing Dependencies` includes unresolved dependency references.
- Completed dependencies remain in the effective graph but no longer appear in
  `Missing Dependencies`.
- A dependent entity cannot be `Ready` while `Missing Dependencies` contains any resolved or
  unresolved entry.
- Dependencies are not deleted merely because they are completed; completion changes readiness, not graph truth.

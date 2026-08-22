# Milestone 8 — Status, Readiness, and Blockers

## Goal
Turn the dependency graph into an implementation-planning tool.

## Tasks
- Finalize workflow statuses.
- Make `Ready` a derived value, not a manually maintained field.
- An entity becomes ready when all effective dependencies meet the configured completion rule.
- Show incomplete blockers for blocked entities.
- Recompute readiness after status/dependency changes.
- Add manager filters such as Ready, Blocked, In Progress, Completed, and Archived.
- Optionally show how many downstream entities each entity blocks.

## Acceptance criteria
- Completing a dependency updates affected readiness automatically.
- Users can see exactly why an entity is blocked.
- Dependencies are not deleted merely because they are completed; completion changes readiness, not graph truth.

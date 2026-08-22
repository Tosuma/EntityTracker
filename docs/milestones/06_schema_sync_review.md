# Milestone 6 — Safe Schema Synchronization and Import Review

## Goal
Update the tracked schema without destroying manual work.

## Tasks
- Diff imported schema against persisted schema.
- Categorize: new entities, dependency changes, unchanged, missing/possibly removed, and possible renames.
- Review only actionable changes rather than dumping every table.
- Allow edits to new/changed import candidates before commit.
- Show a concise summary such as `5 new, 3 changed, 1 missing`.
- Commit transactionally.
- Recompute ranking after commit.
- Soft-archive missing entities rather than automatically deleting them.
- Preserve existing status, notes, and history.

## Acceptance criteria
- Identical re-import is effectively a no-op.
- Five genuinely new tables create five new tracked entities.
- Existing progress stays attached after ranks shift.
- Canceling the review changes nothing.

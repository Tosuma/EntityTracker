# RESP-02 — Assignments and history

**Status: completed (2026-10-01).** Depends on RESP-01.

## Goal

Allow multiple developers to be responsible for one entity and show every period during which
each developer was assigned.

## Required behavior

- Model each assignment period with a stable ID, entity ID, developer ID, UTC start time, and
  optional UTC end time. An open period is a current assignment. The same developer may have
  several nonoverlapping periods on one entity; at most one may be open. Adding an already-current
  developer is a no-op. Removing and later re-adding creates a new period.
- Use the current time when an assignment is added or ended. Do not offer date editing or infer
  earlier assignment dates. Retiring a developer closes every open assignment for that developer
  at one operation time, including assignments on archived entities. Keep the developer and all
  closed periods. Restoring a developer does not reopen assignments.
- Replace the responsible text field in entity creation and editing with a searchable,
  multi-developer picker scoped to available developers in the current Project. Let users create
  a developer from the picker. Show current developers separately and a chronological assignment
  timeline in entity details. Archived details retain current and past assignment information.
- Preserve assignments through schema synchronization, dependency edits, priority changes,
  archive/restore, and Tracker copy within the same Project. A same-Project copy keeps its
  historical periods, remapped to copied entity and period IDs while retaining Developer IDs.
  A cross-Project copy omits responsibility history because Developers belong to the source Project.

## Migration and collaboration

- In one SQLite migration, split each old comma-separated responsible value, trim each part,
  discard blanks, and deduplicate case-insensitively per entity. Treat each value as initials;
  reuse an available Project developer with matching initials or create one. Capture one migration
  UTC time for the migrated open periods. Preserve the original spelling as initials. After a
  verified backfill, retire the scalar storage and its scalar merge contract.
- Persist periods and developer references atomically with entity edits and retirement. Validate
  Project ownership, chronological dates, and one open period per entity/developer. Include full
  history in backups and versioned snapshots. Convert older snapshots before merge; use stable
  identities for migrated records and surface incompatible timestamps or overlapping periods as
  reviewable conflicts rather than duplicating history.

## Tests and acceptance

- Verify multi-assignment, duplicate-add no-op, removal, reassignment, retirement, archive/restore,
  Tracker copy, and exact history after SQLite and snapshot round trips.
- Verify legacy empty, single, comma-separated, repeated, and shared-across-Tracker initials;
  ensure migration records its actual time and creates no fabricated earlier history.
- Verify older-snapshot import, independent Git edits, conflicting interval edits, and failure
  atomicity. The entity editor and details clearly distinguish current from past developers.

## Completion evidence

- SQLite schema version 18 stores assignment periods with stable IDs, UTC start/end times,
  Project/developer checks, interval overlap protection, and one open period per developer and
  entity. The migration splits and deduplicates legacy initials, reuses available Project
  Developers, records one migration time, and drops the scalar database column.
- Creating and editing an entity uses a searchable, multiple-selection Project Developer picker
  with inline Developer creation. Entity details and archived details show current Developers
  separately from the chronological timeline. Retirement closes open periods, including on
  archived entities; restoration does not reopen them.
- Project snapshot format 3 and SQLite backups contain full history. Older snapshots are converted
  at import/merge with stable identities. Collaborative merge reconciles periods by ID and raises
  reviewable conflicts for incompatible interval edits, overlaps, and retirement against a new
  assignment. Same-Project Tracker copies remap entity and period IDs; cross-Project copies omit
  responsibility history as agreed for Project-scoped Developers.
- Focused domain, migration, persistence, archive/restore, backup, snapshot, Tracker-copy, merge,
  picker, and WPF view tests were added or updated. `dotnet build EntityTracker.slnx --no-restore -v:q`
  succeeded with 0 warnings and 0 errors. `dotnet test EntityTracker.slnx --no-build -v:q`
  passed all 703 tests across seven projects (including 224 infrastructure and 185 WPF tests).
  The focused responsibility persistence suite passed 4 tests after the final changes.
- README and getting-started guidance were updated. The deterministic screenshot generator
  rendered 35 Light and 35 Dark images; the changed creation, editing, current details, and
  archived details views were inspected for readable labels, keyboard-accessible controls,
  bounded smooth picker scrolling, and legible presentation in both themes.

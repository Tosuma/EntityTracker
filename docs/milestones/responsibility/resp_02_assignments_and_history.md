# RESP-02 — Assignments and history

**Status: planned.** Depends on RESP-01.

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
  archive/restore, and Tracker copy. A copied Tracker keeps its historical periods, remapped to
  copied entity IDs while retaining the same Project developer IDs.

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

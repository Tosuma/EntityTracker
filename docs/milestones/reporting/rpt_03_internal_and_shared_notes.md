# RPT-03 — Internal Notes and Shared Notes

**Status: completed.**

## Goal and user-facing outcome

Every entity has two notes fields:

- **Internal notes** — only for us; never in a client report. This is today's single "Notes" field,
  renamed in the app; the stored data stays the same.
- **Shared notes** — notes we share with the client; shown in both reports.

(The name "Client notes" is avoided because it suggests notes written by the client.)

## Starting point and boundaries

RPT-01 is complete; the report already treats `Notes` as internal-only. Add the new field the same
way **Filter active** was added (about 64 references across Domain, SQLite, sync snapshot, merger,
Application services and WPF). The Overview table itself is not changed.

## Required implementation

- **Domain:** `TrackedEntity.SharedNotes` with `ChangeSharedNotes`; constructor parameter defaults to
  "" so existing callers are unchanged.
- **SQLite:** migration adds `shared_notes TEXT NOT NULL DEFAULT ''` to `tracked_entities` (as
  `filter_active` was, `SqliteDatabase.cs`); read and write it in `SqliteEntityRepository`,
  `SqliteTrackedStateStore` and `SqliteProjectTrackerStore` (Tracker copy).
- **Project sync:** `SnapshotEntity.SharedNotes = ""`; `ProjectSnapshotJsonCodec` writes it under a new
  snapshot format version (`ProjectSnapshot.CurrentFormatVersion` 4 → 5, following the
  `includeFilterActive` pattern) and reads older versions as empty. `ProjectSnapshotMerger` and
  `CollaborativeConflictField` merge it field by field like Notes. `SqliteProjectSnapshotStore`
  exports and applies it. The existing app-update gate keeps older apps from syncing format 5.
- **Application:** `EntityOverviewItem.SharedNotes`; every service that rebuilds an entity carries it
  so it is never lost (editor service, bulk status, lifecycle, schema synchronization planner,
  priority planning).
- **Editor:** Edit entity shows two labelled fields — "Internal notes" (helper text: only for us,
  never in a client report) and "Shared notes" (helper text: visible to the client in reports).
  Unsaved-change tracking, conflict review and the details pane show both.
- **Overview export (CSV/Excel):** the notes header becomes "Internal notes"; add "Shared notes".
- **Report:** `EntityTableSectionProvider` adds a `sharedNotes` "Shared notes" column (Everyone,
  searchable); `internalNotes` stays internal-only.

## Tests and acceptance

- Domain change; SQLite round trip and migration from a database without the column; snapshot codec
  version 5 round trip and version 4 read as empty; merger merges Shared notes and reports a conflict
  when both sides change it; editor saves both fields; export headers.
- End-to-end report: the client file contains Shared notes but not Internal notes.
- Markup: the editor shows both labelled fields.
- Build and run all test projects (Infrastructure for migration and snapshot); regenerate and publish
  screenshots (Edit entity changes); README explains Internal notes vs Shared notes.

## Completion evidence

- `TrackedEntity.SharedNotes` / `ChangeSharedNotes`; SQLite schema 20 adds `shared_notes`; the entity
  repository, tracked-state store (insert, progress update, fingerprint), Tracker copy and snapshot
  store read and write it.
- Snapshot format 5: `SnapshotEntity.SharedNotes`; the codec writes it from version 5 only when it
  is not empty, and reads versions 1–4 as empty. The validator accepts 1–5. The merger merges it field
  by field; merge review labels it "Shared notes" and Notes "Internal notes", in titles and details.
- Every service that rebuilds an entity carries it: editor service, bulk status, lifecycle,
  schema synchronization planner, priority planning, Git sync merge, Overview items and rows.
- Editor: "Internal notes" (only for us; never in a client report) and "Shared notes" (shared with
  the client in Project reports), with help text for screen readers; unsaved-change tracking covers
  both. Details pane shows both. Overview export: "Internal notes" and "Shared notes" columns.
- Report: a searchable "Shared notes" column for everyone; internal notes stay internal-only.
- Tests: domain; SQLite and codec round trip, version 4 read as empty, empty Shared notes left out
  of entity files; migration leaves existing entities empty; merger merge and conflict; editor saves
  both separately; details; CSV and Excel headers; markup labels; end-to-end client report contains
  Shared notes but not internal notes. Version-pinned tests moved to format 5/6 and schema 20/21.
- `dotnet build -c Release` succeeded. `dotnet test -c Release` passed all 1,049 tests: Domain 62,
  Application 250, DemoData 8, Reporting 86, Screenshots 12, Wpf 379, Infrastructure 252.
- README screenshots regenerated and published (the editor and details changed); README explains
  Internal notes vs Shared notes.

## Agent planning prompt

```text
Plan RPT-03 only. Read docs/milestones/reporting/00_README.md and this milestone. Trace how FilterActive was added end to end (TrackedEntity, SqliteDatabase migration, repositories, snapshot codec versioning, ProjectSnapshotMerger, CollaborativeConflictField, Application services, editor, details, export, report) and specify the same path for SharedNotes, the format-version change and the tests. Do not edit files or broaden the scope.
```

## Agent implementation prompt

```text
Implement RPT-03 according to its approved plan and this milestone. Add SharedNotes end to end (domain, SQLite migration, sync snapshot format 5 with backward reads, merge, services, editor with labelled Internal notes and Shared notes, export, report), keeping internal notes out of client reports. Build and test the complete solution, regenerate screenshots, and update the README.
```

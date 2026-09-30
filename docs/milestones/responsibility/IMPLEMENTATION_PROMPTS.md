# Responsibility implementation prompts

Use one prompt at a time in order. Each prompt is self-contained and points to its milestone's
requirements. The existing PF-03 responsible text field is implemented; RESP-02 replaces it.

## RESP-01 — Project developers

```text
Implement RESP-01 only. Read docs/milestones/responsibility/00_README.md and
docs/milestones/responsibility/resp_01_project_developers.md, then inspect the current Project,
SQLite, snapshot, Git merge, WPF navigation, and test patterns before editing.

Add Project-scoped Developer records with stable IDs, mandatory initials, optional display name,
and available/retired state. Build the Project Developers management experience. Enforce unique
initials among available developers within one Project; allow reuse after retirement, and reject
restoration when it would conflict. Persist and validate the records through SQLite, Project
snapshots, imports, and collaborative merge, including older snapshot compatibility. Keep the
existing responsible text field and entity workflows working until RESP-02.

Add focused domain, persistence, snapshot/merge, and ViewModel tests. Build the complete solution
and run the regression suite. Check accessibility and update visible guidance/screenshots where
affected. Once verified, mark RESP-01 completed in its milestone, the Responsibility roadmap,
and the cross-category status page; record the actual completion evidence. Do not implement
RESP-02 through RESP-04.
```

## RESP-02 — Assignments and history

```text
Implement RESP-02 only, after confirming RESP-01 is complete. Read
docs/milestones/responsibility/00_README.md and
docs/milestones/responsibility/resp_02_assignments_and_history.md. Inspect existing responsible
text storage, entity creation/editing, archive/restore, Tracker copy, snapshots, Git merge, and
tests before editing.

Replace the scalar responsible value with multiple Project Developer assignments. Give each
assignment period a stable ID, UTC start, and optional UTC end; keep all past periods. Adding an
already assigned developer is a no-op, while removing and re-adding creates a new period. End all
open assignments when a developer is retired. Provide a searchable multi-developer picker during
entity creation and editing, allow creating a Developer from it, and show current developers and
the chronological responsibility timeline in entity details.

Migrate existing comma-separated values as initials, reusing matching available Project Developers
and starting migrated assignments at the recorded migration time. Preserve assignments and history
through SQLite, synchronization, archive/restore, Tracker copy, backup, versioned snapshots, and
collaborative merge. Add focused migration, history, UI, persistence, and merge tests. Build and
run the full regression suite; verify accessibility. Update RESP-02 and roadmap status with actual
completion evidence. Do not implement RESP-03 or RESP-04.
```

## RESP-03 — Responsibility search

```text
Implement RESP-03 only, after confirming RESP-02 is complete. Read
docs/milestones/responsibility/00_README.md and
docs/milestones/responsibility/resp_03_responsibility_search.md. Inspect the active and archived
overview projections, filter flyouts, search modes, settings migration, and related tests.

Show current Developers as separate overview items. Make the Responsible dev column filter offer
individual Developers by stable ID, with OR matching within that column and a Blank option for
unassigned entities. Add a saved Search responsible names option to Settings, defaulted on for
new and migrated settings. Ordinary active and archived search should then match entity names,
Developer initials, or optional display names. Preserve the dedicated dependency-search mode.
Refresh results after assignment or Developer changes without retaining hidden bulk selections.

Test multiple Developers on one entity, blank and archived rows, same-name distinct IDs, filter
composition, settings round trips, search on/off behavior, and dependency-search isolation. Build
and run the full regression suite; verify accessibility. Update RESP-03 and roadmap status with
actual completion evidence. Do not implement RESP-04.
```

## RESP-04 — Local identity and Assign me

```text
Implement RESP-04 only, after confirming RESP-01 through RESP-03 are complete. Read
docs/milestones/responsibility/00_README.md and
docs/milestones/responsibility/resp_04_local_identity_and_assign_me.md. Inspect local settings,
Project switching, entity details/editor commands, assignment writes, and snapshot/sync
boundaries before editing.

Add a changeable You in this Project setting that selects one available Developer per Project,
defaulting to unset. Store the Project ID to Developer ID choices only in local application
settings; never include them in shared Project data, snapshots, or Git sync. Treat a retired or
missing selected Developer as unset without matching a replacement by initials. Add Assign me to
active entity details and the editor. It must add the selected Developer alongside others,
create no duplicate assignment period, stage changes in the editor until save, and guide users
to Settings when no valid choice exists.

Test Project switching, two installations choosing different Developers, stale choices, repeated
actions, editor cancellation, assignment timestamps, settings migration, and sync exclusion.
Build and run the full regression suite; verify accessibility. Update RESP-04 and roadmap status
with actual completion evidence.
```

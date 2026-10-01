# Responsibility milestones

The `RESP-` category replaces the single responsible-developer string with project developers,
time-bounded entity assignments, individual-developer search, and a local `Assign me` shortcut.
RESP-01 and RESP-02 are complete; RESP-03 and RESP-04 remain planned. The existing
[PF-03](../product-feedback/pf_03_responsible_developer.md) single-field feature remains the
starting point, not a milestone to rerun.

## Execution order

1. [RESP-01 — Project developers](resp_01_project_developers.md) — completed: shared developer records and management.
2. [RESP-02 — Assignments and history](resp_02_assignments_and_history.md) — completed: multiple current developers and dated assignment periods.
3. [RESP-03 — Responsibility search](resp_03_responsibility_search.md): per-developer filters and optional name search.
4. [RESP-04 — Local identity and Assign me](resp_04_local_identity_and_assign_me.md): a changeable, local developer choice per Project.

Implement in order. Each milestone must leave the solution runnable and preserve the Project and
Tracker boundaries established by the completed UX and Git-sync series. `Developer` means a
project-level planning record, not an authenticated application user. Developer and assignment
data are part of the shared Project; the app's choice of which developer is `you` is local and
never part of Project snapshots or Git sync.

## Shared verification

- Build the complete solution and run the regression suite after each implementation milestone.
- Test application rules below WPF, SQLite migrations and round trips, snapshot validation and
  collaborative merge, and the affected ViewModels.
- Check keyboard access, labels, focus, and Light and Dark presentation for new controls.
- Update user guidance and deterministic screenshots when visible workflows change.

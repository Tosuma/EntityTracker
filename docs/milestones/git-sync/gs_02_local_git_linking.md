# GS-02 — Existing Local Repository Linking

**Status: completed.** GS-01 was verified before implementation. The full solution builds and its regression suite passes.

## Goal and user-facing outcome

Let a user link an existing Project to an existing local Git working tree and manually commit canonical Project snapshots. A repository without an upstream receives local commits only. Ordinary editing, reporting, and navigation remain SQLite-only.

## Starting point and boundaries

Use the [GS-01 snapshot](gs_01_portable_project_snapshot.md) and [shared repository contract](00_README.md). The user creates or clones the repository and configures Git externally. EntityTracker must not clone, run `git init`, add a remote, switch branches, configure credentials, reset, stash, or force-push. GS-02 does not import an unknown Project, fetch or push a remote, or reconcile differing snapshots; those belong to GS-03 and GS-04.

## Required implementation

- Add a folder picker for an existing non-bare Git working tree. Validate system Git availability and version, resolved repository root, checked-out branch, clean working tree, configured commit identity, allowed tracked paths, and unique local association. Reject detached HEAD, unsupported shared repositories, a nested or wrong root, and a path already linked to another Project.
- Detect and record the current branch and existing upstream identity without altering Git configuration. If there is no upstream, keep the link local-only. Pause sync with corrective guidance when the checked-out branch or repository configuration later changes.
- Store link state independently of the Project row so a future deletion tombstone survives local purge. Persist local path, Project association, branch, upstream identity, last common commit, last revision and snapshot hash, last result, and sync status; store no credentials.
- Add an asynchronous, cancellable system-Git adapter with bounded output, timeouts, sanitized logs, and a per-working-tree lock. Run it outside interactive SQLite reads and saves. Stage only `.entitytracker/**`.
- Link a Project to an empty existing repository or one containing an identical valid snapshot. Defer a differing snapshot until GS-04 conflict review exists. Commit canonical snapshots only when bytes differ. Require approval for permanent outbound deletions relative to the sync base.
- Add a Project-dashboard repository card with folder selection, link, unlink, `Sync now`, branch/upstream, last result, and pending action. Unlink only removes local association; it does not delete SQLite data or repository contents.

## Interfaces and compatibility

Application owns `IProjectSyncLinkStore`, sync-link state and result models, and the transport boundary. Infrastructure owns link persistence and system-Git execution; WPF owns the folder picker and card. Existing Project operations do not call Git. Local links remain usable without a remote.

## Tests and acceptance

- Use temporary existing working trees to test link, clean and dirty states, identity errors, unsupported paths, branch changes, duplicate associations, unlink, idempotent commit, and stage-only-`.entitytracker` behavior.
- Verify an existing repository without an upstream receives manual commits, never fetches or pushes, and that ordinary SQLite operations remain responsive during a stalled Git process.
- Verify the adapter cannot issue clone, init, remote-add, branch-checkout, reset, stash, or force-push commands.
- Verify differing or invalid snapshots and unapproved outbound deletions stop without changing SQLite or overwriting the repository.
- Build the complete solution and run its regression suite.

## Completion record

GS-02 adds Project dashboard controls for linking an existing clean repository, manual local snapshot commits, and unlinking. Link state is stored in `%LOCALAPPDATA%\EntityTracker\git-sync-links.v1.json`, separately from SQLite and the older `repositories.json` file. The system Git adapter enforces a command allowlist, timeouts, bounded output, noninteractive execution, and a per-folder lock. It records branch and upstream identity but does not fetch or push. A repository with a differing or invalid snapshot is rejected; permanent outbound deletions require approval bound to a rechecked Project revision and repository state. SQLite editing and reporting do not invoke Git.

Temporary-repository tests cover clean and dirty linking, identity, unsupported paths, duplicate association, changed branch, identical and differing snapshots, invalid data, local-only and idempotent commits, stage scope, unlink, deletion rejection, and a stalled Git inspection concurrent with SQLite reading. `dotnet build EntityTracker.slnx --no-restore` and `dotnet test EntityTracker.slnx --no-restore --no-build` passed on 2026-09-27 (538 tests).

## Agent planning prompt

```text
Plan GS-02 only. Read GS-01 and the GS shared contract; inspect the implemented snapshot store, Project dashboard, settings/local persistence, and existing process execution patterns. Specify link validation, local commit, deletion approval, and test cases. Do not edit files or design GS-03 remote behavior.
```

## Agent implementation prompt

```text
Implement GS-02 according to its approved plan and this milestone. Link only user-prepared existing working trees, support manual local commits and unlink, and keep Git off normal SQLite save/read paths. Build and test the complete solution. Do not implement clone, init, remote fetch/push, import, or later GS milestones.
```

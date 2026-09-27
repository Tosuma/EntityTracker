# GS roadmap — Project Git synchronization

**Status: GS-01 completed; GS-02–GS-05 planned.** Portable Project snapshots are available through the application and infrastructure boundary. No Git synchronization feature is currently available.

The GS series adds full-fidelity Project collaboration through one user-managed, dedicated Git working tree per Project. SQLite remains the application's only runtime store: editing, reporting, navigation, and queries use SQLite. Git work runs asynchronously, outside interactive save and read paths. The completed [product](../product/00_README.md) and [UX](../ux/00_README.md) series provide the existing Project/Tracker foundation; the planned [product feedback](../product-feedback/00_README.md) series adds fields that snapshots must preserve when those fields exist.

## Execution order

1. [GS-01 — Portable Project Snapshot](gs_01_portable_project_snapshot.md) — completed: stable history identity, complete snapshot, validation, revision-aware SQLite application.
2. [GS-02 — Existing Local Repository Linking](gs_02_local_git_linking.md): select and link an existing clean working tree; commit locally by manual sync.
3. [GS-03 — Existing Checkout Import and Remote Sync](gs_03_existing_checkout_remote_sync.md): import from an externally cloned checkout; fetch and push non-divergent branches.
4. [GS-04 — Collaborative Merge and Conflict Review](gs_04_collaborative_merge.md): reconcile divergence, review conflicts, and publish or consume Project tombstones.
5. [GS-05 — Automatic Sync and Hardening](gs_05_automatic_sync_and_hardening.md): background scheduling, status, recovery, and defensive limits.

Complete and verify each milestone before beginning the next. Manual sync remains available after auto-sync is introduced.

## User-managed repository boundary

Users install and configure system Git, clone remote repositories or run `git init`, set remotes and upstreams, configure keys, host trust, credential helpers, and commit identity, and check out the desired branch **outside EntityTracker**. Linking or importing starts with a folder picker pointed at an existing non-bare Git working tree. EntityTracker does not accept remote URLs, clone, initialize repositories, add remotes, change Git configuration, switch branches, or manage credentials.

The future link flow resolves the selected repository root; requires a checked-out branch, clean working tree, and a unique Project association; detects an existing upstream; and rejects bare, detached, unsupported shared, or already-linked repositories. If branch, upstream, or configuration later changes externally, sync pauses with corrective guidance. A branch without an upstream receives local commits only. All Git operations are noninteractive; users repair authentication with Git outside the application.

## Version 1 repository format

Canonical, deterministic UTF-8 JSON lives under `.entitytracker/`:

```text
.entitytracker/
  manifest.json
  project.json
  trackers/{tracker-id}/tracker.json
  trackers/{tracker-id}/entities/{entity-id}.json
  trackers/{tracker-id}/status-history/{event-id}.json
  trackers/{tracker-id}/progress-history/{snapshot-id}.json
  trackers/{tracker-id}/schema-import-summary.json
  deleted-project.json
```

Version 1 preserves Project, Tracker, and entity IDs; UTC timestamps and audit data; relationships; lifecycle state; status and progress history; import metadata; and copy provenance. Entity documents include current state, imported and unresolved dependencies, and manual overrides. Serialization uses stable property and collection order without volatile export timestamps. `deleted-project.json` represents terminal deletion of a linked Project. EntityTracker stages only `.entitytracker/**`. Dedicated repositories may also track `README*`, `LICENSE*`, `.gitignore`, and `.gitattributes`; other tracked paths prevent linking.

## Synchronization and merge contract

Under a per-repository lock, validate the recorded branch and clean working tree; fetch the configured upstream if present; then read a consistent SQLite snapshot and base and remote Git snapshots. Validate format version, Project identity, relationships, and references before any SQLite change. Domain-merge base, local, and remote. Conflicts or unapproved outbound deletions stop before SQLite mutation. Back up SQLite only when inbound data will change it, apply the merged Project in a transaction with an expected-revision check, re-export committed SQLite state, create a canonical commit, and push when an upstream exists. Record success only after commit and applicable push succeed. A rejected push because the remote advanced returns through fetch and merge. Never force-push, reset, stash, discard a dirty tree, switch branches, or edit Git configuration.

One-sided changes win and identical changes collapse. Independent additions and set changes merge. Different edits to the same field or relationship require review. Delete versus unchanged deletes; delete versus modified conflicts. Status changes and their causal history branch are resolved together. Progress snapshots merge by stable ID and are followed by a snapshot of the merged state. Review supports per-conflict and bulk local/remote choices and revalidates the SQLite revision and remote head before application.

Permanent deletion relative to the sync base requires approval from the member publishing the exact deletion set. Auto-sync pauses until approval, and subsequent local changes invalidate it. Other members apply published deletions unless they concurrently modified the object. Project deletion uses a terminal tombstone that survives local purge long enough to publish and that receiving clients apply transactionally.

## Shared future architecture and verification

The planned design adds stable status-event and progress-snapshot IDs, causal status-history links, Project snapshots and revisions, sync results and state, typed conflicts and resolutions, and deletion approvals. Application owns `IProjectSnapshotStore`, `IProjectSyncLinkStore`, transport boundaries, and `ProjectSyncCoordinator`; Git, JSON, and SQLite implementations remain in Infrastructure. WPF owns repository selection and review presentation, never Git or merge rules. Settings version 5 holds only global auto-sync configuration; local link state is separate and credentials stay external.

Each future milestone must build the full solution and pass its relevant tests before the next starts. The series must cover exact snapshot round trips and migration; invalid data and tombstones; temporary local working trees and bare test remotes; non-fast-forward retry; merge and deletion matrices; revision-bound review; atomic import and rollback; scheduler behavior; accessible UI; and a stalled Git operation that leaves ordinary SQLite and UI work responsive. These are **future acceptance requirements**, not verification performed by this documentation task.

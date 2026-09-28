# GS roadmap — Project Git synchronization

**Status: GS-01–GS-05 completed.** Portable snapshots, existing-checkout import, manual and automatic remote synchronization, collaborative merge review, and Project tombstones are available.

The GS series adds full-fidelity Project collaboration through one user-managed, dedicated Git working tree per Project. SQLite remains the application's only runtime store: editing, reporting, navigation, and queries use SQLite. Git work runs asynchronously, outside interactive save and read paths. The completed [product](../product/00_README.md) and [UX](../ux/00_README.md) series provide the existing Project/Tracker foundation; the planned [product feedback](../product-feedback/00_README.md) series adds fields that snapshots must preserve when those fields exist.

## Execution order

1. [GS-01 — Portable Project Snapshot](gs_01_portable_project_snapshot.md) — completed: stable history identity, complete snapshot, validation, revision-aware SQLite application.
2. [GS-02 — Existing Local Repository Linking](gs_02_local_git_linking.md) — completed: select and link an existing clean working tree; commit locally by manual sync.
3. [GS-03 — Existing Checkout Import and Remote Sync](gs_03_existing_checkout_remote_sync.md) — completed: import from an externally cloned checkout; fetch and push non-divergent branches.
4. [GS-04 — Collaborative Merge and Conflict Review](gs_04_collaborative_merge.md) — completed: reconcile divergence, review conflicts, and publish or consume Project tombstones.
5. [GS-05 — Automatic Sync and Hardening](gs_05_automatic_sync_and_hardening.md) — completed: background scheduling, status, recovery, defensive limits, and single-instance protection.

Complete and verify each milestone before beginning the next. Manual sync remains available after auto-sync is introduced.

## User-managed repository boundary

Users install and configure system Git, clone remote repositories or run `git init`, set remotes and upstreams, configure keys, host trust, credential helpers, and commit identity, and check out the desired branch **outside EntityTracker**. Linking or importing starts with a folder picker pointed at an existing non-bare Git working tree. EntityTracker does not accept remote URLs, clone, initialize repositories, add remotes, change Git configuration, switch branches, or manage credentials.

The link and import flows resolve the selected repository root; require a checked-out branch, clean working tree, and a unique Project association; detect an existing upstream; and reject bare, detached, unsupported shared, or already-linked repositories. If branch, upstream, or configuration later changes externally, sync pauses with corrective guidance. A branch without an upstream receives local commits only. All Git operations are noninteractive; users repair authentication with Git outside the application.

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

Under a per-repository lock, validate the recorded branch and clean working tree; fetch the configured upstream if present; then read a consistent SQLite snapshot and base and remote Git snapshots. Validate format version, Project identity, relationships, and references before any SQLite change. Domain-merge base, local, and remote. Conflicts or unapproved outbound deletions stop before SQLite mutation. Back up SQLite only when inbound data will change it, apply the merged Project in a transaction with an expected-revision check, re-export committed SQLite state, create a canonical commit, and push when an upstream exists. Record success only after commit and applicable push succeed. A rejected push because the remote advanced returns through fetch and merge. Divergent changes to allowed files outside `.entitytracker/**` require command-line Git reconciliation before Project merge. Never force-push, reset, stash, discard a dirty tree, switch branches, or edit Git configuration.

One-sided changes win and identical changes collapse. Independent additions and set changes merge. Different edits to the same field or relationship require review. Delete versus unchanged deletes; delete versus modified conflicts. Status changes and their causal history branch are resolved together. Progress snapshots merge by stable ID and are followed by a snapshot of the merged state. Review supports per-conflict and bulk local/remote choices and revalidates the SQLite revision and remote head before application. Manual sync waits while the selected Project has an unfinished edit, resumes when that edit is saved or closed, and lets the user cancel the wait.

Permanent deletion relative to the sync base requires approval from the member publishing the exact deletion set. Auto-sync pauses until approval, and subsequent local changes invalidate it. Other members apply published deletions unless they concurrently modified the object. Project deletion uses a terminal tombstone that survives local purge long enough to publish and that receiving clients apply transactionally.

Automatic sync is enabled by default and starts one background pass after the UI becomes usable.
It processes linked Projects sequentially every five minutes by default; Settings offers 1, 5, 15,
30, and 60 minutes or an off switch. Manual Sync now remains available. An unfinished edit defers
only its Project and resumes when the edit closes. Conflicts and deletion approval require manual
review. Only action-needed automatic results appear in the notification center. The Project card
shows unlinked, idle, syncing, up-to-date, local-pending, deletion-approval, conflict,
authentication-required, configuration-invalid, and failed states.

## Architecture and verification

GS-01–GS-05 provide stable history IDs, Project snapshots and revisions, sync-link state, exact import, deletion approval, manual and automatic remote sync, typed conflict review, tombstones, and defensive bounds. Application owns snapshot, link, backup, transport, and scheduler policy; Git, JSON, and SQLite implementations remain in Infrastructure. WPF owns folder selection and user decisions, never Git commands. Local link state is separate from SQLite and credentials stay external. Settings v5 stores the global automatic schedule only.

The full solution builds with zero warnings and all 646 regression tests pass. Coverage includes snapshot round trips, migration, invalid data, temporary working trees and bare remotes, push retries, atomic import and rollback, SQLite responsiveness during stalled Git, merge and deletion matrices, revision-bound conflict review, collaborator convergence, scheduler timing and isolation, instance protection, size bounds, and symlink rejection.

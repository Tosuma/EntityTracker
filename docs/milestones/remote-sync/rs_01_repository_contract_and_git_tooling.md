# RS-01 — Repository Contract and Git Tooling

## Goal

Define and prove the versioned Project repository contract and safe installed-Git boundary without
changing Project authority or exposing synchronization UI.

## Required implementation

### Repository contract version 1

- Serialize UTF-8 JSON without a BOM, use LF line endings, stable property order, lowercase GUIDs,
  invariant enum names and UTC timestamps, and deterministically sorted collections.
- Use the following managed tree:

  ```text
  entitytracker-project.json
  trackers/<tracker-id>/tracker.json
  trackers/<tracker-id>/entities/<entity-id>.json
  operations/<operation-id>.json
  tombstones/projects/<project-id>.json
  tombstones/trackers/<tracker-id>.json
  tombstones/entities/<entity-id>.json
  ```

- The root document identifies `entitytracker-project`, format version 1, and the complete Project
  metadata. Tracker and entity documents preserve stable IDs, lifecycle, provenance, priority,
  responsible developer, group, notes, imported dependency declarations, and manual overrides.
- Operation documents are append-only and contain the stable operation ID, kind, UTC occurrence,
  affected identities, status-history transitions, and import-summary metadata needed by accepted
  reporting behavior.
- Tombstones preserve the deleted identity, kind, deletion operation ID, and UTC deletion time.
- Do not serialize ranks, readiness, blockers, resolved/unresolved dependency projections,
  progress snapshots, dashboard totals, or comparison results.

### Identity and persistence preparation

- Add a backend-neutral operation GUID to status history and persistence so events can be unioned
  idempotently. One bulk/import operation may own multiple entity history entries.
- Add an additive SQLite migration that assigns stable operation IDs to existing history exactly
  once and preserves all accepted data.
- Validate all documents into backend-neutral state before projecting or comparing them. Reject
  unknown/newer format versions, duplicate identities, cross-Project Tracker references, invalid
  domain values, duplicate normalized active names, inconsistent tombstones, and path/ID mismatch.
- Write managed files through same-directory temporary files and atomic replacement. Failed writes
  must leave the previous tree intact.

### Installed Git boundary

- Add an Infrastructure Git command client using `ProcessStartInfo.ArgumentList`, redirected
  output, cancellation, bounded execution, and no shell.
- Require Git 2.40 or newer and expose typed results for availability, version, repository status,
  identity, current branch, HEAD, upstream, remotes, ancestry, fetch, stage, commit, and push.
- Pass configuration that disables hooks and pagers for every app-issued command. Never execute
  aliases, repository scripts, text conversion filters, credential-bearing command arguments, or
  custom remote helpers.
- Validate a non-bare repository owned by the current user, a clean worktree, one checked-out
  branch, configured author name/email, no nested repository, no submodule/gitlink, and no reparse
  point within managed paths.
- Permit no visible Project linking yet.

## Tests and verification

- Golden-file and property tests prove byte-for-byte deterministic round trips for every persisted
  field, Unicode text, empty collections, recycled state, archived entities, dependencies,
  overrides, operations, and tombstones.
- Migration tests preserve old SQLite history and produce stable non-null operation IDs.
- Validation tests reject malformed JSON, traversal, casing/path mismatch, duplicates, unsupported
  versions, symlinks/reparse points, submodules, dirty repositories, missing identity, and old Git.
- Git integration tests use temporary repositories and verify cancellation, output bounds, hook
  suppression, argument safety, and typed error classification without network access.
- Build the complete solution and run all tests.

## Acceptance criteria

- Repository v1 can represent and restore all accepted Project/Tracker state without derived data.
- Equivalent state always produces identical managed bytes.
- Installed Git is isolated to Infrastructure and cannot invoke repository hooks through supported
  operations.
- Current application behavior and UI remain unchanged.

## Out of scope

Repository registration, Project export, authoritative Git writes, remotes, Sync, merge, and
conflict review.

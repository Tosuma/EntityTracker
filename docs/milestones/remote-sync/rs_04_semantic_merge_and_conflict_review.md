# RS-04 — Semantic Merge and Conflict Review

## Goal

Complete the Sync contract for diverged branches through domain-aware three-way merge, automatic
non-overlapping integration, and explicit review of true conflicts.

## Required implementation

### Three-way merge

- Resolve the merge base and load fully validated base, local, and fetched-remote Project states.
- Merge by stable identity and independently merge Project/Tracker metadata; entity source name,
  status, notes, lifecycle, provenance, requested priority, responsible developer, and group;
  imported dependency declarations; and manual overrides by normalized dependency source key.
- Union identical append-only operations/history by operation ID. The same operation ID with a
  different payload is a conflict.
- Automatically accept a field/key changed on only one side. Identical changes on both sides are
  not conflicts.
- Treat incompatible same-field/key edits, edit-versus-recycle/delete, identity reassignment,
  distinct IDs claiming the same normalized active name, and incompatible tombstones as conflicts.
- A deletion tombstone merges over unchanged stale state but conflicts with a concurrent edit,
  preventing deleted content from being silently resurrected.
- Recompute resolution, ranking, readiness, blockers, progress, and reporting from the merged state.

### Conflict review and commit

- Extend or replace the inactive collaborative-conflict models only with backend-neutral Project,
  Tracker, entity, field, dependency, history, and tombstone conflict payloads required here.
- Add a Fluent conflict-review surface grouped by identity and field/key. Show base, local, and
  remote values as text, label lifecycle/destructive meaning without color alone, require a choice
  for every conflict, and support complete keyboard navigation.
- Cancel leaves HEAD, worktree, registry, and SQLite unchanged.
- Before applying resolutions, fetch again and verify the expected remote tip. If it moved, discard
  the pending result and recalculate instead of applying a stale decision.
- Create one two-parent merge commit containing only the canonical semantic result and a new merge
  operation ID. Then rebuild SQLite and perform a normal fast-forward push.
- If push is rejected after the merge commit, retain the local merge and return to needs-sync; never
  rewrite it or force the remote.

## Tests and verification

- Cover non-overlapping changes to every field, dependency key, different entities/Trackers,
  append-only history, duplicate normalized names, edit/delete, recycle/edit, changed tombstones,
  malformed operations, and equal changes.
- Use two clones for automatic merge, every conflict category, canceled review, stale resolution,
  merge-commit parentage, push rejection, restart, and eventual successful Sync.
- Verify stable IDs/history, no duplicate events, no resurrection, deterministic merged bytes, and
  recomputed derived projections.
- Add view-model, accessibility, focus, Light/Dark, and deterministic screenshot coverage.
- Build the complete solution and run all tests.

## Acceptance criteria

- Non-overlapping concurrent work merges automatically without last-writer-wins data loss.
- Every true conflict requires an explicit current resolution and can be canceled safely.
- Successful divergence integration creates a two-parent merge commit and normal push.
- Stable identity, history, lifecycle, and deletion semantics survive multi-client Sync.

## Out of scope

Arbitrary text-file merging, manual repository editing, rebasing, force pushing, branch selection,
and provider-specific pull-request workflows.

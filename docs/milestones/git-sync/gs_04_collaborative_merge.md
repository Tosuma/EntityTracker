# GS-04 — Collaborative Merge and Conflict Review

**Status: planned.** GS-01 through GS-03 must be complete and verified before this milestone begins.

## Goal and user-facing outcome

Members working on the same Project can merge independent changes, review true conflicts, and converge on the same repository and SQLite state without guessing or losing work.

## Starting point and boundaries

Use the versioned snapshots, revision-aware SQLite store, existing-checkout links, and remote transport from earlier GS milestones. Apply the [shared merge and deletion contract](00_README.md#synchronization-and-merge-contract). Do not add periodic automatic sync; that belongs to [GS-05](gs_05_automatic_sync_and_hardening.md).

## Required implementation

- Add a domain-level three-way merger using base, local SQLite, and remote repository snapshots. Represent typed conflicts for Project and Tracker metadata, entities, dependencies and other relationships, lifecycle changes, status-history branches, progress history, and deletions. Use reviewed two-way reconciliation when the same Project ID exists without a shared base.
- Merge independent additions and set changes; collapse identical edits; accept a one-side-only edit. Conflicting edits to one field or relationship require review. Delete versus unchanged deletes; delete versus modified conflicts. Resolve status state with its causal history branch. Merge progress snapshots by stable ID and append a snapshot of the merged state.
- Add an accessible conflict-review modal with object context and base/local/remote values. Permit local or remote choice per conflict and bulk choice, then validate the resulting snapshot. Invalidate stale reviews when the SQLite Project revision or remote head changes.
- Defer inbound application while the selected Project has unsaved presentation edits. Manual sync offers save, discard, and cancel before proceeding.
- Back up SQLite when inbound changes will alter it. Apply the merged Project transactionally with an expected-revision check, re-export the committed SQLite state, and create a canonical Git merge commit with both heads as parents before pushing. A rejected push returns to fetch and merge without force-pushing.
- Publish a linked Project's permanent deletion through `deleted-project.json`, retaining link state after local purge until publication completes. Receiving clients validate the tombstone, transactionally purge and unlink the Project, and stop for review if they concurrently changed it. Require publisher approval of an exact outbound deletion set; subsequent Project changes invalidate approval.

## Interfaces and compatibility

Domain owns merge semantics; Application owns typed conflicts, resolution, deletion approval, and revision/head validation. Infrastructure owns JSON, Git, SQLite, and backup execution. WPF owns review presentation only. Keep normal editing and reporting on SQLite, and preserve the user-managed Git boundary.

## Tests and acceptance

- Exercise the complete three-way matrix for independent, identical, and conflicting field and relationship edits; additions; status/history branches; progress snapshots; lifecycle changes; entity/Tracker/Project deletion; and two-way same-ID reconciliation.
- Verify exact deletion approval, approval invalidation, inbound tombstones, concurrent modifications, stale SQLite revisions, stale remote heads, and transactional rollback. Verify the merge commit has both expected parents.
- Test keyboard and screen-reader context, bulk and per-conflict decisions, and save/discard/cancel behavior for unsaved edits.
- Verify independent collaborators converge to identical canonical repository and SQLite state. Build the complete solution and run its regression suite.

## Agent planning prompt

```text
Plan GS-04 only. Read the GS contract and completed GS-01–GS-03 implementation; inspect snapshot identities, local link state, repository transport, lifecycle operations, and WPF modal conventions. Specify merge and deletion matrices, review invalidation, and convergence tests. Do not edit files or plan GS-05 scheduling.
```

## Agent implementation prompt

```text
Implement GS-04 according to its approved plan and this milestone. Add domain merge, accessible review, revision/head revalidation, canonical merge commits, and Project tombstones. Keep SQLite transactional and Git external to interactive paths. Build and test the complete solution. Do not start automatic sync or GS-05.
```

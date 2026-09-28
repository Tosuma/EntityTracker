# GS-04 — Collaborative Merge and Conflict Review

**Status: completed.** GS-01 through GS-03 were complete and verified before implementation.

## Goal and user-facing outcome

Members working on the same Project can merge independent changes, review true conflicts, and converge on the same repository and SQLite state without guessing or losing work.

## Starting point and boundaries

Use the versioned snapshots, revision-aware SQLite store, existing-checkout links, and remote transport from earlier GS milestones. Apply the [shared merge and deletion contract](00_README.md#synchronization-and-merge-contract). Do not add periodic automatic sync; that belongs to [GS-05](gs_05_automatic_sync_and_hardening.md).

## Required implementation

- Add a domain-level three-way merger using base, local SQLite, and remote repository snapshots. Represent typed conflicts for Project and Tracker metadata, entities, dependencies and other relationships, lifecycle changes, status-history branches, progress history, and deletions. Use reviewed two-way reconciliation when the same Project ID exists without a shared base.
- Merge independent additions and set changes; collapse identical edits; accept a one-side-only edit. Conflicting edits to one field or relationship require review. Delete versus unchanged deletes; delete versus modified conflicts. Resolve status state with its causal history branch. Merge progress snapshots by stable ID and append a snapshot of the merged state.
- Add an accessible conflict-review modal with object context and base/local/remote values. Permit local or remote choice per conflict and bulk choice, then validate the resulting snapshot. Invalidate stale reviews when the SQLite Project revision or remote head changes.
- Defer inbound application while the selected Project has unsaved presentation edits. Manual sync waits until the edit is saved, discarded, or cancelled, then resumes; the user can cancel the waiting sync.
- Back up SQLite when inbound changes will alter it. Apply the merged Project transactionally with an expected-revision check, re-export the committed SQLite state, and create a canonical Git merge commit with both heads as parents before pushing. A rejected push returns to fetch and merge without force-pushing.
- Publish a linked Project's permanent deletion through `deleted-project.json`, retaining link state after local purge until publication completes. Receiving clients validate the tombstone, transactionally purge and unlink the Project, and stop for review if they concurrently changed it. Require publisher approval of an exact outbound deletion set; subsequent Project changes invalidate approval.

## Interfaces and compatibility

Domain owns merge semantics; Application owns typed conflicts, resolution, deletion approval, and revision/head validation. Infrastructure owns JSON, Git, SQLite, and backup execution. WPF owns review presentation only. Keep normal editing and reporting on SQLite, and preserve the user-managed Git boundary.

## Tests and acceptance

- Exercise the complete three-way matrix for independent, identical, and conflicting field and relationship edits; additions; status/history branches; progress snapshots; lifecycle changes; entity/Tracker/Project deletion; and two-way same-ID reconciliation.
- Verify exact deletion approval, approval invalidation, inbound tombstones, concurrent modifications, stale SQLite revisions, stale remote heads, and transactional rollback. Verify the merge commit has both expected parents.
- Test keyboard and screen-reader context, bulk and per-conflict decisions, and waiting, resumption, and cancellation around unsaved edits.
- Verify independent collaborators converge to identical canonical repository and SQLite state. Build the complete solution and run its regression suite.

## Completion record

GS-04 adds typed three-way Project reconciliation across metadata, lifecycle, entities, relationships, status branches, and progress history. Independent changes merge, competing changes use an accessible modal with base, local, and remote context and individual or bulk choices. Reviews recheck the SQLite revision and Git head before applying. Manual sync waits for unfinished edits and resumes when the edit closes; the waiting sync can be cancelled.

Merged inbound state is backed up, applied with an expected-revision transaction, re-exported from SQLite, and committed before push. Divergent heads produce a canonical two-parent merge commit. A checkout already fast-forwarded to upstream reconciles separate SQLite edits against the recorded common commit. Rejected pushes fetch and retry without force-pushing. Changes to allowed repository files outside `.entitytracker/**` on both sides stop with command-line Git guidance before SQLite mutation.

Linked Project purge retains a durable pending tombstone in the local link record until publication. The publisher approves the exact deletion set, including nested objects; a remote advance requires fresh approval. Receivers validate and apply tombstones transactionally, with review when local Project state changed. Keeping local state leaves an unlinked Project and a clear shared-deletion notice. No SQLite schema migration was required; the optional deletion intent is backward compatible with existing link files.

Temporary independent checkouts and bare remotes verify convergence, two-parent commits, same-head reconciliation, stale reviews, exact deletion approval, tombstone publication and recovery, revision rollback, and edit waiting and cancellation. The conflict review and repository screenshots were regenerated in light and dark themes and visually checked; the README path is generic while the fixture remains in a temporary workspace. `dotnet build EntityTracker.slnx --no-restore` passed with zero warnings, and `dotnet test EntityTracker.slnx --no-build --no-restore` passed all 591 tests on 2026-09-27.

## Agent planning prompt

```text
Plan GS-04 only. Read the GS contract and completed GS-01–GS-03 implementation; inspect snapshot identities, local link state, repository transport, lifecycle operations, and WPF modal conventions. Specify merge and deletion matrices, review invalidation, and convergence tests. Do not edit files or plan GS-05 scheduling.
```

## Agent implementation prompt

```text
Implement GS-04 according to its approved plan and this milestone. Add domain merge, accessible review, revision/head revalidation, canonical merge commits, and Project tombstones. Keep SQLite transactional and Git external to interactive paths. Build and test the complete solution. Do not start automatic sync or GS-05.
```

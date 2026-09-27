# GS-03 — Existing Checkout Import and Remote Sync

**Status: planned.** GS-01 and GS-02 must be complete and verified before this milestone begins.

## Goal and user-facing outcome

After cloning and configuring a repository outside EntityTracker, a new member can select its existing working-tree directory and import the Project into SQLite with the same Trackers, stable IDs, current and recycled state, and full history. A linked branch with an existing upstream can manually fetch and push non-divergent changes.

## Starting point and boundaries

Use the [GS shared contract](00_README.md) and GS-01 snapshot validation. The user owns clone/init, remote and upstream configuration, keys, credentials, host trust, and branch checkout. EntityTracker accepts only a local folder, never a remote URL or clone destination. GS-03 handles only non-divergent histories; reviewed reconciliation and divergent merges belong to [GS-04](gs_04_collaborative_merge.md).

## Required implementation

- Import a valid Project snapshot from a selected, clean, non-bare working tree into SQLite atomically, preserving Project, Tracker, entity, relationship, lifecycle, and history IDs and import metadata. Reject invalid format versions, broken references, detached HEAD, branch mismatch, dirty worktrees, and Project tombstones before SQLite mutation.
- If the Project ID already exists locally, route an identical or non-divergent state to linking and defer a differing state for GS-04 reconciliation rather than duplicating it. A repository containing a different Project ID cannot be linked to the selected local Project; offer importing it separately. If another local Project uses the imported name, ask for a unique local name while retaining the repository Project ID.
- Detect and use the selected branch's existing upstream. Implement noninteractive fetch, fast-forward inbound application, canonical commit, and push for non-divergent history. Recheck branch, upstream, cleanliness, and remote head at the appropriate boundaries. A missing upstream means local commits only until the user configures Git externally and refreshes or relinks.
- A rejected push because the remote advanced returns through fetch and validation. Retry if the result remains non-divergent; otherwise pause for GS-04 review. Do not force-push. Authentication or key failures report a clear action to verify access with command-line Git outside EntityTracker, then retry.
- Apply inbound data with the GS-01 expected-revision transaction and a retained pre-apply SQLite backup only when it changes SQLite. Never record a successful remote sync before the push succeeds.

## Interfaces and compatibility

Application coordinates snapshot validation, Project ID/name decisions, revision checks, and sync results. Infrastructure performs Git and SQLite work behind those interfaces. WPF supplies folder selection and user decisions; it never implements Git commands. Existing local-only links from GS-02 continue committing without an upstream.

## Tests and acceptance

- Use externally initialized temporary working trees and bare test remotes for import, fetch, fast-forward application, commit, push, upstream detection, missing upstream, dirty trees, detached HEAD, branch mismatch, authentication failure, and non-fast-forward retry.
- Verify exact IDs and full history after importing into an empty catalog. Cover same-ID and same-name collisions, invalid snapshots, unsupported versions, tombstones, atomic rollback, and pre-apply backup behavior.
- Verify no clone, init, remote-add, branch-checkout, reset, stash, or force-push command can be issued by the Git adapter.
- Verify a stalled Git transport cannot delay ordinary SQLite reads, writes, or navigation. Build the complete solution and run its regression suite.

## Agent planning prompt

```text
Plan GS-03 only. Read the GS contract and completed GS-01/GS-02 implementation, Project creation and naming rules, SQLite backup behavior, and link UI. Specify existing-checkout import, same-ID/name handling, non-divergent remote sync, and failure tests. Do not edit files or plan collaborative merge implementation.
```

## Agent implementation prompt

```text
Implement GS-03 according to its approved plan and this milestone. Import from user-selected existing working trees and support non-divergent fetch/commit/push through system Git. Preserve IDs, transactional SQLite behavior, and local-only links. Build and test the complete solution. Do not clone, initialize, reconfigure Git, or start GS-04.
```

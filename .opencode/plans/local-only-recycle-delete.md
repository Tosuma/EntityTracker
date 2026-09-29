# Make recycle-bin project deletion local-only (no auto-publish to remote)

## Goal

Deleting a linked project from the recycle bin must only remove it from the local
application tracking. It must **never** delete the shared/remote repository data
automatically. Remote deletion remains available, but only when a user explicitly
clicks "Publish deletion" in the Portfolio view.

## Current state

Recycle-bin delete (`CatalogManagementViewModel.ConfirmPurgeAsync` ->
`PurgeLinkedProjectAsync` in
`src/EntityTracker.Application/GitSync/ProjectGitSyncMerge.cs:61-101`) only purges
the local snapshot and records a `PendingDeletion` intent (link status
`PendingDeletion`). It does not push to the remote itself.

The remote deletion happens in `PublishTombstoneAsync`
(`ProjectGitSyncMerge.cs:103-187`), invoked via `SyncNowAsync` when the local
snapshot is gone and `link.PendingDeletion is not null`
(`ProjectGitSync.cs:326-366`).

`SyncNowAsync` is reached from two places:
- Manual: Portfolio "Publish deletion" button -> `ShellViewModel.PublishPendingDeletionAsync`.
- Automatic: `ProjectAutoSyncService.RunPassAsync` -> `RunProjectAsync` -> `_sync`.

The bug: `ProjectAutoSyncService.RunPassAsync`
(`src/EntityTracker.Application/GitSync/ProjectAutoSyncService.cs:139`) skips only
`RemoteDeletedLocalKept` and `Deleted`. `PendingDeletion` links are **not** skipped,
so auto-sync publishes pending deletions to the remote on the next pass, deleting the
shared data for everyone.

## Proposed change

### 1. `src/EntityTracker.Application/GitSync/ProjectAutoSyncService.cs`

In `RunPassAsync`, after the existing skip for `RemoteDeletedLocalKept` / `Deleted`
and after `_states.TryAdd(...)` (so the UI still records `LocalPending`), add a
skip for links that have a pending deletion:

```csharp
if (link.PendingDeletion is not null) continue; // never auto-publish deletions
```

This prevents auto-sync from invoking `SyncNowAsync` (and thus
`PublishTombstoneAsync`) for these links. The manual "Publish deletion" button is
unaffected because it calls `SyncNowAsync` directly via
`ShellViewModel.PublishPendingDeletionAsync`.

No change is needed in `PurgeLinkedProjectAsync`, `PublishTombstoneAsync`, or
`SyncCoreAsync`; the manual path and its guards are unchanged.

### 2. Test: `tests/EntityTracker.Application.Tests/GitSync/ProjectAutoSyncServiceTests.cs`

Add a test that a link with `PendingDeletion` set (and `SyncStatus ==
"PendingDeletion"`) is **not** passed to the sync delegate on `RunPassAsync`, and
that its recorded state is `LocalPending` (not `Syncing`/`UpToDate`/`Failed`).
Use a counting delegate and assert it was never called for that project, while a
normal `Pending` link is still synced.

## Risks / open questions

- A user who wants to delete the shared data must now remember to click "Publish
  deletion" explicitly. This is the intended tradeoff per the user's choice.
- The `WaitForEditAndRetryAsync` path in `ProjectAutoSyncService` can also call
  `RunProjectAsync`, but only for links that previously threw
  `ProjectSyncEditDeferredException`. Pending-deletion links are skipped before any
  sync, so they never enter that path. No change needed there.
- Confirm the exact `ProjectSyncLink` positional order used by the test `Link(...)`
  helper (10th positional arg is `PendingDeletion`) when writing the new test.

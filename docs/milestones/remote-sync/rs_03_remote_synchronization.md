# RS-03 — Remote Synchronization

## Goal

Add a safe, explicit Project Sync for non-diverged HTTPS/SSH upstreams while preserving offline
commits and clearly blocking divergence until semantic merge is implemented.

## Required implementation

- Use the managed branch's configured upstream; do not require the remote to be named `origin`.
- Accept HTTPS and SSH URLs without embedded credentials. Reject filesystem, `git://`, `ext`, custom
  remote-helper, and other production transports.
- Delegate authentication to installed Git, its configured credential helpers, SSH agent, and
  host-key policy. Persist no password, token, key, certificate, or credential-bearing URL.
- Add one Project-scoped **Sync** command. Do not fetch on startup, timers, navigation, or ordinary
  saves.
- Sync performs fetch and classifies the commit graph:
  - equal tips: report up to date;
  - remote is an ancestor: push the managed branch normally;
  - local is an ancestor: fast-forward, validate the fetched tree, and rebuild the cache;
  - tips diverged: do not change HEAD or SQLite and present a safe “merge required” state owned by
    RS-04.
- Never force-push, rebase published commits, delete remote refs, switch branch, or push more than
  the one configured branch.
- If a normal push is rejected because the remote moved after fetch, preserve all local commits and
  return to a needs-sync state.
- Present last successful fetch/push time, ahead/behind state, offline/authentication errors, missing
  upstream, invalid remote state, canceled work, and newer schema without exposing secrets.

## Tests and verification

- Use two temporary clones and a local bare test remote to cover equal, local-ahead, remote-ahead,
  diverged, moved-during-push, missing upstream, rejected transport, cancellation, and restart.
- Test credential and SSH failures through classified fake process results; ordinary CI requires no
  credentials or network.
- Prove Sync does not alter HEAD/cache on divergence or failed fetch and never invokes a force push.
- Verify the command is disabled with explanatory text when no upstream exists.
- Update deterministic screenshots and operational documentation.
- Build the complete solution and run all tests.

## Acceptance criteria

- A user explicitly synchronizes non-diverged work through one command.
- Offline commits remain intact after every network/authentication failure.
- Remote-ahead state fast-forwards and reprojects safely; local-ahead state pushes normally.
- Divergence is detected without textual merge, data loss, or misleading success.

## Out of scope

Automatic three-way merge, conflict selection, background Sync, remote provisioning, cloning, and
branch management.

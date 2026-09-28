# GS-05 — Automatic Sync and Hardening

**Status: completed.** GS-01 through GS-04 were completed and verified before implementation.

## Goal and user-facing outcome

Keep linked Projects synchronized in the background while preserving responsive SQLite workflows, clear sync status, manual control, and safe recovery. Automatic sync is application-wide, enabled by default, and runs every five minutes after an initial background pass once the UI is usable.

## Starting point and boundaries

Use the completed manual snapshot, repository, remote, merge, and deletion flows. The [GS shared contract](00_README.md) remains authoritative. Disabling auto-sync must leave manual `Sync now` available. The application still does not clone, initialize, configure, or repair Git repositories or manage credentials.

## Required implementation

- Upgrade settings to version 5 with only global `AutoSyncEnabled=true` and a default five-minute interval. Offer 1, 5, 15, 30, and 60-minute intervals. Keep local repository paths and link state outside roaming/global settings; never store credentials.
- Start one asynchronous sync pass after the UI becomes usable, then run at the selected interval. Process linked Projects sequentially; a paused or failed Project must not block others. Poll configured upstreams even when SQLite has no local changes. Cancel active work cleanly at shutdown without starting a blocking final sync.
- Expose unlinked, idle, syncing, up-to-date, local-pending, deletion-approval, conflict, authentication-required, configuration-invalid, and failed states with an actionable last result. Keep the selected Project's unsaved-edit behavior and manual review rules from GS-04.
- Retain pre-apply backups only when inbound changes will alter SQLite. Document the external clone/init/configuration workflow, relinking, invalid configuration, credentials troubleshooting, and repository/SQLite recovery in Help, README, architecture, collaborative-storage, and recovery guidance. Those documentation updates happen when GS-05 is implemented, not when this roadmap is written.
- Harden path validation against traversal and symlinks, bound repository and Git output sizes and operation time, sanitize logs, and protect against concurrent application instances. Continue to stage only `.entitytracker/**` and never force-push, reset, stash, switch branches, or alter Git configuration.

## Interfaces and compatibility

Application owns scheduler policy and sync-state models; Infrastructure owns settings persistence and operational limits; WPF presents settings, status, and Help. Preserve settings migration from older versions and keep Git away from synchronous SQLite/UI paths.

## Tests and acceptance

- Test initial pass timing, each interval, disablement with manual sync retained, sequential Project processing, failure isolation, upstream polling without local changes, and shutdown cancellation.
- Test stale/invalid repository configuration, authentication-required state, deletion approval and conflicts pausing only the affected Project, multi-instance protection, bounds, symlinks, and sanitized logs.
- Verify backup creation only for inbound SQLite changes and successful recovery guidance. Use a deliberately stalled Git transport to prove ordinary SQLite reads, writes, and UI navigation remain responsive.
- Test settings version 5 migration and UI settings/status accessibility. Build the complete solution and run its regression suite.

## Completion evidence

Settings v5 stores only the global automatic sync switch and interval, preserving v1–v4 reads.
The Application scheduler starts after the WPF shell is usable, checks linked Projects in sequence,
defers unfinished edits, isolates Project failures, and cancels work at shutdown. Manual sync remains
available when automatic sync is off. Automatic conflict and deletion paths require manual review
without showing a background dialog or mutating SQLite. The repository card and action-needed
notifications expose the result; routine automatic checks remain quiet.

System Git runs only against user-managed existing checkouts. The transport rejects traversal and
symlink entries and bounds snapshot file count (20,000), individual files (8 MiB), total snapshot
bytes (128 MiB), Git metadata output (4 MiB), and local/remote operation time (30/120 seconds).
A per-data-root instance guard activates the existing application window. Help, README,
architecture, collaborative-storage, and recovery guidance now describe setup and repair.

Verification: `dotnet build EntityTracker.slnx --no-restore -m:1 --verbosity quiet` succeeded with
zero warnings and errors. `dotnet test EntityTracker.slnx --no-build -m:1 --verbosity quiet` passed
all 646 tests, including scheduler timing and isolation, settings migration, stalled Git with
responsive SQLite reads and writes, conflict/deletion deferral, bounds, symlinks, and instance
protection. README screenshots were regenerated and visually reviewed in Light and Dark mode.

## Agent planning prompt

```text
Plan GS-05 only. Read the GS contract and completed GS-01–GS-04 implementation; inspect settings migration, startup/shutdown composition, sync results, backup/recovery guidance, and WPF settings. Specify scheduling, status, hardening, documentation, and acceptance tests. Do not edit files or broaden the scope beyond GS-05.
```

## Agent implementation prompt

```text
Implement GS-05 according to its approved plan and this milestone. Add background scheduling, settings, status, defensive limits, and current user/recovery documentation while preserving manual sync and SQLite responsiveness. Build and test the complete solution. Do not add clone/init, credential management, or unrelated features.
```

# Local recovery, backups, and logs

## Application data locations

EntityTracker keeps local runtime data under:

```text
%LOCALAPPDATA%\EntityTracker\
```

The relevant paths are:

```text
entity-tracker.db              active SQLite database
settings.json                  optional local appearance and last active Project/Tracker context
repositories.json              local Git-backed Project locations and projected commit IDs
backups\                       automatic SQLite backups
logs\                          daily application logs
```

The settings file is created when appearance or active context is saved. It must
never contain a password, client secret, access token, certificate, or other authentication
material. Appearance and the remembered selection are installation-local; Project and Tracker
records remain in SQLite.

`repositories.json` contains no remote URL or credential. Do not copy it between computers as a
Project backup: repository paths and last successful fetch/push timestamps are installation-local.

## Explicit Project synchronization

A Git-backed Project may remain local-only indefinitely. When its managed branch has a configured
upstream, **Sync** is the only action that contacts it. EntityTracker uses the upstream's configured
remote name and branch; it does not require `origin`. Production remotes must use credential-free
HTTPS or SSH configuration, and authentication remains with installed Git, Git Credential Manager,
the SSH agent, and the user's SSH host-key policy.

EntityTracker repository documents use canonical LF line endings. When cloning a repository with
external Git tooling on Windows, configure that dedicated checkout with `core.autocrlf=false`
before checkout. EntityTracker blocks a checkout whose managed files were transformed or are dirty;
it does not reset or normalize user files automatically.

When an upstream is configured, Sync first fetches and verifies that pending SQLite operations can
be committed without concealing incoming work. It then converts the durable outbox into one ordered
local commit per accepted operation. A repository without an upstream skips that preflight and
creates the local commits directly. Equal tips require no change, local-ahead work is pushed
normally, and a remote-ahead Project is validated before HEAD fast-forwards and SQLite is rebuilt.
Failed fetches, authentication failures, cancellation, and rejected pushes preserve SQLite work and
any local commits already created. If a push may
have raced with another client, run Sync again; never force-push. Diverged histories are reported
as **Merge required** and remain untouched until RS-04 semantic merge is implemented.

## Automatic backups

Before SQLite schema initialization, EntityTracker uses SQLite's online backup operation to make
a consistent copy:

- at most one normal backup per UTC day;
- an additional timestamped backup whenever the stored schema version differs from the supported
  application schema, before migration begins;
- the newest 14 backup files total are retained.

A backup failure is logged and shown as a startup warning, but it does not by itself prevent the
application from opening. A database initialization or migration failure still stops startup.

## Restore a database backup

1. Close every running EntityTracker instance.
2. Open `%LOCALAPPDATA%\EntityTracker` in File Explorer.
3. Make a safety copy of the current `entity-tracker.db` outside that folder.
4. In `backups`, choose the required `.db` file by its UTC date/time.
5. Copy that backup into `%LOCALAPPDATA%\EntityTracker` and name the copy
   `entity-tracker.db`, replacing the active file only after step 3.
6. Start EntityTracker. The normal migration process will upgrade an older backup if needed and
   will first take another pre-migration backup.

Do not edit or restore the database while EntityTracker is running. If the restored database is
newer than the application supports, use a newer compatible application version instead of trying
to downgrade the schema manually.

## Rebuild a Git-backed Project cache

For a Git-backed Project, committed repository HEAD is authoritative through the last successful
Sync, while unsynchronized work is authoritative in SQLite. If EntityTracker reports a stale cache,
choose **Rebuild cache** on the Project dashboard. Rebuild is blocked until all pending SQLite
operations have been synchronized. If the repository folder moved,
choose **Locate repository** and select the same Project repository at the registered branch and
commit. Rebuild replaces only that Project's SQLite projection and does not create a commit or
contact a remote.

Do not edit managed repository JSON or use Git history-rewriting commands as a recovery shortcut.
Resolve a dirty worktree with normal Git tools without discarding work, then retry. A local-only
repository is fully supported; no remote needs to be added for local commits or cache recovery.

## Logs

EntityTracker writes UTC daily logs to `logs\entity-tracker-yyyyMMdd.log` and retains the newest
14 daily files. Logs cover startup/provider selection, backup and migration failures, import/save
failures, and unhandled UI exceptions.

Logs intentionally do not include entity notes, CSV contents, SQL query contents, authentication
material, or the complete settings document. Review a log before sharing it because exception
messages can still contain local file paths.

## Invalid settings recovery

Malformed, unsupported-version, or unknown-field settings never replace the working file.
EntityTracker shows a warning and continues with SQLite. Correct or move the existing
`settings.json`, then save appearance again from **Settings** or choose a Project and
Tracker. Moving the file resets only local appearance and the remembered selection; it does not
change Projects, Trackers, or entity data in SQLite. Legacy non-secret SharePoint fields can remain
in an older settings file for compatibility, but no Connections UI or remote behavior is exposed.

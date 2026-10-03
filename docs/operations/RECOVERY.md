# Local recovery, backups, and logs

## Application data locations

EntityTracker keeps local runtime data under:

```text
%LOCALAPPDATA%\EntityTracker\
```

The relevant paths are:

```text
entity-tracker.db              active SQLite database
settings.json                  local appearance, active context, and automatic sync schedule
git-sync-links.v1.json         local Project-to-checkout associations and sync state
backups\v<schema-version>\    automatic SQLite backups grouped by source schema version
logs\                          daily application logs
```

The settings file is created when appearance, active context, or automatic sync is saved. It must
never contain a password, client secret, access token, certificate, or other authentication
material. Appearance and the remembered selection are installation-local; Project and Tracker
records remain in SQLite.

## Automatic backups

Before SQLite schema initialization, EntityTracker uses SQLite's online backup operation to make
a consistent copy:

- at most one normal backup per UTC day;
- an additional timestamped backup whenever the stored schema version differs from the supported
  application schema, before migration begins;
- pre-sync backups before inbound SQLite changes.

Backups are stored under `backups\v<schema-version>`, using the schema version recorded inside
each database. At startup, older backups in the root `backups` folder are moved into the matching
version folders. Pre-migration backups are kept for rollback; the newest 14 daily and pre-sync
backups are retained across the version folders.

A backup failure is logged and shown as a startup warning, but it does not by itself prevent the
application from opening. A database initialization or migration failure still stops startup.

## Restore a database backup

1. Close every running EntityTracker instance.
2. Open `%LOCALAPPDATA%\EntityTracker` in File Explorer.
3. Make a safety copy of the current `entity-tracker.db` outside that folder.
4. In `backups`, open the folder for the schema version you need (for example, `v18`) and choose
   the required `.db` file by its UTC date/time.
5. Copy that backup into `%LOCALAPPDATA%\EntityTracker` and name the copy
   `entity-tracker.db`, replacing the active file only after step 3.
6. Start EntityTracker. The normal migration process will upgrade an older backup if needed and
   will first take another pre-migration backup.

Do not edit or restore the database while EntityTracker is running. If the restored database is
newer than the application supports, use a newer compatible application version instead of trying
to downgrade the schema manually.

## Recover a Project sync

Automatic sync checks linked Projects after startup and every 1, 5, 15, 30, or 60 minutes as
selected in Settings (enabled and five minutes by default). Manual **Sync now** remains available
when automatic sync is disabled. A Project with an unfinished edit resumes after the edit is saved
or closed; another linked Project can sync meanwhile. Conflicts and outbound deletions require
manual review or approval. Only inbound changes that alter SQLite create a pre-apply backup.

If the Project card says **Authentication required**, use command-line Git in the existing checkout
to repair the credential helper, key, host trust, or remote access; then retry **Sync now**. If it
says **Configuration invalid**, restore the checkout and its expected branch/upstream, or unlink
and relink the clean existing root. EntityTracker does not create, clone, configure, or repair Git
repositories. A missing checkout does not remove the SQLite Project. The notification's action
opens the affected Project for details.

For SQLite recovery, first close EntityTracker, copy both the current database and repository to
a safe location, then restore the chosen `.db` backup by the procedure above. Do not replace
`.entitytracker` files with a database backup: compare the restored Project with the existing
checkout, then use **Sync now** to reconcile it. If the checkout itself is damaged, repair or
reclone it with system Git outside EntityTracker and relink the clean root. Keep
`git-sync-links.v1.json` with the local database when moving a full installation; paths may need
relinking on the new machine.

## Logs

EntityTracker writes UTC daily logs to `logs\entity-tracker-yyyyMMdd.log` and retains the newest
14 daily files. Logs cover startup/provider selection, backup and migration failures, import/save
failures, unhandled UI exceptions, and Project sync stage timings. Sync timing entries include
fetch and Git snapshot read durations and file counts, without repository paths or snapshot content.

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

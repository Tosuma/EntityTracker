# Local recovery, backups, and logs

## Application data locations

EntityTracker keeps local runtime data under:

```text
%LOCALAPPDATA%\EntityTracker\
```

The relevant paths are:

```text
entity-tracker.db              active SQLite database
settings.json                  optional non-secret connection setup
backups\                       automatic SQLite backups
logs\                          daily application logs
```

The settings file is not created until a connection setup is saved. It must never contain a
password, client secret, access token, certificate, or other authentication material.

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
`settings.json`, then save the connection setup again from **Connections**. Removing a valid saved
setup from that page deletes only `settings.json`; it does not change the SQLite database.


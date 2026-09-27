# Collaborative storage status

## Current decision

EntityTracker uses SQLite as its only persistence provider. UX-08 retired the unused SharePoint
configuration model, provider selector, and associated application composition branch. The
application does not authenticate with, read from, write to, or synchronize with any remote
service. GS-02 adds manual local Git repository linking and snapshot commits from the Project
dashboard; ordinary SQLite operations do not invoke Git.

Settings versions 1–3 may contain retired `activeStorage` and `sharePoint` fields. They remain
readable only for a safe migration of supported appearance and active Project/Tracker context.
Loading does not rewrite an existing file. The next legitimate settings save writes version 4
atomically and omits the retired fields.

## Preserved boundaries

- WPF composes the concrete SQLite implementations in `App.xaml.cs`.
- Application services consume focused persistence interfaces shaped around accepted use cases.
- `IPersistenceInitializer` remains the startup seam for initialization and recovery warnings.
- Domain, Application, and Reporting contain no WPF, SQLite, authentication, or remote-provider
  types.
- Stable entity, Project, and Tracker identifiers remain independent of storage implementation.
- A replacement UI can use the existing Application and Reporting services without reimplementing
  ranking, synchronization, readiness, lifecycle, or reporting rules.

Application still contains backend-neutral `CollaborativeConflict`, `CollaborativeConflictField`,
and `CollaborativeConflictSet` value types from the retired provider investigation. No active
backend produces them and no conflict-review UI consumes them. They are inactive historical seams,
not a commitment to a particular provider or synchronization model.

## Git-sync status

The [GS-01–GS-05 roadmap](../milestones/git-sync/00_README.md) records GS-01 portable snapshots
and GS-02 local repository linking as completed. Users prepare and configure repositories outside
EntityTracker, then select an existing clean working tree. The local association is stored outside
SQLite so a later deletion tombstone can survive Project purge. Remote fetch and push, import,
merge, recovery, and conflict review remain planned. SQLite stays the runtime store and the inward
dependency direction remains intact.

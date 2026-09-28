# Collaborative storage status

## Current decision

EntityTracker uses SQLite as its only persistence provider. UX-08 retired the unused SharePoint
configuration model, provider selector, and associated application composition branch. The
application does not authenticate with a remote service or manage Git credentials. GS-01–GS-05
add portable Project snapshots, existing-checkout linking, manual fetch and push, collaborative
merge review, and terminal Project tombstones. Ordinary SQLite operations do not invoke Git.

Settings versions 1–3 may contain retired `activeStorage` and `sharePoint` fields. They remain
readable only for a safe migration of supported appearance and active Project/Tracker context.
Loading does not rewrite an existing file. The next legitimate settings save writes version 5
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
and `CollaborativeConflictSet` value types from the retired provider investigation. Git-sync uses
its own typed Project merge conflicts; the older value types remain inactive historical seams.

## Git-sync status

The [GS-01–GS-05 roadmap](../milestones/git-sync/00_README.md) records GS-01–GS-05 as completed.
Users prepare and configure repositories outside EntityTracker, then select an existing clean
working tree. The local association is stored outside SQLite so a deletion tombstone survives
Project purge until publication. Manual sync can import, fetch, merge, review conflicts, and push.
It waits for unfinished presentation edits before applying inbound changes. The Application
scheduler runs linked Projects sequentially after startup and at the configured global interval.
Automatic sync defers an unfinished edit, while manual sync waits for it. WPF presents status and
review; Infrastructure owns Git process limits and the settings v5 file. A named per-data-root
mutex prevents a second application instance from opening the same SQLite store. SQLite stays
the runtime store and the inward dependency direction remains intact.

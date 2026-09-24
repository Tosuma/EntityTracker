# Collaborative storage status

## Current decision

EntityTracker uses SQLite as its only persistence provider. UX-08 retired the unused SharePoint
configuration model, provider selector, and associated application composition branch. The
application does not authenticate with, read from, write to, or synchronize with any remote
service, and the shell exposes no remote connection or Sync control.

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

## Future work

Any collaborative provider or Git synchronization requires a separately approved product
milestone covering authority, data format, authentication, concurrency, merge behavior, offline
behavior, recovery, migration, and user-visible conflict handling. It must preserve the inward
dependency direction and must not be inferred from the inactive conflict value types.

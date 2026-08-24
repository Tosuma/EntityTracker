# Milestone 13 — Live SharePoint Integration

## Goal

Implement the organization-approved SharePoint integration so SharePoint becomes the authoritative
collaborative store while SQLite remains the local cache and standalone development provider.

Milestone 12 defined the provider boundary, recovery behavior, and collaborative semantics. This
milestone implements those contracts against a real approved SharePoint environment.

## Prerequisites

- The organization provides an approved Microsoft Entra application registration, tenant/client
  identifiers, delegated permissions, and a SharePoint test site.
- Required SharePoint lists are provisioned and permissioned by the organization. EntityTracker
  must not create lists or request broader provisioning permissions.
- Authentication uses the organization-approved Microsoft API and identity library with
  interactive delegated browser sign-in and PKCE.
- The remote schema, list identifiers, required columns, indexes, and version marker are documented
  and approved before adapter implementation begins.
- No password, client secret, access token, refresh token, certificate, or other authentication
  material may be stored in `settings.json`.

## Tasks

### Configuration and connection

- Migrate the optional local settings schema without breaking existing version-1 settings.
- Store only non-secret connection metadata needed to locate the tenant, site, and approved lists.
- Add connection, sign-in, sign-out, test, and provider-activation behavior to the Connections page.
- Clearly show whether the application is using standalone SQLite, an online SharePoint connection,
  or a read-only cached SharePoint view.
- Keep standalone SQLite available for local development, tests, and explicit user selection.

### SharePoint adapter and schema validation

- Implement the SharePoint adapter in Infrastructure using the organization-approved API and
  authentication approach.
- Validate the provisioned remote schema and version before reading or writing; never guess or
  silently create missing lists, columns, indexes, or permissions.
- Map stable entity GUIDs, normalized source keys, status, notes, lifecycle, provenance,
  dependencies, unresolved dependencies, manual overrides, history, and revision metadata without
  leaking SharePoint types outside Infrastructure.
- Implement the existing focused persistence responsibilities, reshaping an Application contract
  only when a concrete SharePoint mismatch requires it. Do not introduce a generic repository or
  one provider god class.
- Account for SharePoint's lack of cross-list transactions with a recoverable, idempotent operation
  protocol. Interrupted writes must be detectable and safe to complete or reconcile without
  duplicating history or replaying a committed change.

### Authoritative storage and local cache

- Treat SharePoint as authoritative and SQLite as a cache, not as equal multi-master stores.
- Refresh the cache after connection and whenever remote revisions show that cached state is stale.
- Commit writes remotely first and update SQLite only after remote success.
- If the remote write succeeds but the cache update fails, invalidate the affected cache state and
  refetch it without repeating the remote operation.
- Make offline SharePoint-backed use visibly read-only. Do not queue hidden writes or report an
  offline change as saved.
- Permit initial population only when the approved remote data set is empty. Preserve all stable
  GUIDs, history, dependencies, overrides, lifecycle, provenance, notes, and progress. Refuse to
  seed or overwrite a non-empty remote data set without a separately reviewed reconciliation plan.

### Concurrency and conflict review

- Use ETags or equivalent approved revision tokens for optimistic concurrency.
- Apply the documented three-way merge independently to scalar fields and normalized dependency
  keys.
- Automatically merge non-overlapping edits.
- Return backend-neutral conflict payloads for incompatible same-field/key edits,
  archive-versus-edit, identity reassignment, and duplicate normalized names with different GUIDs.
- Add a conflict-review modal that groups conflicts by entity, explains base/local/remote values,
  requires an explicit resolution for every conflict, rereads the latest revision before applying,
  and changes nothing when canceled.

### Reliability, privacy, and operations

- Handle throttling, transient failures, cancellation, expired sessions, permission loss, and remote
  schema changes with bounded retry and clear diagnostics.
- Use idempotent operation IDs for history and multi-list writes so retries cannot duplicate data.
- Log connection state and failure categories without logging tokens, credentials, entity notes,
  imported CSV contents, SQL query contents, or complete remote payloads.
- Document required delegated permissions, provisioned-list schema, connection setup, initial
  population, cache recovery, conflict recovery, and administrator troubleshooting.
- Keep provider-specific SDK, authentication, ETag, request, retry, and list-mapping types inside
  Infrastructure.

## Tests

- Adapter mapping tests cover every persisted entity, dependency, override, lifecycle, and history
  field, including unresolved dependencies and archived entities.
- Configuration migration tests preserve existing version-1 settings and never persist secrets.
- Remote schema validation tests reject missing, renamed, incompatible, or newer schema versions.
- Cache tests cover initial load, incremental refresh, stale revisions, remote success followed by
  cache failure, cache invalidation, and refetch.
- Offline tests prove all writes are disabled and no write queue is created.
- Concurrency tests use two clients to cover automatic non-overlapping merge, every true conflict
  category, canceled review, stale conflict resolution, and successful retry.
- Idempotency and failure-injection tests cover interruption between remote list writes, duplicate
  operation delivery, throttling, timeouts, expired authentication, and permission loss.
- Initial-population tests preserve IDs and history for an empty remote data set and reject a
  non-empty target.
- Existing Domain, Application, Infrastructure, Reporting, WPF, migration, and standalone SQLite
  tests remain green.
- A separately gated smoke suite runs against the organization-approved SharePoint test site. Unit
  and ordinary CI runs must not require credentials or network access.

## Acceptance criteria

- A user can interactively sign in to the approved SharePoint test environment and the application
  validates its provisioned schema before activation.
- SharePoint-backed mode supports the existing reads, CSV synchronization, manual entity creation,
  entity edits, archive/restore, status and notes, dependencies and overrides, progress history,
  ranking, readiness, reporting, and export behavior.
- SharePoint is authoritative, SQLite is updated only after remote commit, and offline cached use is
  clearly read-only.
- Stable GUIDs, normalized identity, unresolved dependencies, manual overrides, notes, lifecycle,
  provenance, and append-only history survive synchronization and cache refresh.
- Non-overlapping concurrent edits merge automatically; true conflicts require explicit review and
  cannot be silently overwritten.
- Interrupted or retried multi-list operations do not produce partial invisible state, duplicate
  history, or repeated committed writes.
- Initial population works only against an empty approved remote data set and preserves existing
  identities and history.
- No secrets or tokens are written to local settings, logs, SQLite, or source control.
- Standalone SQLite mode remains functional and all existing tests continue to pass.
- SharePoint, Microsoft API, authentication, ETag, and throttling concerns remain isolated to
  Infrastructure and WPF composition/presentation where appropriate.

## Out of scope

- In-app SharePoint list or Entra application provisioning.
- App-only client-secret or certificate authentication.
- Offline write queues or delayed background upload.
- Symmetric multi-master synchronization between SharePoint and SQLite.
- Automatic overwrite or merge into a non-empty remote data set during initial population.

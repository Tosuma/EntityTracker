# Collaborative storage contract

## Status and scope

Milestone 12 defines the boundary and merge semantics needed by a future approved SharePoint
adapter. It does **not** connect to SharePoint, authenticate a user, create lists, or synchronize
remote data. SQLite is the only active provider in this build.

The future target is not symmetric multi-master storage:

```text
SharePoint (authoritative)
          ↓
SQLite (local cache)
```

The SQLite cache remains useful for local reads, ranking, reporting, and development. It must not
be treated as an equally authoritative peer.

## Provider and UI seams

- WPF selects concrete persistence implementations only in `App.xaml.cs`.
- Application services consume focused persistence interfaces shaped around current use cases.
- `IPersistenceInitializer` is the provider-neutral startup seam for provider preparation and
  recovery warnings.
- Domain, Application, and Reporting contain no WPF, SQLite, SharePoint, authentication, or
  Microsoft Graph types.
- A replacement UI can compose and call the existing Application and Reporting services without
  reimplementing ranking, synchronization, readiness, lifecycle, or reporting rules.
- A future provider should implement the existing focused read/store contracts. Do not replace
  them with a generic repository or a single provider god object.

Some operations currently use interfaces with SQLite-oriented implementations because those are
the operations the application actually performs. Add or reshape a contract only when the remote
implementation exposes a concrete mismatch; do not speculate ahead of approved access.

## Identity and uniqueness

- `EntityId` GUIDs are immutable across providers and caches.
- Normalized `EntitySourceKey` values are unique for active and archived tracked identities.
- A matching imported or remote normalized name reconciles with the existing stable identity; it
  does not create a new GUID.
- Different GUIDs claiming the same normalized name are a conflict. They are never silently
  coalesced.
- Dependencies are keyed by the owner identity plus normalized dependency source key. Resolution
  to an entity GUID is derived from the candidate state and may change when an entity appears,
  archives, or is restored.

## Connectivity and write sequence

The future SharePoint-backed mode has these rules:

1. Offline use is read-only. The application may show a previously verified SQLite cache, but it
   must not queue hidden edits or claim an offline write was saved.
2. Writes require an online read of the relevant remote revisions.
3. The remote transaction/operation is committed first using concurrency tokens.
4. The SQLite cache is updated only after remote success.
5. If remote commit succeeds but cache update fails, invalidate the affected cache state and
   refetch it. Never retry the remote write blindly.
6. A failed or conflicted remote write leaves the cache's authoritative view unchanged.

Remote calls, list identifiers, ETags, retries, throttling, authentication, and token handling all
belong in Infrastructure. None may appear in Domain, Application, Reporting, or WPF view models.

## Optimistic concurrency and merge

Each editable remote aggregate needs a revision token (for SharePoint, normally an ETag or an
equivalent approved revision). A write compares three states:

- **base** — the revision from which the local edit started;
- **local** — the user's intended value;
- **remote** — the current authoritative value.

Non-overlapping edits merge automatically. Merge comparison is field-specific:

- source/display name;
- development status;
- notes;
- lifecycle state;
- provenance;
- each imported dependency key;
- each manual dependency override key.

Dependencies are not compared as one serialized collection. Add/remove/action changes are merged
per normalized dependency key. If only one side changed a scalar field or dependency key from the
base, that change wins. If both sides changed the same field or key to different values, the write
produces a true conflict.

The following are always explicit conflicts:

- archive versus edit of the same entity;
- different IDs claiming the same normalized name;
- incompatible edits to the same scalar field;
- incompatible add/remove/action changes to the same dependency key;
- identity reassignment.

No last-writer-wins overwrite is allowed for these cases.

## History and derived data

- Status history is append-only.
- Each future remote history append carries a globally unique operation ID. Replaying the same
  operation is idempotent and must not create a duplicate history entry.
- Progress snapshots and rankings are derived projections. They are recomputed after a successful
  authoritative update rather than field-merged as user edits.
- Status timestamps are preserved with their operation; clock order alone must not bypass revision
  checks.

## Conflict review payload

Application defines backend-neutral `CollaborativeConflict`, `CollaborativeConflictField`, and
`CollaborativeConflictSet` value types. They describe the entity, optional dependency key, and
base/local/remote display values. They deliberately contain no SharePoint or WPF types.

A future conflict-review modal should:

1. group conflicts by entity and field/dependency key;
2. explain the base, local, and current remote values;
3. require an explicit choice for each true conflict;
4. reread the latest revision before applying the resolution;
5. cancel without changing remote or cached state.

This milestone does not add the modal because no active backend can currently produce these
conflicts.

## Initial SharePoint population

A future migration tool may seed SharePoint only when the approved remote data set is empty. It
must preserve stable entity GUIDs, dependency keys, lifecycle/provenance, notes, progress, and
history operation identity. It must refuse to overwrite or automatically merge into a non-empty
remote data set; that requires a separately reviewed migration/reconciliation plan.


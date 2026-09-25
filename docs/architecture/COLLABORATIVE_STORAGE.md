# Collaborative storage direction

## Current implementation

EntityTracker supports SQLite-only Projects and optional Git-backed Projects. UX-08 retired the
unused SharePoint configuration model and provider selector. A user-triggered Project Sync can now
fetch, fast-forward, or normally push a non-diverged managed branch through a configured HTTPS or
SSH upstream. There is no startup, timer, navigation, or save-triggered network access.

RS-01 defines [Project repository format v1](PROJECT_REPOSITORY_FORMAT.md), adds backend-neutral
operation IDs to retained history, and provides a constrained installed-Git command boundary in
Infrastructure. RS-02 lets a Project link an existing empty Git repository or open an existing
EntityTracker repository. For that Project, committed HEAD is authoritative and SQLite is rebuilt
as a local projection. RS-03 adds safe upstream classification and blocks diverged histories for
RS-04. A repository may have no remote; accepted operations still create local commits and work
offline.

Settings versions 1–3 may contain retired `activeStorage` and `sharePoint` fields. They remain
readable only so supported appearance and active Project/Tracker context migrate safely. The next
legitimate settings save writes version 4 without those retired fields.

## Approved Git direction

RS-01 through RS-05 govern optional Git backing at Project scope:

```text
Dedicated Project Git repository (authoritative)
                         ↓
            SQLite projection/cache
```

- SQLite-only Projects remain supported.
- Every linked Project uses one dedicated repository at a user-selected path and one managed
  branch. Repository documents are deterministic and reviewable but app-owned.
- EntityTracker opens or links repositories already initialized by external Git tooling. It does
  not initialize or clone them.
- Each accepted operation becomes one local commit and remains available offline.
- A user-triggered Sync uses installed Git to fetch, semantically merge, and push through HTTPS or
  SSH. There is no background network access.
- Git credential helpers or SSH tooling own authentication. EntityTracker stores no credentials.
- Permanent deletion removes current state and records a tombstone; existing Git history is not
  rewritten.

RS-03 implements the non-diverged subset of this direction. Semantic merge remains unimplemented.
The complete sequencing and acceptance criteria are defined in the
[remote synchronization roadmap](../milestones/remote-sync/README.md). These decisions are approved
architecture direction, not implemented behavior.

## Preserved boundaries

- WPF composes concrete Infrastructure implementations and presents Application state; it does not
  own serialization, Git commands, cache projection, or merge rules.
- Application services consume focused contracts shaped around accepted use cases. Do not replace
  them with a generic repository or provider god object.
- Git invocation, filesystem validation, canonical JSON, remote transport checks, and SQLite
  projection belong in Infrastructure.
- Domain, Application, and Reporting contain no WPF, SQLite, process, credential, or Git SDK types.
- Stable Project, Tracker, Entity, dependency, and operation identities cross storage boundaries.
- Ranking, readiness, blockers, dependency resolution, progress snapshots, dashboards, and
  comparisons remain derived projections.

The existing `CollaborativeConflict`, `CollaborativeConflictField`, and
`CollaborativeConflictSet` types are inactive historical seams. RS-04 may reshape or replace them
only with backend-neutral conflict payloads required by the approved three-way merge.

## Safety rules

- Never silently use last-writer-wins for incompatible concurrent changes.
- Never force-push, rebase published history, execute repository hooks, or clean/reset a user's
  repository.
- Reject unsupported schemas and external modifications before changing authoritative state.
- Commit authoritative state before updating its SQLite projection. A projection failure triggers
  a rebuild and must not replay the authoritative operation.
- Validate a fetched remote tree before fast-forwarding. Divergence, failed fetch, and rejected
  pushes leave local commits and SQLite intact.

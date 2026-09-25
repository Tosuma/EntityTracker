# Project repository format v1

EntityTracker repository format 1 is the durable, app-owned representation used by the remote-sync
roadmap. The format is defined in RS-01 but is not user-visible until RS-02. SQLite remains the
only authority until a Project is explicitly linked in that later milestone.

## Managed tree

```text
entitytracker-project.json
trackers/<tracker-id>/tracker.json
trackers/<tracker-id>/entities/<entity-id>.json
operations/<operation-id>.json
tombstones/projects/<project-id>.json
tombstones/trackers/<tracker-id>.json
tombstones/entities/<entity-id>.json
```

All IDs in paths and documents are lowercase `D`-format GUIDs. Paths and JSON properties are
case-sensitive even on Windows. Managed paths cannot traverse outside the repository or contain a
reparse point.

## Canonical JSON

- UTF-8 without a BOM, two-space indentation, LF line endings, and one final LF.
- Properties are emitted in the order defined by the v1 document contracts.
- Enums use their invariant, case-sensitive names.
- UTC timestamps use `yyyy-MM-ddTHH:mm:ss.fffffffZ`.
- Nullable properties and empty collections are emitted explicitly.
- Identity collections sort by GUID. Dependency declarations and overrides sort by normalized
  source name and then kind/action. Status transitions sort by occurrence, entity ID, and kind.
- Unknown properties, malformed values, and missing or unsupported versions are rejected rather
  than ignored.

The root document has document type `entitytracker-project`, format version `1`, and complete
Project metadata. Tracker and entity documents preserve catalog metadata, lifecycle, provenance,
requested priority, responsible developer, group, notes, audit timestamps, imported dependency
declarations, and manual dependency overrides.

Imported dependencies are stored by source name and kind. Resolved and unresolved edges are cache
projections and are never authoritative repository data. Ranks, readiness, blockers, progress
snapshots, dashboard totals, and comparison results are likewise excluded.

## Operations and deletion

Operation documents are immutable and append-only. They preserve the operation ID and kind,
occurrence time, affected identities, status transitions, and optional import summary. Tombstones
are also immutable and append-only and must reference the matching deletion operation.

A permanently deleted Project keeps `entitytracker-project.json` as its versioned repository
identity and last complete metadata. Its Project tombstone makes the repository terminal; no live
Tracker or entity document may remain. This allows a later Open operation to recognize the deleted
repository without treating historical metadata as live state.

## Git safety boundary

App-issued Git commands use the installed Git CLI through argument lists without a command shell.
Git 2.40 or newer is required. Commands have bounded redirected output, cancellation and timeouts,
and disable hooks, pagers, signing, automatic line-ending conversion, and global attributes.

Repositories must be non-bare, clean, owned by the current Windows user, on one symbolic checked-
out branch, and have author name and email configured. An unborn symbolic branch is valid; detached
HEAD is not. Nested repositories, submodules/gitlinks, repository attributes, custom remote
helpers, and reparse points in managed paths are rejected. Production remotes are credential-free
HTTPS or SSH configuration only. Authentication remains owned by Git Credential Manager or SSH
tooling; EntityTracker neither receives nor stores secrets.

The supported API cannot force push, rewrite history, reset, clean, rebase, switch branches, or
accept arbitrary Git switches/refspecs. RS-01 does not call fetch or push from the application and
does not register repositories.

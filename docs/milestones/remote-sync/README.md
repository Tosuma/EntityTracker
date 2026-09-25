# EntityTracker remote synchronization roadmap

The `RS-` milestones add optional Project-level Git versioning and remote collaboration after the
completed UX overhaul. They replace the retired SharePoint direction; they do not revive a generic
provider selector.

## Fixed product decisions

- Git backing is optional per Project. SQLite-only Projects remain supported.
- One dedicated, user-selected Git repository represents exactly one Project.
- Git is authoritative for a linked Project; SQLite is its local query projection/cache.
- Repository documents are deterministic and reviewable but are edited only through EntityTracker.
- Every accepted operation creates one local commit. Offline commits are supported.
- Network access occurs only when the user invokes Sync. Sync fetches, semantically merges, and
  pushes the one managed branch.
- EntityTracker uses an installed Git CLI and the user's Git credential manager or SSH tooling.
- EntityTracker opens or links existing repositories; it does not initialize or clone them.
- Production remotes are limited to HTTPS and SSH. Force pushes and history rewrites are forbidden.
- Permanently deleted data disappears from current state and gains a tombstone, while Git history
  remains available to repository administrators.

## Execution order

1. [RS-01 — Repository Contract and Git Tooling](rs_01_repository_contract_and_git_tooling.md)
2. [RS-02 — Local Git-Backed Projects](rs_02_local_git_backed_projects.md)
3. [RS-03 — Remote Synchronization](rs_03_remote_synchronization.md)
4. [RS-04 — Semantic Merge and Conflict Review](rs_04_semantic_merge_and_conflict_review.md)
5. [RS-05 — Recovery, Security, and Consistency](rs_05_recovery_security_and_consistency.md)

RS-01 through RS-03 are complete. RS-03 synchronizes equal, local-ahead, and remote-ahead histories
and reports divergence without integrating it. RS-04 and RS-05 remain planned.

Implement the milestones in order. An earlier milestone may safely detect and block a state owned
by a later milestone, but it must not expose a misleading partial success.

## Shared architecture rules

- Domain, Application, and Reporting remain independent of Git processes, filesystem details,
  SQLite, and WPF.
- Git command execution, canonical JSON, repository inspection, and cache projection belong in
  Infrastructure. WPF presents state and invokes Application use cases only.
- Stable Project, Tracker, Entity, dependency, and operation identities cross the repository/cache
  boundary unchanged.
- Business validation occurs before authoritative commits. Derived ranks, readiness, blockers,
  dependency resolution, progress snapshots, and comparison projections are recomputed.
- App-issued Git commands use argument lists without a command shell, disable repository hooks,
  never log secrets, and never accept credentials embedded in a URL.
- Every milestone keeps the complete solution buildable and all existing SQLite behavior green.

## Deliberate exclusions

The roadmap does not include background synchronization, in-app repository creation or cloning,
branch switching, pull requests, hosting-provider APIs, an embedded Git library, custom remote
helpers, submodules, Git LFS, manual editing support, or destructive history rewriting.

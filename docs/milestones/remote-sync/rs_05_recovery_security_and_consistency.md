# RS-05 — Recovery, Security, and Consistency

## Goal

Audit and harden the complete Git-backed workflow so repository, cache, network, authentication,
security, and presentation failures remain recoverable and understandable.

## Required implementation

### Recovery and reliability

- Detect interrupted managed-file writes, stale/incomplete cache projection, unavailable or moved
  repository folders, corrupt objects, invalid HEAD, detached HEAD, branch/upstream changes,
  unfinished external Git operations, rejected pushes, and remote schema/version changes.
- Provide safe actions to locate a moved repository, revalidate, rebuild SQLite from HEAD, retry a
  canceled/failed Sync, and open diagnostics. Never reset, clean, discard, or rewrite user commits.
- Bound and cancel Git processes and network retries. Do not blindly retry authentication failures,
  non-fast-forward pushes, validation errors, or authoritative commits.
- Preserve current SQLite backup/recovery for SQLite-only Projects. Document that a linked Project's
  repository is authoritative and the SQLite database is disposable cache for that Project.

### Security and privacy

- Re-audit all commands for shell avoidance, hook suppression, pager/editor suppression, transport
  allow-listing, path traversal, symlink/reparse-point escape, submodules, nested repositories,
  unsafe ownership, custom helpers/filters, and unbounded output.
- Redact credentials and user information from command diagnostics. Do not log tokens, keys,
  credential-bearing URLs, entity notes, imported CSV contents, SQL query contents, or full
  repository documents.
- Refuse unsupported external modifications rather than attempting an automatic reset or cleanup.

### UX, documentation, and release

- Standardize Project sync states, long errors, progress/cancellation, unavailable repositories,
  cache rebuild, dirty worktree, missing Git/identity, authentication, divergence, conflicts, and
  successful Sync across Portfolio and Project dashboard.
- Verify Light/Dark/System, keyboard order, focus restoration, screen-reader names/live regions,
  minimum/wide windows, DPI scaling, and long repository paths/messages.
- Document Git 2.40+, author identity, HTTPS credential-manager and SSH prerequisites, external
  repository initialization/cloning, linking/opening, offline commits, Sync, conflicts, relocation,
  cache rebuild, permanent-delete history, and administrator recovery.
- Update deterministic screenshot generation and README images without touching user data.

## Tests and verification

- Run failure injection at every authoritative-write/cache boundary and every fetch/merge/push
  boundary, including cancellation and restart.
- Run a two-client matrix against temporary bare remotes with 125+ entities and multiple Trackers,
  repeated operations, large histories, moved remotes, auth simulations, corrupt files, and stale
  caches.
- Verify hooks and unsafe repository features cannot execute through app-issued commands.
- Run the complete screenshot suite, publish pipeline, solution build, and all tests.
- Perform a manual Windows pass with installed Git/Git Credential Manager and an SSH remote in a
  dedicated test environment; keep this separately gated from ordinary CI.

## Acceptance criteria

- Every supported failure preserves authoritative commits and provides a safe recovery path.
- Cache deletion/rebuild cannot lose Project data or create remote writes.
- Security controls prevent supported operations from executing repository hooks or unsupported
  transports/features.
- Git-backed and SQLite-only Projects remain accessible, consistent, and clearly distinguished.
- Documentation and screenshots match the shipped behavior, and the complete suite is green.

## Out of scope

Background synchronization, in-app clone/init, branch and pull-request workflows, hosted-provider
APIs, embedded credentials, force push, history erasure, and manual JSON editing support.

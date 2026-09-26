# RS-02 — Local Git-Backed Projects

## Goal

Allow a Project to opt into an existing local Git repository, make its committed repository state
authoritative, and keep SQLite as a recoverable projection without requiring a remote.

## Required implementation

### Local registry and onboarding

- Add an atomically written, versioned repository registry under `%LocalAppData%\EntityTracker`.
  Store Project ID, canonical repository path, managed branch, and last projected commit only.
  Do not duplicate the remote URL or credentials from Git configuration.
- Add **Link repository** for an existing SQLite Project. The user selects an already initialized,
  non-bare, clean repository with no existing EntityTracker manifest or unrelated tracked content;
  EntityTracker writes repository v1 and creates the initial operation commit.
- Add **Open repository** for registering an existing valid EntityTracker repository. Validate its
  current branch and complete state before adding it to Portfolio and rebuilding its cache.
- Add **Locate repository** for a registered Project whose folder moved or is unavailable. Require
  the located manifest to carry the same Project ID.
- Reject duplicate repository paths, duplicate registered Project IDs, Project-name collisions
  under existing catalog rules, wrong branches, dirty worktrees, and invalid/newer schemas.
- Do not initialize or clone repositories.

### Authoritative mutations

- Introduce one Application-level Project authority/mutation coordinator used by every mutating
  workflow. WPF and individual use cases must not branch directly on Git versus SQLite.
- Preserve current SQLite behavior for an unlinked Project.
- For a linked Project, load/verify the state represented by HEAD, prepare and validate the complete
  business operation, write canonical managed files, stage only managed paths, and create exactly
  one local commit before projecting the new HEAD into SQLite.
- Generate one operation GUID per accepted save, import, bulk update, lifecycle action, Project or
  Tracker management action. Include `EntityTracker-Operation-Id: <guid>` in the generated commit
  message trailer.
- Derive concise commit subjects from the operation kind; do not ask users for commit messages and
  do not include notes or CSV contents in them.
- On pre-commit failure, restore managed files to HEAD and leave SQLite unchanged. If commit succeeds
  but projection fails, report that the authoritative change succeeded, mark the cache stale, and
  rebuild it from HEAD before further reads/writes.
- Permit offline commits. A dirty working tree or unsupported external edit blocks EntityTracker
  writes without resetting or overwriting user files.

### UX and lifecycle

- Show whether the active Project is SQLite-only, Git-backed and clean, unavailable, stale-cache,
  or blocked by repository validation.
- Keep repository actions Project-scoped and keyboard accessible. Preserve active Project/Tracker
  context after linking, opening, locating, or rebuilding.
- For Git-backed permanent deletion, write a tombstone and remove current managed state. Explain
  that Git administrators can still recover historical content; never claim physical erasure.

## Tests and verification

- Exercise every accepted mutation workflow and prove one successful operation produces one commit
  with one operation ID and an equivalent SQLite projection.
- Cover link/open/locate, moved folders, missing Git identity, clean/dirty repositories, initial
  export failure, commit failure, projection failure/rebuild, restart, recycled and permanently
  deleted state, and SQLite-only regressions.
- Verify no remote access occurs and offline operations succeed.
- Update deterministic Light/Dark screenshots for onboarding and repository states.
- Build the complete solution and run all tests.

## Acceptance criteria

- Users can retain SQLite-only Projects or link/open a Git-backed Project at any accessible path.
- HEAD is authoritative for a linked Project and SQLite can be rebuilt without information loss.
- Every accepted local operation is an atomic Git commit; failures never create a false success.
- No remote operation, cloning, initialization, or merge behavior is exposed.

## Out of scope

Fetch, push, remote authentication UI, divergence integration, and conflict review.

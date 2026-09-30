# RESP-04 — Local identity and Assign me

**Status: planned.** Depends on RESP-01 through RESP-03.

## Goal

Let each app installation know which Project developer to use for one-click self-assignment,
without assuming a login or sharing that choice with collaborators.

## Required behavior

- Add a `You in this Project` selector to Settings. For each Project, the installation can select,
  change, or clear one available developer. The default is unset. Show the selected Project's
  initials and optional display name so the choice is clear.
- Store the choice locally as Project ID to developer ID. It must not enter Project snapshots,
  Git sync, Tracker copies, or exported shared data. A Project switch changes the effective
  selection. A retired, deleted, or otherwise unavailable choice is treated as unset until the
  user selects an available developer; never silently match a replacement by initials.
- Add `Assign me` to active entity details and the entity editor. It adds the selected developer
  alongside other current developers and leaves them unchanged. If already assigned, it makes no
  duplicate period. In the editor it stages the addition and dates it on save; from details it
  persists immediately. With no valid selection, guide the user to Settings instead of assigning.
- Keep the selector freely changeable. Changing `you` does not rewrite prior assignments or
  attribute earlier work to the newly selected developer.

## Tests and acceptance

- Verify local settings migration and round trip, Project switching, two installations choosing
  different developers for the same Project, and exclusion from snapshots and sync.
- Verify both action locations, adding alongside others, repeated clicks, editor cancellation,
  stale/retired selections, and assignment timestamps. Check keyboard and accessible labels.
- A user can change their local choice and assign themselves in one action without affecting
  another collaborator's local choice.

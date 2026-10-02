# RESP-04 — Local identity and Assign me

**Status: completed.** RESP-01 through RESP-03 were complete before implementation.

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

## Completion evidence

- Settings version 7 stores one optional Developer ID per Project in the local settings file.
  The Settings selector shows available Developers and clears retired or missing selections
  without matching initials. The selected ID is absent from Project records, SQLite backups,
  portable snapshots, and Git sync; a snapshot hash remains unchanged when two installations
  choose different Developers for the same Project.
- **Assign me** in active details adds a period atomically through the tracked-state transaction,
  leaves other assignments open, and is idempotent. The editor stages the same choice in its
  Developer picker; saving starts the period at the database UTC write time, and cancellation
  discards it. Missing choices show inline Settings guidance, and editor navigation uses the
  existing unsaved-work confirmation.
- Focused settings, SQLite, snapshot, and ViewModel tests cover migration, switching Projects,
  separate installations, retirement, repeated actions, archive protection, editor save and
  cancellation, and timestamps. `dotnet build EntityTracker.slnx --no-restore -m:1 --nologo -v:q`
  passed on 2026-10-02 with 0 errors; seven `NU1900` warnings reported that NuGet vulnerability
  data was unavailable. `dotnet test EntityTracker.slnx --no-build --no-restore -m:1 --verbosity quiet`
  passed all 726 tests across seven projects (191 Application, 8 DemoData, 60 Domain,
  230 Infrastructure, 23 Reporting, 12 Screenshots, 202 WPF).
- The new selector and action buttons have accessible names and use native keyboard controls;
  guidance uses a live announcement. Deterministic Light and Dark screenshots were regenerated;
  Settings, entity details, and editor captures were inspected in both themes, and affected
  README images and user guidance were updated.

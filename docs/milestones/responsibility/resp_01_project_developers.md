# RESP-01 — Project developers

**Status: completed — 2026-09-30.** Built on the existing Project/Tracker model and portable Project snapshots.

## Goal

Create a reusable developer directory within each Project. These records identify people who can
later be assigned to entities; they do not create logins, permissions, or authentication.

## Required behavior

- A developer has an immutable ID, owning Project ID, mandatory initials, one optional display
  name, and an available or retired state. Initials and display name can be edited without changing
  identity. Trim both fields; match initials case-insensitively while retaining display casing.
- Available developers must have unique initials within their Project. A retired developer keeps
  their ID and details; a new developer may reuse those initials. Restoring a retired developer
  must fail clearly if an available developer already uses them.
- Add a Developers destination or section in the selected Project's workspace for listing,
  searching, creating, editing, retiring, and restoring developers. Show retired records separately
  and make their state explicit. No Project selection means no developer directory.
- Keep the existing `ResponsibleDeveloper` text field working until RESP-02 replaces it. This
  milestone does not infer developer records from that text or change entity assignments.

## Data and collaboration

- Persist developer records under Project ownership in SQLite, with a uniqueness rule for
  available initials. Use stable IDs as relationship keys; initials are never a primary key.
- Include records in versioned Project snapshots, copies/imports, validation, and Git merge.
  Upgrade older snapshots as an empty developer directory. Merge records by ID and review
  incompatible edits, including concurrent creations that would duplicate available initials.
- Project deletion removes its directory. Tracker deletion does not. Maintain the established
  optimistic revision and backup behavior for incoming Project changes.

## Tests and acceptance

- Verify Project isolation, blank-initial rejection, case-insensitive available-name uniqueness,
  edits preserving ID, retirement, initials reuse, and conflicting restoration.
- Verify SQLite and snapshot round trips, old-snapshot upgrade, and independent/conflicting Git
  merges. Verify the directory can be used with keyboard and screen reader labels.
- A user can maintain developers in one Project without changing responsibility data in another.

## Completion evidence

- Project-scoped Developer IDs, mandatory initials, optional display names, and available/retired
  state are implemented. SQLite schema version 17 enforces available-initials uniqueness per
  Project. Retirement asks for the selected developer's exact initials before saving.
- The Developers directory opens from the Project dashboard, has a return action, search, an
  available list and a retired-developers dialog, and keyboard-accessible controls with screen reader names. Light and Dark
  screenshots were generated and inspected; the affected README captures were updated.
- Project snapshot format 2 includes developers. Format 1 snapshots and tombstones remain readable.
  SQLite snapshot import/export and Git merge tests cover independent edits, duplicate-initials
  review, and preservation of retired records. The existing entity responsible text is unchanged.
- `dotnet build EntityTracker.slnx --no-restore -m:1 --verbosity quiet` succeeded with 0 errors.
  `dotnet test EntityTracker.slnx --no-build --no-restore -m:1 --verbosity quiet` passed all 688
  tests across seven projects. A final focused run after the last navigation and persistence edits
  passed 16 tests. Screenshot generation produced 35 images per theme. The build reported only
  NU1900 warnings because the NuGet vulnerability feed was unavailable.

### UI follow-up — 2026-09-30

- Removed a duplicate retirement confirmation panel. The retired list is hidden until the
  **Retired developers** button opens a dialog with Edit, Restore, and Close actions.
- The complete solution build succeeded. All 179 WPF tests and 12 screenshot tests passed.
  Screenshot generation rendered 35 images per theme; the retirement and retired-dialog captures
  were inspected in Light and Dark themes.
- Reworked the directory into a two-column page: prominent search and an independently scrollable
  available-developers table on the left, with a permanent add/edit form on the right. Retirement
  confirmation now opens one focused modal; the top row keeps Back and Retired developers at opposite
  edges. Form drafts survive a cancelled retirement, while switching edit targets is disabled until
  the draft is saved or cleared.
- The complete solution built with 0 errors and two NU1900 vulnerability-feed warnings.
  The regression suite passed all 689 tests across seven projects, including 180 WPF tests.
  The screenshot generator rendered 35 images per theme; the updated directory and retirement
  dialog were inspected in Light and Dark themes.

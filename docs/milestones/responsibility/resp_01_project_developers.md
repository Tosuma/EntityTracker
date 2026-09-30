# RESP-01 — Project developers

**Status: planned.** Depends on the existing Project/Tracker model and portable Project snapshots.

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

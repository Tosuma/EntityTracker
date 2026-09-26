# Product Feedback Milestone PF-03 — Responsible Developer

## Goal

Record and display the developer responsible for each entity without imposing a separate user or
identity-management system.

## Behavior and data model

- Add an optional `ResponsibleDeveloper` free-text field to `TrackedEntity`.
- Trim surrounding whitespace while preserving the user's spelling and casing. An empty value clears
  the assignment.
- Keep the value attached to stable entity identity. Rank changes must never move the assignment to
  another entity.
- Make the field editable during manual entity creation and in the existing entity editor.
- Add a `Responsible dev` column to active and archived overview rows.
- Preserve the value through CSV synchronization, dependency edits, archive, restore, backup, and
  schema-name changes.
- Give existing database rows an empty value during migration and support lossless SQLite round trips.
- Add responsible developer as an independently mergeable scalar field in the collaborative conflict
  contract so a future provider does not treat it as notes or schema data.

## Tests and verification

- Verify new and migrated entities default to no responsible developer.
- Verify creation and editing accept ordinary names and team labels, trim outer whitespace, preserve
  casing, and allow clearing.
- Verify repository reads and writes preserve the field.
- Verify CSV synchronization does not overwrite manually entered assignments.
- Verify archive and restore retain the assignment and the archived overview displays it.
- Verify overview rows project the responsible developer into the new column.
- Verify collaborative conflict values can identify this field independently.

## Acceptance criteria

- Users can enter arbitrary responsible-developer text without maintaining user accounts.
- The value is visible in the overview and remains bound to the same stable entity.
- Imports and lifecycle operations do not erase or replace it.

## Out of scope

- Developer accounts, authentication, directories, avatars, or permissions.
- Autocomplete or validation against a list of people.
- Multiple responsible developers on one entity.
- Workload balancing or assignment reporting.

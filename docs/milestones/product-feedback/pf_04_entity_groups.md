# Product Feedback Milestone PF-04 — Entity Groups with Suggestions

## Goal

Allow each entity to carry a free-text group while making existing group names easy to reuse.

## Behavior and data model

- Add one optional `GroupName` free-text value to `TrackedEntity`. A group is a value, not a separate
  managed entity or identifier.
- Trim surrounding whitespace and use an empty value to clear the group.
- Match existing group values case-insensitively for suggestions and reuse their stored canonical
  casing when an exact match is selected.
- Make the field editable during manual creation and in the existing entity editor.
- Add a `Group` column to active and archived overview rows.
- Preserve group membership through synchronization, dependency edits, archive, restore, backup, and
  schema-name changes.
- Give existing rows an empty group during SQLite migration and add group as an independently
  mergeable scalar field in the collaborative conflict contract.

## Suggestions

- Derive suggestions from distinct non-empty groups used by active or archived tracked entities so a
  useful group name does not disappear when its current entities are archived.
- Follow the established dependency-suggestion interaction: update suggestions as the user types,
  match case-insensitively, order deterministically, and cap results at ten.
- Accept free text even when no suggestion matches; creating a new group requires no separate dialog.
- Do not create duplicates that differ only by casing or surrounding whitespace in the suggestion
  list.

## Tests and verification

- Verify new and migrated entities default to no group and that a group can be cleared.
- Verify exact and partial case-insensitive searches, deterministic ordering, deduplication, the
  ten-result limit, and groups that exist only on archived entities.
- Verify selecting an existing suggestion uses its canonical casing while unmatched text remains
  valid.
- Verify creation, editing, SQLite round trips, synchronization preservation, archive/restore, and
  overview projection.
- Verify collaborative conflicts identify group changes independently.

## Acceptance criteria

- A user can type a new group or quickly select an existing one.
- Each entity has at most one group and the overview displays it.
- Group suggestions remain consistent without introducing group-administration infrastructure.
- Imports and lifecycle operations preserve manually maintained group values.

## Out of scope

- Multiple groups per entity, nested groups, or group identities.
- Group rename/delete administration.
- Group-based permissions, reporting, or ranking behavior.

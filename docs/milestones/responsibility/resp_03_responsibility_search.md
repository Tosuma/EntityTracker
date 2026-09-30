# RESP-03 — Responsibility search

**Status: planned.** Depends on RESP-02 and the existing overview search and column filters.

## Goal

Find entities by an individual current developer, including entities with several developers.

## Required behavior

- Show each entity's current developers as separate items in active and archived overviews. Keep
  the full timeline in entity details; past developers do not count as currently responsible.
- Change the `Responsible dev` column filter to offer distinct developers, identified internally
  by stable ID and displayed with initials and optional display name. An entity matches if any of
  its current developers is selected. Offer `(Blank)` only for entities with no current developer.
  Preserve the established OR logic within a column and AND logic across filters.
- Add `Search responsible names` to the Settings page as a saved application preference, defaulted
  on for new and upgraded settings. When on, ordinary active and archived overview search matches
  entity names or any current developer's initials or display name, case-insensitively. When off,
  ordinary search retains entity-name behavior. The existing `Search dependency names` mode
  remains dependency-specific.
- Search and filters use the current Project's developer records. Renames and assignment edits
  refresh results without leaving hidden rows selected for bulk actions.

## Tests and acceptance

- Verify an entity with Alice and Bob appears under either developer, never as a combined filter
  value; verify multiple selected developers, blank, archived rows, and similarly named developers
  with distinct IDs.
- Verify settings migration, saved on/off behavior after restart, initials and display-name
  matching, renamed developers, and dependency-search isolation.
- A user can find a multi-assigned entity by any current developer without typing the full list.

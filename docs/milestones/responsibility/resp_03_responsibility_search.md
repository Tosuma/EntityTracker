# RESP-03 — Responsibility search

**Status: completed (2026-10-01).** Depends on RESP-02 and the existing overview search and column filters.

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

## Completion evidence

- Active and archived overview rows now carry separate current Developer records with stable IDs,
  initials, and optional display names. Both tables render each assignment separately. The
  `Responsible dev` filter offers each current Developer by ID, plus `(Blank)` for unassigned
  rows; selections are OR within that column and compose with the other column filters.
- Settings version 6 saves `Search responsible names`, defaulted on for new and older settings.
  Ordinary search checks entity names and current Developer initials or display names when enabled;
  dependency search remains limited to dependency names. Assignment and Developer edits refresh
  the overview and clear bulk selections when projections change.
- Focused tests cover multiple and similarly named Developers, ID-based filtering after rename,
  Blank and archived rows, filter composition, settings migration and restart, search on/off,
  dependency isolation, SQLite-backed overview projection, and selection clearing. The complete
  solution built with 0 warnings and 0 errors. All seven regression project suites passed: 717
  tests total (60 Domain, 191 Application, 8 DemoData, 23 Reporting, 227 Infrastructure,
  196 WPF, 12 Screenshots). Two unrelated tests failed during the concurrent solution-wide run;
  both passed when rerun in isolation, and their complete Infrastructure and WPF suites passed.
- README and getting-started guidance were updated. The deterministic generator produced 36
  Light and 36 Dark screenshots. Overview and Settings captures were inspected in both themes;
  Developer items have distinct borders, and the enabled setting, labels, keyboard-operable
  filter controls, and accessible item names were checked.

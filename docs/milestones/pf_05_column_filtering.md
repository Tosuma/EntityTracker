# Product Feedback Milestone PF-05 — Column Filtering and Status Sorting

## Goal

Give users an Excel-style way to filter the entity table from selected column headers without
allowing them to add, remove, reorder, or configure which columns support filtering.

PF-05 builds on the responsible-developer and group metadata introduced by PF-03 and PF-04.

## Filterable columns

Filtering must be an explicit, reusable column capability rather than bespoke controls wired into
each header.

- The active Overview supports column menus on `Responsible dev`, `Group`, `Status`, and
  `Work status`.
- The Archived tab supports the same menus on `Responsible dev`, `Group`, and `Status`.
  `Work status` is omitted there because every archived row has the same derived state.
- Other columns retain ordinary headers and cannot be made filterable by the user.
- Declaring another column filterable in a future milestone should require assigning the reusable
  filter-header component and a typed filter key, not copying filter UI or event handlers.

Each filterable header shows a dropdown action. The menu contains:

- a search field for the column's available values;
- `Select all`;
- a checkbox for every available distinct value;
- a `(Blank)` option for an empty responsible developer or group;
- explicit `Apply` and `Clear filter` actions.

Closing the menu without applying must not change the active filter. A filtered header uses an
accented filter indicator. The table also provides a visible `Clear all filters` action.

## Filter semantics

- Selections within one column use OR logic. Selecting `Not started` and `Rework needed`, for
  example, includes a row with either status.
- Filters across columns use AND logic. A `Billing` group filter combined with the two statuses
  above includes only Billing rows whose status is either `Not started` or `Rework needed`.
- Entity-name or dependency-name search is also combined with the column filters using AND logic.
- When opening a column menu, derive its available values from rows that satisfy the current text
  search and every other active column filter. Ignore that column's own current selection while
  calculating its choices.
- Consequently, if the other active filters leave no rows belonging to `Group A`, `Group A` is not
  offered in the Group menu. Values that remain possible can still be selected even when they were
  excluded by that column's previous selection.
- Match and deduplicate responsible-developer and group values case-insensitively while preserving
  their display casing. Filter Status and Work status by their typed enum values rather than their
  formatted labels.
- Filters are session state only. Refreshing or editing reapplies them to newly loaded rows, while
  restarting the application resets them.
- Changing, clearing, or applying a filter clears overview row selection so hidden rows can never
  remain selected for a bulk-status update.

## Status sorting

Only `Status` and `Work status` support sorting. Responsible developer and Group remain filter-only.

- Status forward order is `Not started`, `In progress`, `Rework needed`, `Dev. completed`, and
  `Reconciled`.
- Work-status forward order is `Ready`, `Blocked`, `In progress`, `Rework needed`,
  `Dev. completed`, and `Reconciled`.
- Each applicable menu offers forward and reverse status order using these defined sequences, not
  alphabetical label order.
- Only one custom sort may be active. Choosing a sort from another column replaces the previous one.
- Show the active sort direction in the column header and provide `Clear sort`.
- Sorting changes presentation only. It does not modify or recompute dependency rank. Clearing the
  custom sort restores the normal effective-priority-then-rank ordering.
- The Archived tab supports the defined Status sort but has no Work-status sort.

## Overview and Archived tabs

- Remove the existing `View` selector and the exclusive `OverviewManagerFilter` model. Column
  filters replace its status and readiness views.
- Retain the active status summary cards as shortcuts. A status card replaces the Status column's
  selection with that one status while preserving filters on other columns.
- The `Total` summary card clears every column filter and custom sort while preserving the text
  search. Summary counts continue to describe the complete active dataset and never change when the
  visible rows are filtered.
- Ready and Blocked are selected through the Work-status column menu rather than separate manager
  views.
- Add an `Archived` tab immediately after Overview and remove archived entities from the Overview
  filter model entirely.
- Give Archived its own entity-name search, filter state, sort state, result summary, empty state,
  and relevant metadata grid. It retains the existing inspect-and-restore action and has no bulk
  status toolbar.
- Active and archived filter state are independent and remain available when switching tabs during
  the current application session.
- Opening an archived match from manual creation first navigates to Archived and then opens its
  details. After a successful restore, refresh both collections, remove the entity from Archived,
  navigate to Overview, and show the restored active entity there.
- Update all tab-index navigation and screenshot automation for the inserted Archived tab.

## Presentation model

- Represent filterable columns with a typed key covering Responsible developer, Group, Status, and
  Work status.
- Give each active or archived column filter its own staged selections, applied selections,
  available options, active-filter indicator, and commands for Apply and Clear.
- Represent the optional custom sort with its typed column and forward/reverse direction.
- Keep filtering and sorting in testable presentation logic over the already loaded overview rows;
  do not move it into Domain, persistence, or SQL queries.
- Keep active and archived source collections separate from their filtered/sorted projections.

## Tests and verification

- Verify one and multiple selections use OR logic within a column.
- Verify Responsible developer, Group, Status, and Work-status filters combine with AND logic.
- Verify the Team Lead can combine `Not started` and `Rework needed` in the Status menu.
- Verify available-value lists honor search and all other filters while ignoring their own applied
  selection.
- Verify case-insensitive metadata matching and deduplication, preserved display casing, `(Blank)`,
  value search, Select all, Apply, Clear, and closing without Apply.
- Verify filter indicators, result summaries, filtered empty states, `Clear all filters`, refresh,
  and editing while filters are active.
- Verify summary-card shortcuts preserve other column filters, while Total clears column filters and
  custom sorting but retains text search.
- Verify forward and reverse Status and Work-status ordering, single-sort replacement, sort
  indicators, and restoration of default priority/rank ordering.
- Verify Responsible developer and Group do not expose sort commands.
- Verify applying a filter or sort clears the bulk-status row selection.
- Verify the Archived tab has independent search/filter/sort state, excludes Work-status controls,
  and supports inspection and restore navigation.
- Add a WPF/XAML regression check proving only the four specified active columns and three applicable
  archived columns expose filter menus.
- Update screenshot generation and its tab navigation, then regenerate any README image whose UI is
  materially changed.

## Acceptance criteria

- Users can open a supported column header and filter by any currently available value combination.
- Multiple columns compose predictably using OR within a column and AND across columns.
- Only Status and Work status can be sorted, and they use the defined workflow order rather than
  alphabetic order.
- The old View selector is gone; Ready and Blocked remain accessible through Work status.
- Archived entities have a dedicated tab with applicable filtering and their existing restore flow.
- Filters and sorts affect presentation only and cannot cause hidden bulk edits or persistence
  changes.
- Adding another filterable column later requires configuration of the reusable header/filter model,
  not another independent implementation.

## Out of scope

- User-defined, removable, reorderable, or dynamically filterable columns.
- Alphabetical sorting of Responsible developer or Group.
- Multi-column sorting or sorting Priority, Rank, Entity, Source, Dependencies, or Notes.
- Saved, shared, or persisted filter presets.
- Server-side filtering, changes to status definitions, or changes to dependency ranking.

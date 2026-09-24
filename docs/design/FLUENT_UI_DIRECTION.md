# EntityTracker Fluent UI design direction

## Purpose

EntityTracker will adopt Microsoft's Fluent design language while remaining a .NET 10 WPF
application. The redesign is intended to improve information hierarchy, navigation, consistency,
accessibility, and manager-oriented workflows. It is not a rewrite of Domain, Application,
Reporting, or persistence behavior.

The preferred technical baseline is:

```text
Microsoft Fluent / Fluent 2 design language
        ↓
.NET 10 WPF built-in Fluent theme
        ↓
EntityTracker semantic resources and workflows
```

Standard WPF controls and built-in theming should be used where they satisfy the interaction.
EntityTracker must not depend on a third-party Fluent component framework or recreate WinUI
controls solely for visual imitation.

## Audience and product questions

The primary audience is a Team Lead or Project Manager. The interface should help that user answer:

- What needs attention?
- What can be implemented now?
- What is blocked or unresolved?
- What changed during synchronization?
- How far along is each tracker, project, and the complete portfolio?

Terminology should describe work and decisions rather than internal software types, database tables,
or graph implementation details.

## Projects, trackers, and portfolio context

EntityTracker will manage more than one implementation tracker.

- A **Project** groups related implementation tracks.
- A **Tracker** owns an independent set of entities, dependencies, planning metadata, progress, and
  history.
- Unrelated projects remain separate but can be summarized in the same portfolio.
- Related trackers can be compared side by side without loading another file or restarting the
  application.
- SQLite remains one local catalog and working cache. Project and tracker context are first-class
  data, not separate game-save-like database files.

The application opens to a portfolio/project experience and keeps the selected Project and Tracker
visible in the shell. Tracker-scoped pages update in place when the selected tracker changes.

Creating a tracker supports blank creation, initialization from CSV, or a one-time copy from an
existing tracker. Copying reproduces active entities, dependency facts and corrections, unresolved
names, groups, and requested priorities. It creates independent identities and resets development
status, history, notes, and responsible developer. The source and copy do not remain synchronized.

Projects and trackers use a recycle stage before permanent deletion. Permanent purge is separately
guarded by requiring the exact name. Deleting a project affects its complete tracker hierarchy.

## Information architecture

Use a persistent grouped left navigation rather than the current horizontal tab strip.

```text
Portfolio
Project dashboard

Tracker
  Overview
  Archived
  Reports

Manage
  Schema synchronization
  Add entity

Utilities
  Help & SQL
  Settings
```

Ready and Blocked remain Work-status filters within Overview. They are not separate destinations.
Import CSV and Add Entity remain focused destinations rather than shortcuts on the Overview page.
The SQL query remains independently available and is never a prerequisite for importing.

The obsolete SharePoint Connections page and configuration are removed. The shell does not expose
a remote connection, repository, or Sync control before a separately approved synchronization
milestone provides working behavior.

## Density and layout

Use a hybrid administration layout:

- keep entity tables compact enough for high row counts;
- give page headings, summaries, dashboards, forms, and review sections more breathing room;
- avoid large decorative whitespace that displaces operational data;
- support the minimum window size, common DPI scaling, and wide desktop layouts;
- use virtualization for long tables and comparison matrices.

The default active entity table shows manager essentials:

- Priority;
- Rank;
- Entity;
- Work status;
- Development status;
- Responsible developer;
- Group;
- Blockers;
- row actions.

Provenance, dependency counts and full dependency lists, notes, timestamps, and other secondary
information belong in a read-only right-side details pane. Opening the pane must not interfere with
extended row selection for bulk status updates. Editing remains accessible only through the row
kebab and its guarded modal.

## Fluent visual language

Use the built-in WPF Fluent theme as the control baseline and layer small semantic resources over
it. Do not scatter arbitrary sizes or raw colors through feature XAML.

Shared WPF resources should cover:

- typography roles;
- a compact spacing scale;
- control and row sizing;
- semantic surfaces and borders;
- corner radii where needed;
- focus indication;
- brand and status colors;
- primary, secondary, and destructive command variants.

EntityTracker continues to use the accepted brand palette in
[`DESIGN_GUIDE.md`](DESIGN_GUIDE.md). Coral remains reserved for blocked, rework, destructive,
missing/removal, and error conditions. Retained unresolved import references continue to use the
centralized yellow warning palette.

Status and readiness are displayed as compact labeled badges. Icons and color support the label;
color must never be the only signal.

## Theme behavior

Support `Light`, `Dark`, and `System` appearance modes. `System` is the default and follows Windows.
The choice is local presentation configuration and must not be synchronized as tracker data.

Application resources must be theme-aware. Exported report images retain a stable, accessible
report palette so an export does not unexpectedly change because the operator changed the live
application theme.

## Interaction principles

- Preserve accepted filtering semantics: OR within a column and AND across columns.
- Keep filter and status-sort actions in Fluent column-header flyouts. Do not add a duplicate filter
  pane or applied-filter chip system.
- Preserve lazy entity/dependency search and `Ctrl+F`.
- Preserve clickable summary cards as status-filter shortcuts.
- Keep bulk status selection atomic and clear selection when context or projection changes.
- Keep Complete/Partial synchronization semantics and actionable review behavior unchanged.
- Keep entity creation warnings non-fatal for deliberately unresolved dependencies.
- Keep archive/restore reversible and archive confirmation difficult to trigger accidentally.
- Prompt before changing tracker context when an edit or synchronization review would be discarded.

## Reporting and comparison

`Progress` becomes `Reports` for tracker-level charts and exports.

Portfolio and Project dashboards also show aggregate progress. Aggregate counts and percentages are
entity-weighted: every active entity instance in every included active tracker contributes to the
total. Tracker-specific cards remain visible so a large tracker does not obscure individual track
performance.

A Project can show an entity comparison matrix across its trackers. Rows match by the established
normalized schema key. Missing entities are explicit, and actionable differences are shown by
default with an option to show every entity. Unrelated projects do not share an entity matrix.

All aggregation and comparison calculations remain in Application or Reporting. WPF only presents
their query models.

## Accessibility baseline

Accessibility applies to every UX milestone, not only the final audit.

- Every workflow must be operable with a keyboard.
- Focus must remain visible in light and dark themes.
- Icon-only actions require automation names and useful tooltips.
- Status is never communicated by color alone.
- Tab order must follow visual and task order.
- Text and interactive states must retain accessible contrast.
- Dialogs and flyouts must place and restore focus predictably.
- Layouts must remain readable under ordinary Windows DPI scaling.

## Architectural constraints

- WPF remains presentation and composition only.
- Domain and Application must not reference WPF, Fluent resources, or control types.
- Ranking, readiness, synchronization, dependency resolution, validation, persistence, history, and
  reporting calculations stay outside Views and code-behind.
- The redesign may split the current large window into focused Views and presentation models, but
  must not duplicate business rules to simplify binding.
- Tracker scope is explicit at Application and persistence boundaries; do not introduce a mutable
  global tracker singleton.
- Git synchronization is not part of the Fluent roadmap.

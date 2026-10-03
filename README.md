# EntityTracker

[![CI](https://github.com/Tosuma/EntityTracker/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Tosuma/EntityTracker/actions/workflows/ci.yml)
[![Platform: Windows](https://img.shields.io/badge/platform-Windows-0078D4)](docs/DEVELOPMENT.md)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)](global.json)
[![UI: WPF](https://img.shields.io/badge/UI-WPF-0C54C2)](src/EntityTracker.Wpf)
[![License: MIT](https://img.shields.io/badge/license-MIT-yellow.svg)](LICENSE)

EntityTracker is a Windows desktop application for planning database implementation work in
dependency-safe order. It imports PostgreSQL schema relationships, keeps progress attached to
stable entities, highlights blockers, and turns implementation history into useful progress
reports.

![EntityTracker portfolio showing projects and implementation progress](images/light/portfolio.png)

## Why EntityTracker?

Large database schemas are rarely implemented safely in alphabetical order. Tables depend on
other tables, exported schemas may contain unknown references, and re-importing a changing schema
must not erase project-management data.

EntityTracker keeps those concerns separate:

- imported schema facts describe what depends on what;
- manual overrides capture real-world corrections without being lost on the next import;
- stable entity identities retain status, notes, lifecycle, and history;
- ranking and readiness are recalculated from the effective dependency graph;
- unresolved and unfinished dependencies remain visible as actionable blockers.

## Highlights

- **Safe schema synchronization** — preview Complete or Partial PostgreSQL CSV imports before
  applying additions, dependency changes, or archives.
- **Dependency-aware planning** — rank entities so dependencies appear before the entities that
  use them, while preserving unknown references as unresolved dependencies.
- **Manual tracking and correction** — create entities, add or suppress dependencies, update notes
  and progress, and archive or restore entities.
- **Workflow visibility** — combine Excel-style filters on Responsible dev, Group, Status, and
  Work status; sort workflow statuses in their defined order; and search by entity or dependency
  name.
- **Progress reporting** — inspect current status, implementation history, ready-versus-blocked
  trends, and weekly change; copy or export charts as PNG files.
- **Project portfolio** — compare entity-weighted Project/Tracker progress, persisted trends, and
  normalized entity differences without opening another window or database.
- **Safe catalog management** — create blank, CSV-backed, or copied Trackers and rename, recycle,
  restore, or guardedly purge Projects and Trackers.
- **Local-first reliability** — SQLite persistence, automatic daily and pre-migration backups,
  rolling logs, and documented recovery procedures.
- **Accessible Fluent workflow** — built-in .NET 10 WPF Fluent controls, Light/Dark/System themes,
  keyboard-safe dialogs, visible focus, labeled status, and automation names for repeated actions.
- **Replaceable core** — Domain, Application, and Reporting remain independent of WPF, SQLite,
  CSV libraries, and future synchronization infrastructure.

## How it works

1. Run the built-in PostgreSQL extraction query and export its result as a semicolon-delimited CSV.
2. Choose Complete or Partial synchronization and review every actionable difference.
3. Apply the reviewed schema while EntityTracker preserves stable progress, notes, and history.
4. Use dependency-safe rank, readiness, blockers, filters, and search to choose the next work item.
5. Update development status and use Reports to communicate delivery trends.

## Screenshots

<!-- Generated with scripts/Generate-ReadmeScreenshots.ps1. See docs/DEVELOPMENT.md. -->

The deterministic screenshot suite contains matching [`images/light`](images/light) and
[`images/dark`](images/dark) captures for every state shown below. The README uses the light set
for consistency.

From a Project dashboard, select **Developers** to manage that Project's developer directory.
Search by initials or display name in the left column; the available-developers list scrolls
independently. Use the form on the right to add a developer or edit a selected one.
Initials are required and unique among available developers in the Project; retired records can
be restored when their initials are free. Retiring a developer requires entering their exact initials.
The retired list stays hidden until you select **Retired developers**.
In entity creation or editing, search available Project developers and select everyone currently
responsible. Add new developers on the Project Developers page. Removing someone ends their current
assignment; reassigning them starts a new dated period. Entity details show all current assignments
and up to three timeline entries. Select **View full history** for the complete timeline, then
**Back** to return to the entity. Current assignments appear by newest start time, followed by
past assignments by newest end time. Retiring a developer ends all their open assignments, including
archived entities, without deleting their history.

<table>
  <tr>
    <td width="50%">
      <strong>Manage the implementation portfolio</strong><br />
      Compare active Projects using entity-weighted progress summaries.<br /><br />
      <img src="images/light/portfolio.png" alt="EntityTracker portfolio with active project summary cards" />
    </td>
    <td width="50%">
      <strong>Compare related Trackers</strong><br />
      Review aggregate history, per-Tracker context, and actionable entity differences.<br /><br />
      <img src="images/light/project-dashboard.png" alt="EntityTracker project dashboard with tracker summary cards" />
    </td>
  </tr>
  <tr>
    <td width="50%">
      <strong>Review schema synchronization</strong><br />
      Compare a complete or partial PostgreSQL snapshot before changing tracked state.<br /><br />
      <img src="images/light/schema-synchronization.png" alt="Schema Synchronization page with Complete and Partial import choices" />
    </td>
    <td width="50%">
      <strong>Report progress over time</strong><br />
      See manager summaries, status distribution, implementation history, and blockers.<br /><br />
      <img src="images/light/progress.png" alt="Reports page with status pie chart and implementation history charts" />
    </td>
  </tr>
  <tr>
    <td width="50%">
      <strong>Create tracked entities</strong><br />
      Add manual entities with resolved or deliberately unresolved dependencies.<br /><br />
      <img src="images/light/add-entity.png" alt="Add Entity page for creating a tracked entity and selecting dependencies" />
    </td>
    <td width="50%">
      <strong>Edit without losing imported facts</strong><br />
      Update development status, notes, lifecycle, and manual dependency corrections.<br /><br />
      <img src="images/light/edit-entity.png" alt="Edit Entity modal with status, notes, dependencies, and archive controls" />
    </td>
  </tr>
</table>

| Project developers | Confirm retirement | Retired developers |
| --- | --- | --- |
| ![Project Developers directory with available developers](images/light/project-developers.png) | ![Single initials confirmation for retiring a developer](images/light/project-developer-retirement.png) | ![Retired developers dialog with restore action](images/light/project-developers-retired.png) |

### Import and synchronize an existing Git checkout

Clone and configure a dedicated Project repository with command-line Git first. On the Portfolio,
choose **Import Project** and select its clean working-tree root. EntityTracker imports the
snapshot with its stable IDs and history. If its Project name is already used locally, choose a
private name for this installation; the shared snapshot keeps its original name.

The Project dashboard shows the linked repository, checked-out branch, upstream, current sync
state, last result, and the latest sync duration for this session. **Sync now** fetches
and applies upstream changes, merges independent Project edits,
and asks you to review competing changes before committing and pushing. It waits for an unfinished
edit to be saved or closed before applying incoming data. A checkout without an upstream keeps
local commits only. Permanent deletion of a linked Project publishes a tombstone; a member with
concurrent local changes can keep the Project locally as an unlinked copy. Unlinking by itself
leaves both the repository and SQLite data in place. EntityTracker does not clone repositories,
configure Git, or manage credentials. Automatic sync is enabled by default: it checks linked
Projects after startup and every five minutes. In **Settings**, choose 1, 5, 15, 30, or 60 minutes,
or turn it off while keeping **Sync now** available. A Project with an unfinished edit waits until
the edit is saved or closed; other Projects continue. Conflict and deletion review still require
manual action. Routine automatic checks are quiet; a notification stays visible when action is
needed.

The Tracker Overview **Export** menu saves the active entity table as an Excel workbook or CSV file.

![Overview Export menu with Excel and CSV options](images/light/overview-export-menu.png)

In **Settings → Overview**, choose **Shown entities** to export the current search, filters, and
sort, or **All active entities** to ignore search and filters while keeping the sort. The CSV
separator is also selected there. Choose the file location and name in the save dialog; a
notification reports when the export finishes or fails.

**Settings** is split into categories: **General** (appearance), **Project** (your local Developer
for any Project, independent of the current context), **Overview** (search and export), **Sync**
(automatic Project sync), and **About** (app version).

![Settings General page with the appearance choice](images/light/settings.png)

![Settings Project page with the Project picker and local Developer choice](images/light/settings-project.png)

![Settings Overview page with overview search and export options](images/light/settings-overview.png)

![Settings Sync page with automatic Project sync and interval](images/light/settings-sync.png)

![Settings About page with the app version](images/light/settings-about.png)

For slow syncs, the daily application log records stage durations, fetch time, snapshot read time,
and the number of snapshot files read or reused. The remote may still take time to fetch or push;
the application keeps its validation and remote recheck steps in order.

![Illustrative Project dashboard with an upstream Git link, last sync timing, and manual Sync now control](images/light/project-git-repository.png)

The sidebar notification center shows the current sync phase. A completed sync disappears after
a short display time. A sync that needs attention stays visible with a contextual action; the
Project card also retains its detailed status. Notifications are kept for the current session.

![Project sync progress in the sidebar notification center](images/light/project-sync-progress.png)

![Project sync action needed in the sidebar notification center](images/light/project-sync-action-needed.png)

The merge review shows each conflicting object with its base, local, and remote values. Choose a
side for each conflict or apply one choice to all, then confirm the resolved Project state.

![Project merge review with base, local, and remote values and per-conflict choices](images/light/project-merge-review.png)

### Compare sibling Trackers

The Project matrix aligns active entities by normalized source key. It starts with actionable
differences—including missing entities, divergent statuses, blockers, rework, and unresolved
references—and can explicitly show all entities. The category cards filter the matrix and reuse
the Overview status colors for fast scanning. Each present cell opens that entity in its Tracker's
Overview after the normal unsaved-work confirmation.

![Project entity comparison with labeled statuses and explicit missing entities](images/light/project-comparison.png)

### Manage Tracker lifecycle

Recycling a Tracker is reversible and returns to its owning Project dashboard. The Project-scoped
recycle bin keeps the removed Tracker available for restore without losing its entities or history;
restoring it also returns to that Project dashboard.

| Recycle confirmation | Project recycle bin | Restored Project dashboard |
| --- | --- | --- |
| ![Confirm recycling a Tracker](images/light/tracker-recycle-confirmation.png) | ![Project recycle bin with a Tracker available to restore](images/light/tracker-recycle-bin.png) | ![Project dashboard after restoring its Tracker](images/light/project-dashboard-tracker-restored.png) |

Permanent deletion is deliberately separate from recycling, lists the affected data, requires the
exact Tracker name, and starts with focus on the safe Cancel action.

![Guarded permanent Tracker deletion confirmation](images/light/tracker-permanent-delete-confirmation.png)

### Find and maintain tracked entities

In **Settings**, choose **You in this Project** from the available Project Developers. This
choice is local to this installation and can differ by Project. **Assign me** in active entity
details adds that Developer immediately; **Assign me** in the editor stages the assignment until
you save. If no available Developer is selected, the action offers a link to Settings. Changing
the choice does not change existing assignments or their history.

Use the dropdown on a supported column header to select individual current Project Developers,
groups, statuses, or work statuses. An entity with several Developers appears under each one;
**(Blank)** finds entities with no current Developer. Selections within a column are alternatives,
while filters on different columns work together. With no values checked, all rows are shown; choose values
and press **Apply** to narrow the list. **Clear filter** unchecks every value and leaves the menu
open. Status summary cards remain useful one-click shortcuts. Ordinary search matches entity names
and, by default, current Developer initials and
display names. Turn off **Search responsible names** in Settings to search entity names only.
**Search dependency names** remains a separate mode. Open search with <kbd>Ctrl</kbd>+<kbd>F</kbd>.

Development status keeps the user-set detail: **Rework needed** means work is pending,
**Reworking** means it is active, and **Blocked** manually pauses an entity. Work status shows
Ready for Not started or Rework needed, In progress for development or Reworking, and
Waiting on dependencies for unstarted entities with unmet dependencies. A manually Blocked
entity shows Blocked. The Blockers column shows unmet dependencies during active work.

![EntityTracker Responsible dev filter with separate Project Developers and a Blank option](images/light/overview-filter-flyout.png)

![EntityTracker overview filtered by the dependency name unit](images/light/overview-search.png)

Archived entities have their own tab and independent search, filters, and Status sort. They remain
available as read-only records with their progress, notes, and dependencies intact and can be
deliberately restored from the archived view.

Entity names and each row's **View details** action open a read-only side pane with priorities,
rank, provenance, assignment, full notes, effective dependencies, blockers, and audit timestamps.
Use **Edit** in the details header to open the entity editor. Opening or closing the pane does not
disturb bulk row selection.

The Add Entity and edit workflows use searchable Fluent suggestion controls. Unknown dependency
names are added only through the explicit **Add as unresolved** action, while archive remains a
separate reversible action with confirmation.

![EntityTracker read-only entity details pane](images/light/overview-details.png)

![EntityTracker full responsibility history in the entity details pane](images/light/responsibility-history.png)

![EntityTracker edit modal focused on dependencies and explicit unresolved additions](images/light/edit-entity-dependencies.png)

![EntityTracker reversible archive confirmation naming the selected entity](images/light/archive-entity-confirmation.png)

![Archived EntityTracker entity with its preserved details and Restore entity action](images/light/archived-entity.png)

![EntityTracker read-only archived entity details pane with preserved dependencies](images/light/archived-details.png)

### Understand dependency blockers

Entities with unresolved references—or dependencies that are themselves unresolved—are marked
directly in the overview. Selecting the warning icon explains the graph state and lists the
unresolved names affecting that entity, while the Blockers column shows unresolved or not-yet-
implemented direct dependencies.

![EntityTracker overview showing dependency warning icons, missing dependencies, and details for an upstream-unresolved entity](images/light/overview-missing-entities-as-dependencies.png)

### Explore the dependency graph

**Dependency graph** shows the selected Tracker's active entities as a solar system. Foundation
entities without dependencies sit in the centre, with a clearly dominant one, the entity most
others need, in the exact middle. Each ring outward adds one more layer of dependencies, so the map
reads from the centre outward in build order; within a ring, entities earlier in the
dependency-safe rank sit closer to the centre. A lone foundation is the centre itself, and
missing dependencies orbit on the outermost ring, straight out from the entities that need them. A larger dot means more entities refer to it
directly. Entities are placed straight out from what they link to, so chains run outward instead of
across the map, and only direct links are drawn, never ones a longer chain already implies. Links stay
faint until you hover over an entity. Click an entity to highlight everything it depends on, back
towards the centre; **Focus on selection** hides the rest, and **Hide unconnected** removes
entities without links. Find an entity by name, double-click it to open its details, drag it to a
new spot, or export the current view as a PNG.

![EntityTracker dependency graph showing every active entity colored by status](images/light/dependency-graph.png)

![Dependency graph with one entity selected and its dependency chain highlighted](images/light/dependency-graph-selected.png)

![Entity details pane opened from the dependency graph](images/light/dependency-graph-details.png)

### Import review details

Changed entities show dependency additions and removals directly, with any required progress choice
kept beside the affected entity before Apply becomes available.

![Schema synchronization review showing dependency additions, removals, and progress-impact choices](images/light/schema-synchronization-changed-entities.png)

Complete imports make potentially removed entities explicit before anything is saved. Entities
missing from the new snapshot are proposed for soft-archiving, while their progress and notes are
preserved.

![Schema synchronization review showing entities missing from a Complete snapshot and proposed for soft-archiving](images/light/schema-synchronization-import-csv-with-missing-entities.png)

Unknown dependency references do not make an otherwise valid import fail. EntityTracker retains
them as unresolved dependencies, shows exactly which entities are affected, and keeps them blocked
until matching entities become available.

![Schema synchronization review showing retained unresolved dependencies and their missing entity names](images/light/schema-synchronization-unresolved-dependencies.png)

### Extract a PostgreSQL schema

Help & SQL explains Portfolio, Project, and Tracker context; statuses and blockers; import modes;
Reports; and lifecycle actions. It also provides the versioned PostgreSQL query used to produce a
compatible schema CSV without requiring a live database connection inside EntityTracker.

![EntityTracker Help and SQL guidance](images/light/help-and-sql.png)

![EntityTracker PostgreSQL schema extraction query helper](images/light/sql-query.png)

## Project status

Product Milestones 1–12, PF-01 through PF-05, and UX-01 through UX-08 are complete. EntityTracker uses
SQLite as its local catalog and working store. Obsolete SharePoint presentation and runtime
configuration have been retired. Manual and automatic Git synchronization are available for
existing checkouts.

A separate
[PF-01–PF-05 product feedback milestone group](docs/milestones/product-feedback/00_README.md)
records the implemented bulk status updates, customer priority, responsible-developer and group
metadata, and column filtering with status-order sorting. The
[RESP-01–RESP-04 Responsibility roadmap](docs/milestones/responsibility/00_README.md) records
completed Project developers, dated assignments, responsibility search, and local `Assign me`.
The independent
[CI-01 engineering milestone](docs/milestones/engineering/ci_01_continuous_integration.md) defines
CI validation for pull requests, pushes to `main`, and app release tags; successful pushes are packaged.
The live badge above reports the current `main` build status.

The [GS-01–GS-05 Git-sync roadmap](docs/milestones/git-sync/00_README.md) now includes portable
snapshots, local linking, existing-checkout import, collaborative merge, and automatic checks. Prepare
and configure a clean dedicated repository outside EntityTracker. Use **Import Project** on the
Portfolio for a Project already present in the repository, or **Link repository** on an existing
Project dashboard. **Sync now** merges independent edits, offers conflict review when needed,
commits locally, and uses the configured upstream when present. **Unlink** removes only the local
association. **Settings** controls the application-wide automatic schedule; **Sync now** remains
available when automatic sync is disabled.

See the [milestone status](docs/milestones/milestone_status.md) and complete
[roadmap](docs/milestones/00_README.md) for details.

## Technology and architecture

EntityTracker targets .NET 10 and uses WPF, SQLite, CsvHelper, and LiveCharts. The solution keeps
dependencies pointing inward:

```text
WPF composition and presentation
              ↓
Infrastructure and Reporting
              ↓
Application use cases
              ↓
Domain model
```

Business rules do not depend on WPF or infrastructure technologies. Read the
[architecture rules](docs/architecture/ARCHITECTURE.md) and
[collaborative storage status](docs/architecture/COLLABORATIVE_STORAGE.md) for the current storage
boundary. Git is used for Project dashboard link and sync actions and scheduled background passes;
SQLite remains the
runtime store for editing, reporting, and navigation.

## Getting started

EntityTracker currently runs on Windows and uses the .NET SDK pinned by `global.json`.

See the [getting started guide](docs/GETTING_STARTED.md) for prerequisites, one-command local
installation, updates, and first steps. Colleagues build approved release tags on their own
computers; the app checks for updates and guides them through rebuilding when one arrives.

![Required app update blocking the workspace](images/light/app-update-required.png)

The [development guide](docs/DEVELOPMENT.md) contains complete instructions for publishing, CI,
screenshots, schema import details, and locating local application data.

## Documentation

- [Getting started](docs/GETTING_STARTED.md)
- [Development, build, and run guide](docs/DEVELOPMENT.md)
- [Design and color guide](docs/design/DESIGN_GUIDE.md)
- [PostgreSQL schema CSV contract](docs/importing/schema-csv-contract-v1.md)
- [Local backup, logs, and recovery](docs/operations/RECOVERY.md)
- [Architecture rules](docs/architecture/ARCHITECTURE.md)
- [Roadmap and milestones](docs/milestones/00_README.md)

## Contributing

Contributions are welcome. Start with [CONTRIBUTING.md](CONTRIBUTING.md), which explains the
required verification commands and the architectural constraints that keep the core independent
of UI and storage technologies.

## License

EntityTracker is available under the [MIT License](LICENSE).

# EntityTracker

[![CI](https://github.com/Tosuma/Entity-Dependency-Manager/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Tosuma/Entity-Dependency-Manager/actions/workflows/ci.yml)
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
- **Optional Git collaboration** — retain SQLite-only or local-only Projects, or explicitly Sync a
  dedicated repository through its configured HTTPS/SSH upstream; non-diverged work pushes or
  fast-forwards while divergence is blocked without data loss.
- **Local-first reliability** — SQLite projections, automatic daily and pre-migration backups,
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
5. Update work status and use Reports to communicate delivery trends.
6. Optionally link a Project to an existing empty Git repository for committed history. A remote
   is not required; when an upstream is configured, use the Project-scoped Sync action explicitly.

## Screenshots

<!-- Generated with scripts/Generate-ReadmeScreenshots.ps1. See docs/DEVELOPMENT.md. -->

The deterministic screenshot suite contains matching [`images/light`](images/light) and
[`images/dark`](images/dark) captures for every state shown below. The README uses the light set
for consistency.

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
      <strong>Synchronize an upstream explicitly</strong><br />
      Fetch or push only the managed Project branch through configured HTTPS or SSH tooling.<br /><br />
      <img src="images/light/repository-sync-up-to-date.png" alt="Git-backed Project showing an up-to-date configured upstream and Sync action" />
    </td>
    <td width="50%">
      <strong>Keep repositories local-only</strong><br />
      Continue creating local commits when no upstream is configured.<br /><br />
      <img src="images/light/repository-local-only.png" alt="Local-only Git-backed Project with Sync disabled and an explanatory message" />
    </td>
  </tr>
  <tr>
    <td width="50%">
      <strong>Publish offline commits</strong><br />
      See ahead/behind state before pushing local work normally.<br /><br />
      <img src="images/light/repository-sync-ahead.png" alt="Git-backed Project showing three local commits ahead of its upstream" />
    </td>
    <td width="50%">
      <strong>Block unsafe divergence</strong><br />
      Preserve local HEAD and SQLite when semantic merge is required.<br /><br />
      <img src="images/light/repository-merge-required.png" alt="Git-backed Project showing merge-required state with Sync safely disabled" />
    </td>
  </tr>
  <tr>
    <td width="50%">
      <strong>Recover a stale local cache</strong><br />
      Rebuild one Git-backed Project from authoritative repository HEAD without contacting a remote.<br /><br />
      <img src="images/light/repository-stale-cache.png" alt="Git-backed Project showing a stale-cache status and Rebuild cache action" />
    </td>
    <td width="50%">
      <strong>Locate a moved repository</strong><br />
      Restore a Project-scoped repository association while keeping other Projects available.<br /><br />
      <img src="images/light/repository-unavailable.png" alt="Git-backed Project showing an unavailable repository and Locate repository action" />
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
      Update work status, notes, lifecycle, and manual dependency corrections.<br /><br />
      <img src="images/light/edit-entity.png" alt="Edit Entity modal with status, notes, dependencies, and archive controls" />
    </td>
  </tr>
</table>

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

Use the dropdown on a supported column header to select any combination of responsible developers,
groups, statuses, or work statuses. Selections within a column are alternatives, while filters on
different columns work together. Status summary cards remain useful one-click shortcuts. Search
entity names and, when needed, dependency names from the overview; the same search opens with
<kbd>Ctrl</kbd>+<kbd>F</kbd>.

![EntityTracker Work status column filter with staged choices and typed sorting](images/light/overview-filter-flyout.png)

![EntityTracker overview filtered by the dependency name unit](images/light/overview-search.png)

Archived entities have their own tab and independent search, filters, and Status sort. They remain
available as read-only records with their progress, notes, and dependencies intact and can be
deliberately restored from the archived view.

Entity names and each row's **View details** action open a read-only side pane with priorities,
rank, provenance, assignment, full notes, effective dependencies, blockers, and audit timestamps.
Opening or closing this pane does not disturb bulk row selection; editing remains in the row action
menu.

The Add Entity and edit workflows use searchable Fluent suggestion controls. Unknown dependency
names are added only through the explicit **Add as unresolved** action, while archive remains a
separate reversible action with confirmation.

![EntityTracker read-only entity details pane](images/light/overview-details.png)

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

Product Milestones 1–12 and UX Milestones UX-01 through UX-08 are complete. EntityTracker currently
uses SQLite as its local catalog and working store. Obsolete SharePoint presentation and runtime
configuration have been retired, and the application exposes no remote synchronization control.

A separate
[PF-01–PF-05 product feedback milestone group](docs/milestones/feedback/README.md)
plans bulk status updates, customer priority, responsible-developer and group metadata, and
column filtering with status-order sorting without extending the numbered roadmap. The independent
[CI-01 engineering milestone](docs/milestones/engineering/ci_01_continuous_integration.md) now validates pull
requests and pushes to `main` and packages successful `main` builds. The live badge above reports
the current `main` build status; CI-01 remains in progress until the `main` package artifact is
verified.

See the [milestone status](docs/milestones/milestone_status.md) and complete
[roadmap](docs/milestones/README.md) for details. RS-01 through RS-03 of the
[remote-sync roadmap](docs/milestones/remote-sync/README.md) are implemented. Semantic merge and
final recovery/security hardening remain planned as RS-04 and RS-05.

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
[collaborative storage direction](docs/architecture/COLLABORATIVE_STORAGE.md) for the preserved
boundary and approved Git roadmap. Explicit non-diverged synchronization is implemented; semantic
merge remains deliberately unavailable.

## Getting started

EntityTracker currently runs on Windows and uses the .NET SDK pinned by `global.json`.

The [development guide](docs/DEVELOPMENT.md) contains prerequisites and complete instructions for
cloning, restoring, building, testing, running, publishing, importing a schema, and locating local
application data.

## Documentation

- [Development, build, and run guide](docs/DEVELOPMENT.md)
- [Design and color guide](docs/design/DESIGN_GUIDE.md)
- [PostgreSQL schema CSV contract](docs/importing/schema-csv-contract-v1.md)
- [Local backup, logs, and recovery](docs/operations/RECOVERY.md)
- [Architecture rules](docs/architecture/ARCHITECTURE.md)
- [Roadmap and milestones](docs/milestones/README.md)

## Contributing

Contributions are welcome. Start with [CONTRIBUTING.md](CONTRIBUTING.md), which explains the
required verification commands and the architectural constraints that keep the core independent
of UI and storage technologies.

## License

EntityTracker is available under the [MIT License](LICENSE).

# UX-01 — Projects and Trackers Foundation

## Context

EntityTracker currently has one SQLite database, one global normalized entity-name space, global
progress snapshots, and one latest-import summary. Application services and repository reads assume
that every persisted entity belongs to the same tracker.

The approved UX direction requires related Trackers grouped into Projects, seamless context
switching, independent progress, one-time tracker copying, and later cross-tracker comparison. This
cross-layer milestone is therefore an explicit prerequisite to the visual redesign.

## Goal

Make Project and Tracker first-class, strongly typed concepts while keeping the existing application
usable against an automatically migrated default tracker.

## User-facing outcome

Existing installations reopen with all accepted data and behavior intact. Their current data belongs
to `Default project` / `Default tracker`, ready for the portfolio shell introduced in UX-03.

No project switcher or portfolio dashboard is introduced yet.

## Design decisions

- A Project contains one or more independent Trackers.
- All Projects and Trackers live in one SQLite catalog.
- Tracker scope is explicit at Application and persistence boundaries.
- Project names are case-insensitively unique across the catalog.
- Tracker names are case-insensitively unique within a Project.
- Recycled names remain reserved until restored or permanently purged.
- A copied tracker is a one-time independent copy, not a live template relationship.
- Related trackers are compared later by normalized entity source key.

## Required implementation

### Domain and Application

- Add strongly typed `ProjectId` and `TrackerId` values.
- Add focused Project and Tracker models with name, lifecycle, timestamps, and optional copy-origin
  metadata.
- Make tracker ownership explicit for `TrackedEntity` and validate that resolved dependency edges do
  not cross tracker boundaries.
- Require a `TrackerId` in repository queries and every operation that reads or changes tracker
  state, including overview, synchronization, creation, editing, lifecycle, workflow, history,
  ranking inputs, and reporting.
- Do not use a mutable global/ambient current-tracker service in Domain or Application.
- Add focused project/tracker persistence ports and management operations. Do not introduce a
  generic repository or generic hierarchy framework.

### Tracker creation and copying

Support application operations for:

- creating a Project;
- creating a blank Tracker;
- preparing/committing a new Tracker from a reviewed Complete CSV candidate;
- copying a Tracker from any active Tracker in any active Project;
- renaming, recycling, restoring, and permanently purging Projects and Trackers.

Copy only active source entities. Copy:

- source names and normalized keys;
- imported dependency facts and kinds;
- unresolved dependency names;
- manual dependency additions and suppressions;
- group names;
- requested priorities.

Generate new entity identities and remap every copied resolved relationship. Reset:

- development status to `Not started`;
- notes;
- responsible developer;
- status/progress history, apart from the new tracker's creation baseline.

Copied provenance must truthfully distinguish copied structure from direct CSV/manual creation and
must remain compatible with later CSV confirmation.

CSV-based tracker creation must reuse the existing parser, candidate-state resolution, and review
models. Canceling or failing the operation creates no Tracker or partial entity state.

### Lifecycle

- Recycling a Tracker hides it from active selection and metrics but preserves its complete state.
- Recycling a Project hides the Project and its full Tracker hierarchy without overwriting each
  Tracker's own lifecycle state.
- Restoring a Project restores visibility of the hierarchy while leaving individually recycled
  Trackers recycled.
- Permanent purge is transactional and physically removes the selected Tracker or complete Project
  hierarchy.
- The Application operation accepts an explicit purge request; typed-name confirmation remains a
  presentation responsibility in UX-03.

### Persistence migration

Upgrade the current SQLite schema from version 11.

- Add Project and Tracker tables.
- Add `tracker_id` ownership to tracked entities.
- Replace global entity-source uniqueness with `(tracker_id, source_key)` uniqueness.
- Scope progress snapshots by Tracker.
- Replace the global latest-import singleton with one summary per Tracker.
- Backfill every existing row, dependency, override, history record, snapshot, and import summary
  into one generated default Project and Tracker.
- Preserve globally unique existing `EntityId` values.
- Take the established pre-migration backup and apply the migration transactionally.

Dependency and history tables may infer tracker scope through their entity references where that
keeps the schema simpler. Application validation must still prevent cross-tracker relationships.

### Compatibility bridge

Until UX-03 introduces selection, WPF composition resolves the sole/default active Tracker and passes
its `TrackerId` explicitly to existing presentation operations. It must fail clearly if no active
Tracker can be selected rather than silently mixing catalog data.

## Interaction behavior

This milestone introduces no new navigation. Startup migration is automatic and non-blocking when
successful. Naming prompts, creation wizards, recycle bins, and purge confirmations belong to
UX-03.

## Architectural constraints

- Project/Tracker ownership is a domain/application concern, not a WPF filter.
- Do not simulate trackers with separate SQLite files.
- Do not rebuild dependency/ranking logic per tracker; pass the scoped state into existing logic.
- Do not implement cross-tracker dependency edges.
- Do not add Git synchronization fields or behavior.
- Do not start Fluent styling from UX-02.

## Tests / verification

Automated tests must cover:

- version-11 migration with complete preservation of entities, IDs, dependencies, unresolved
  references, overrides, statuses, notes, groups, assignments, priorities, archives, histories,
  snapshots, and latest import;
- automatic default Project/Tracker creation for existing and empty databases;
- same normalized entity key allowed in different Trackers and rejected within one Tracker;
- strict tracker isolation for every existing read/write service;
- rejection of cross-tracker dependencies;
- blank, CSV, and copied Tracker transactions, including cancellation/failure;
- copied identity remapping, relationship integrity, copy fields, and reset fields;
- Project/Tracker rename uniqueness, recycle, restore, cascade visibility, and purge;
- tracker-scoped progress/history/import summary calculations;
- every accepted regression test.

Manual verification:

- open an existing populated database after migration;
- verify its overview, archive, synchronization, creation, editing, and reports still behave as
  before;
- verify backup/recovery documentation remains accurate.

## Acceptance criteria

- [ ] Projects and Trackers have strongly typed stable identities.
- [ ] Every tracked entity belongs to exactly one Tracker.
- [ ] Entity source keys are unique within, not across, Trackers.
- [ ] Application and persistence operations cannot leak data across Trackers.
- [ ] Existing version-11 data migrates into one default Project/Tracker without loss.
- [ ] Blank, CSV, and copy creation operations are transactional.
- [ ] Copying reproduces structure/planning data, remaps IDs, and resets execution data.
- [ ] Copied Trackers remain independent after creation.
- [ ] Projects and Trackers support recycle, restore, and permanent purge semantics.
- [ ] Progress history and latest-import data are tracker-scoped.
- [ ] The existing WPF application remains runnable against the default Tracker.
- [ ] No Project/Tracker business rule is implemented in WPF.
- [ ] No Fluent shell, portfolio dashboard, Git sync, or SharePoint work is implemented.
- [ ] The complete solution builds and all tests pass.

## Out of scope

- Fluent styling or navigation;
- portfolio/project dashboards;
- user-facing Project/Tracker management;
- cross-tracker reporting and comparison UI;
- live template relationships;
- moving an entity between Trackers;
- dependencies between Trackers;
- Git serialization, authentication, merge, pull, push, or conflict handling.

## Agent planning prompt

```text
Plan UX-01 — Projects and Trackers Foundation.

Read:
- docs/architecture/ARCHITECTURE.md
- docs/design/FLUENT_UI_DIRECTION.md
- docs/milestones/00_README.md
- docs/milestones/ux_00_README.md
- docs/milestones/ux_01_projects_and_trackers_foundation.md

Inspect the current Domain, Application persistence contracts and services, SQLite schema/migrations,
settings, WPF composition, reporting queries, and all tests. Produce a repository-specific plan for
UX-01 only. Pay particular attention to explicit TrackerId scoping, version-11 migration, global
snapshot/import-summary tables, copy identity remapping, lifecycle/purge semantics, and regression
coverage. Do not modify the repository and do not plan UX-02 or Git synchronization.
```

## Agent implementation prompt

```text
Implement UX-01 according to:
- docs/architecture/ARCHITECTURE.md
- docs/design/FLUENT_UI_DIRECTION.md
- docs/milestones/ux_01_projects_and_trackers_foundation.md
- the approved UX-01 implementation plan.

Implement UX-01 only. Keep tracker scope explicit outside WPF, migrate existing data transactionally,
preserve all accepted behavior, and add comprehensive isolation/migration/copy tests. Keep the WPF
application operating on the default active Tracker without implementing portfolio navigation.
Build the complete solution, run all tests, verify every acceptance criterion, and do not begin
UX-02 or Git synchronization.
```


# UX-03 — Application Shell and Portfolio

## Context

UX-01 provides first-class Projects and Trackers, and UX-02 provides a coherent Fluent visual
foundation. The current UI is one large `MainWindow.xaml` with horizontal tabs and a single
orchestrating ViewModel.

## Goal

Introduce the Fluent application shell, portfolio/project hierarchy, seamless tracker selection,
and complete Project/Tracker management without changing tracker-specific business workflows.

## User-facing outcome

A manager can see all Projects, compare the basic state of a Project's Trackers, switch the active
Tracker in place, and create, copy, rename, recycle, restore, or permanently delete Projects and
Trackers without dealing with database files.

## Design decisions

- Use grouped persistent left navigation.
- Show Project and Tracker context persistently.
- Portfolio and Project dashboard are distinct destinations.
- Tracker pages update in place when context changes.
- Current tracker filtering/search state is preserved per Tracker for the application session;
  bulk selection is always cleared on a switch.
- Recycle is reversible; permanent purge requires the exact name.
- Do not show a Git connection or Sync control before that feature exists.

## Required implementation

### Shell structure

- Reduce `MainWindow` to shell composition, navigation, busy/notification presentation, and a modal
  host.
- Extract current page bodies into focused Views/UserControls using existing presentation models.
- Add a typed WPF navigation model rather than string/tab-index navigation.
- Group destinations as documented in `FLUENT_UI_DIRECTION.md`.
- Place Help & SQL and Settings/Appearance in the utility area.
- Remove the SharePoint Connections destination and its presentation-only configuration workflow.
- Reserve no visible placeholder for Git synchronization.

### Context and switching

- Present current Project and Tracker in the shell with accessible selectors.
- Scope every tracker page and command to the selected `TrackerId` supplied by UX-01.
- Restore the last valid active Project/Tracker on startup; otherwise navigate to Portfolio.
- Clear selection and close transient flyouts when switching.
- Preserve independent search/filter/sort state per Tracker for the current session.
- If an editor, creation flow, or unapplied synchronization review is dirty, require explicit
  discard confirmation before switching; cancel keeps the original context and state.

### Portfolio and Project management

- Portfolio lists active Projects with basic active-Tracker/entity/progress counts.
- Project dashboard lists its active Trackers with basic progress and attention summaries.
- Provide focused creation/rename dialogs with case-insensitive duplicate validation.
- New Tracker supports Blank, CSV, and Copy modes.
- CSV mode uses a Complete import candidate and creates nothing until the user applies the review.
- Copy mode can select any active Tracker in any active Project, previews copied/reset fields, and
  commits transactionally.
- Surface the migrated default names with a non-blocking rename prompt.
- Provide Project- and Tracker-level recycle views and restore actions.
- Permanent purge requires entering the exact current name and shows affected Tracker/entity/history
  counts. Project purge clearly identifies the complete hierarchy that will be removed.

Detailed charts and the entity comparison matrix belong to UX-07. Basic summary values in this
milestone must still come from Application/Reporting query models rather than WPF calculations.

## Interaction behavior

- Switching context never opens another process, window, or database file.
- Navigation retains the selected Project/Tracker unless the destination is portfolio-wide.
- Tracker-only destinations are disabled or hidden when no active Tracker exists.
- Canceling creation, rename, recycle, restore, purge, or context discard modifies nothing.
- Recycled items disappear from active selectors and metrics immediately after commit.
- Restoring returns the existing stable Project/Tracker identity.

## Architectural constraints

- Shell ViewModels own navigation and presentation state only.
- Project/Tracker validation, copying, lifecycle, and purge remain Application operations.
- Do not duplicate per-page ViewModels merely to satisfy navigation.
- Do not introduce a generic navigation framework or dependency on third-party shell controls.
- Preserve every existing tracker workflow while moving its View.
- Do not implement Git synchronization or the UX-04 through UX-07 page redesigns.

## Tests / verification

Automated tests must cover typed navigation, startup restoration/fallback, context switching,
per-Tracker transient table state, mandatory selection clearing, dirty-state confirmation,
destination enablement, Project/Tracker CRUD, creation modes, rename validation, recycle/restore,
typed-name purge, canceled actions, and XAML/view composition.

Update screenshot automation to navigate by typed destination rather than tab index.

Manual verification must cover keyboard navigation, selector accessibility, empty portfolio/project
states, many Projects/Trackers, long names, resizing, modal focus, destructive confirmations,
Light/Dark/System themes, and seamless switching between three related Trackers.

## Acceptance criteria

- [ ] Persistent grouped left navigation replaces the horizontal tab strip.
- [ ] Project and Tracker context is always clear on tracker-scoped pages.
- [ ] Switching Trackers updates the current workspace without database-file loading or restart.
- [ ] Dirty work cannot be lost silently during switching.
- [ ] Portfolio and Project dashboards show basic manager summaries.
- [ ] Blank, CSV, and copied Tracker creation are available and cancel safely.
- [ ] Copied Trackers clearly explain copied and reset data.
- [ ] Project/Tracker rename, recycle, restore, and guarded purge are usable.
- [ ] Existing data receives a non-blocking default-name rename path.
- [ ] Existing tracker workflows remain functional in extracted Views.
- [ ] SharePoint Connections UI is removed.
- [ ] No non-functional Git UI is exposed.
- [ ] Business rules remain outside WPF.
- [ ] The complete solution builds and all tests pass.

## Out of scope

- detailed portfolio/project charts;
- entity comparison matrix;
- overview column redesign or details pane;
- redesigning synchronization, creation, editing, or reports content;
- Git repository configuration or synchronization.

## Agent planning prompt

```text
Plan UX-03 — Application Shell and Portfolio.

Read the architecture, Fluent design direction, UX category README, UX-01, UX-02, and this
milestone. Inspect the implemented tracker services, MainWindow/MainWindowViewModel, every current
tab, modal host, navigation test, settings, and screenshot automation. Produce a repository-specific
plan for UX-03 only. Preserve all accepted workflows while extracting focused Views and adding the
shell, selectors, dashboards, and lifecycle flows. Do not modify the repository or plan Git sync.
```

## Agent implementation prompt

```text
Implement UX-03 according to the architecture, Fluent design direction, this milestone, and the
approved plan. Add the typed Fluent shell, portfolio/project context, tracker switching and complete
Project/Tracker management. Preserve current workflow behavior, update navigation and screenshot
tests, build the solution, and run all tests. Do not begin UX-04 or expose Git synchronization.
```


# Database Entity Dependency Tracker — Roadmap

Build a Windows C#/.NET WPF application that imports database relationships from CSV, tracks development per stable entity, computes a dependency-safe implementation order, visualizes progress, and supports multiple implementation trackers grouped into projects.

## Principles
- No business logic in the UI.
- Rank is derived and recomputed; it is never entity identity.
- Progress/notes belong to stable entities, never rows.
- Imported schema data and manual overrides remain distinguishable.
- Infrastructure is replaceable through interfaces and dependency injection.
- Every milestone leaves a runnable/testable solution.
- Add classes/abstractions only when they have a concrete responsibility.

## Product milestones
1. Solution skeleton and domain
2. CSV parsing
3. Dependency graph and ranking
4. SQLite persistence
5. First WPF overview
6. Safe schema synchronization
7. Manual overrides/editing
8. Status, readiness, and blockers
9. History and charts
10. Chart export/reporting
11. SQL-query utility and polish
12. SharePoint-ready boundary and hardening
13. Live SharePoint integration — retired; a future Git synchronization capability will be planned separately

## Product feedback milestones

These milestones address planning and workflow feedback independently of the numbered product
roadmap. They do not continue the Milestone 1–13 sequence:

- [PF-01 — Bulk status updates](pf_01_bulk_status_updates.md)
- [PF-02 — Priority planning and replaceable ranking](pf_02_priority_planning.md)
- [PF-03 — Responsible developer](pf_03_responsible_developer.md)
- [PF-04 — Entity groups with suggestions](pf_04_entity_groups.md)
- [PF-05 — Column filtering and status sorting](pf_05_column_filtering.md)

## Independent engineering milestones

These milestones improve repository engineering and may be implemented independently of the
numbered product roadmap:

- [CI-01 — Continuous integration](ci_01_continuous_integration.md)

## UI/UX modernization milestones

The `UX-` category modernizes the WPF presentation around Microsoft's Fluent design language and
introduces the Project/Tracker information architecture required for portfolio management. It does
not continue the numbered product roadmap or the `PF-` sequence.

- [UX roadmap and execution order](ux_00_README.md)
- [UX-01 — Projects and Trackers Foundation](ux_01_projects_and_trackers_foundation.md)
- [UX-02 — Fluent Foundation and Theme](ux_02_fluent_foundation.md)
- [UX-03 — Application Shell and Portfolio](ux_03_application_shell_and_portfolio.md)
- [UX-04 — Overview and Entity Details](ux_04_overview_and_entity_details.md)
- [UX-05 — Schema Synchronization Experience](ux_05_schema_synchronization_experience.md)
- [UX-06 — Entity Creation and Editing](ux_06_entity_creation_and_editing.md)
- [UX-07 — Reporting and Tracker Comparison](ux_07_reporting_and_tracker_comparison.md)
- [UX-08 — Accessibility and Consistency](ux_08_accessibility_and_consistency.md)

The previous SharePoint integration direction has been retired. SQLite remains the local catalog and
working cache. Project-level synchronization to a Git repository and folder is a future product
capability whose serialization, merge, authentication, and recovery rules are not part of the UX
roadmap.

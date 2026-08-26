# Database Entity Dependency Tracker — Roadmap

Build a Windows C#/.NET WPF application that imports database relationships from CSV, tracks development per stable entity, computes a dependency-safe implementation order, visualizes progress, and can later replace SQLite with SharePoint.

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
13. Live SharePoint integration

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

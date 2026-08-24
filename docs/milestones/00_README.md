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

## Milestones
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

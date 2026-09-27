# Database Entity Dependency Tracker — Milestone roadmap

Build a Windows C#/.NET WPF application that imports database relationships from CSV, tracks development per stable entity, computes a dependency-safe implementation order, visualizes progress, and supports multiple implementation trackers grouped into projects.

## Principles

- No business logic in the UI.
- Rank is derived and recomputed; it is never entity identity.
- Progress and notes belong to stable entities, never rows.
- Imported schema data and manual overrides remain distinguishable.
- Infrastructure is replaceable through interfaces and dependency injection.
- Every implementation milestone leaves a runnable, testable solution.
- Add classes and abstractions only when they have a concrete responsibility.

## Categories

| Category | Roadmap | Status | Relationship |
| --- | --- | --- | --- |
| Product | [01–12 and intermediate milestones](product/00_README.md) | Completed; former 13 retired | Original product foundation. |
| Product feedback | [PF-01–PF-05](product-feedback/00_README.md) | Planned | Independent workflow improvements on the product foundation. |
| Engineering | [CI-01](engineering/00_README.md) | Completed | Repository engineering independent of the product sequence. |
| UI/UX | [UX-01–UX-08](ux/00_README.md) | Completed | Projects/Trackers and Fluent presentation, building on the product foundation. |
| Git sync | [GS-01–GS-05](git-sync/00_README.md) | GS-01 completed; GS-02–GS-05 planned | Portable snapshots are implemented; Project-level Git collaboration remains planned. |

See the [cross-category milestone status](milestone_status.md) for the recorded state of each series. A planned milestone document describes future work; it does not make the feature available in the current application.

The live SharePoint integration formerly called Milestone 13 is retired. SQLite is currently the only runtime store. GS-01 adds portable snapshots; later GS milestones specify a user-managed Git repository workflow while retaining SQLite as the application's runtime source of truth.

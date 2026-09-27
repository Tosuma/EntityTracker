# Milestone 1 — Solution Skeleton and Domain

## Goal
Create the minimum clean architecture. No real importing, persistence, ranking UI, or reporting yet.

## Suggested projects
- `EntityTracker.Domain`
- `EntityTracker.Application`
- `EntityTracker.Infrastructure`
- `EntityTracker.Reporting`
- `EntityTracker.Wpf`
- Test projects as needed.

## Tasks
- Establish inward dependency direction.
- Create initial entity/table model with stable application identity and source name.
- Model dependency edges.
- Add a small development-status model.
- Reject obvious invalid states such as self-dependency.
- Keep rank out of the entity model.
- Configure dependency injection only at the application composition root.
- Launch a minimal WPF window.

## Acceptance criteria
- Clean checkout builds.
- WPF launches.
- Domain knows nothing about WPF, SQLite, CSV, SharePoint, or charts.
- No god class or generic utility dumping ground.

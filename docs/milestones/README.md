# EntityTracker milestone roadmap

EntityTracker is a Windows C#/.NET WPF application that imports database relationships from CSV,
tracks development per stable entity, computes a dependency-safe implementation order, visualizes
progress, and supports multiple Trackers grouped into Projects.

## Principles

- Keep business logic outside WPF.
- Treat rank as a derived projection, never as entity identity.
- Attach progress and notes to stable entities, never rows.
- Keep imported schema data and manual overrides distinguishable.
- Keep infrastructure replaceable through focused interfaces and dependency injection.
- Leave the solution runnable and testable after every milestone.
- Add abstractions only for concrete responsibilities.

## Milestone groups

- [Core product milestones](core/README.md) — completed product milestones 01–12 and the retired
  SharePoint direction.
- [Product feedback milestones](feedback/README.md) — independently planned PF-01–PF-05 workflow
  improvements.
- [Engineering milestones](engineering/README.md) — independent repository and delivery work.
- [UI/UX modernization](ux/README.md) — completed UX-01–UX-08 Fluent modernization.
- [Remote synchronization](remote-sync/README.md) — planned RS-01–RS-05 Project-level Git
  synchronization.

See [milestone status](milestone_status.md) for the current state of every group.

## Approved storage direction

SQLite remains the working store for SQLite-only Projects and the local projection/cache for a
future Git-backed Project. Git backing is optional per Project. Each linked Project uses one
dedicated, user-selected repository and one managed branch. Repository files are app-owned but
reviewable. Accepted operations create local commits, offline work is allowed, and a user-triggered
Sync will fetch, semantically merge, and push through installed Git.

The approved direction does not yet change the application. Implement RS-01 through RS-05 in
order; do not expose partial remote behavior from a later milestone.

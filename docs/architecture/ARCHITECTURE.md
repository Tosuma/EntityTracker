# Architecture Rules

## Purpose

This document defines the architectural constraints for the Database Entity Dependency Tracker.

These rules should be treated as project-level guardrails by both human developers and coding agents.

The goal is to keep the application maintainable, testable, and replaceable without over-engineering it.

---

## 1. Core architectural principle

The application should be structured so that business logic is independent of:

- WPF
- SQLite
- SharePoint
- CSV libraries
- charting libraries
- file dialogs
- operating-system-specific UI concerns

The UI and infrastructure are replaceable outer layers.

The core application behavior should remain usable even if the persistence or UI technology changes.

---

## 2. Project structure

The initial solution should contain these projects:

```text
EntityTracker.slnx

src/
  EntityTracker.Domain/
  EntityTracker.Application/
  EntityTracker.Infrastructure/
  EntityTracker.Reporting/
  EntityTracker.Wpf/

tests/
  EntityTracker.Domain.Tests/
  EntityTracker.Application.Tests/
  EntityTracker.Infrastructure.Tests/
```

Additional projects should only be introduced when there is a concrete need.

Do not create projects merely to make the architecture appear more sophisticated.

---

## 3. Dependency direction

Dependencies must point inward.

The intended dependency direction is approximately:

```text
EntityTracker.Domain
        ↑
EntityTracker.Application
        ↑
EntityTracker.Infrastructure
        ↑
EntityTracker.Wpf
```

`EntityTracker.Reporting` may depend on Domain/Application models as needed, but core projects must not depend on Reporting.

The WPF project acts as the composition root and may reference the outer implementations required to run the application.

### Rules

`EntityTracker.Domain` must not reference:

- Application
- Infrastructure
- Reporting
- WPF

`EntityTracker.Application` may reference:

- Domain

It must not reference:

- WPF
- SQLite-specific libraries
- SharePoint-specific libraries
- file-dialog libraries
- charting libraries

`EntityTracker.Infrastructure` may reference:

- Domain
- Application

It contains implementations for:

- SQLite
- file export (CSV and Excel)
- filesystem access
- future SharePoint integration

`EntityTracker.Reporting` may contain:

- reporting models
- historical-data aggregation
- chart-data preparation
- export logic

It should not contain workflow/business rules that belong in Domain or Application.

`EntityTracker.Wpf` may reference:

- Application
- Infrastructure
- Reporting
- Domain where appropriate for presentation models

The WPF project should not implement business logic.

---

## 4. UI rules

The WPF project is a presentation layer.

Use MVVM.

ViewModels may:

- expose presentation state
- validate simple UI input
- execute commands
- call application services
- transform application results into display-friendly state

ViewModels must not:

- calculate dependency rankings
- execute SQL directly
- write export files directly
- manage SQLite connections
- decide whether an entity is ready for implementation
- implement Git synchronization or merge rules

Views should contain no meaningful business logic in code-behind.

Small purely visual code-behind is acceptable when WPF makes it significantly simpler.

---

## 5. Domain rules

The Domain project represents concepts that exist independently of storage and UI.

Likely domain concepts include:

- tracked database entity/table
- dependency relationship (always between entity IDs)
- development status
- placeholder entity for a dependency on a name no entity has yet
- status transition
- entity lifecycle state

Only add a domain class when it models a real concept or protects an invariant.

Do not create classes solely to satisfy a pattern.

---

## 6. Stable identity rule

Development tracking must never be associated with a display row or rank.

Each tracked entity must have a stable identity.

For example:

```csharp
public sealed record EntityId(Guid Value);
```

A table's current ranking may change at any time without affecting:

- status
- notes
- history
- manual corrections
- assignment information

Rank is derived data.

---

## 7. Ranking rule

Ranking must be treated as a pure computation.

Conceptually:

```text
Dependency Graph
      ↓
Ranking Algorithm
      ↓
Ordered Result
```

The ranking should be recomputed whenever the effective dependency graph changes.

Do not try to manually move or insert individual ranks.

The default implementation work order must always satisfy this invariant:

> No entity may appear before any entity it depends on.

Topological ordering is therefore the hard constraint.

Additional importance scoring may only influence ordering among entities that are simultaneously eligible.

The Overview's default planning order groups entities by effective due date, then effective priority,
then Rank. Both effective values pass from an entity to everything it depends on, which keeps the
earliest date and the most urgent priority (`PriorityPlanningService`). A dependency's effective due
date and priority are therefore never later or less urgent than those of an entity that needs it, so
the grouping stays dependency-safe. Neither changes Rank.

Presentation-only analytical views are an explicit exception to the visual ordering statement.
For example, a user may temporarily sort the Overview by Status or Work status to inspect a
workflow segment. Such a sort:

- does not change or recompute Rank;
- does not become the implementation work order;
- may visually place an entity before one of its dependencies; and
- must restore the dependency-safe effective-due-date-then-effective-priority-then-rank order when
  cleared.

Dependency safety therefore remains mandatory for computed Rank and the default planning order,
while an explicitly selected analytical display sort is clearly indicated as a temporary view.

---

## 8. Readiness rule

Readiness is derived state.

Do not persist a mutable field such as:

```text
IsReady = true
```

unless it is explicitly treated as a disposable cache.

Instead, calculate readiness from:

- the entity's effective dependencies
- the completion state of those dependencies
- the configured workflow rules

A dependency is not removed from the graph merely because it is completed.

The relationship remains true; only the blocker state changes.

---

## 9. Dependencies refer to entities by ID

An entity's dependencies are one list, and each dependency refers to the entity it depends on by
its ID, never by its name. A name is a display value that can change; a rename must not break or
move a dependency.

A dependency on a name no entity has yet is a dependency on a placeholder entity
(`EntityLifecycleState.Placeholder`) with that name:

- a placeholder is not shown as an entity; it is resolved as an unresolved reference to its name;
- adding an entity with the name turns the placeholder into that entity, keeping its ID, so every
  dependency on it connects;
- renaming an entity to a placeholder's name moves the placeholder's dependents to the entity;
- a placeholder nothing depends on is removed;
- permanently deleting an entity others depend on turns it back into a placeholder;
- a placeholder's ID is derived from its Tracker and name, so independent collaborators create the
  same placeholder, and a Git merge folds a placeholder into an entity with the same name.

The effective dependency graph is the dependencies of the active entities, resolved against the
active entities: a dependency on an archived entity or a placeholder is unresolved.

---

## 10. Persistence abstraction

The application should not know whether data is stored in:

- SQLite
- SharePoint
- another SQL database
- another future backend

Introduce persistence interfaces when persistence is actually implemented.

Example:

```csharp
public interface IEntityRepository
{
    Task<TrackedEntity?> GetAsync(
        EntityId id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrackedEntity>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        TrackedEntity entity,
        CancellationToken cancellationToken = default);
}
```

Do not prematurely create generic repository frameworks.

### Database normal form

The SQLite schema is in Boyce-Codd normal form (and so also in 4NF and 5NF, as no table holds
independent multi-valued facts). Keep it there:

- **No derived columns.** A column must not be computable from other columns of its row. Names are
  compared with the `ENTITY_NAME` collation (trimmed, culture-invariant upper case, ordinal — the
  app's `EntitySourceKey` rule), so `UNIQUE (tracker_id, source_name)` enforces the rule without a
  separate key column. A Project or Tracker is recycled exactly when `recycled_at_utc` is set.
- **One record of each fact.** An entity's development status is its newest status event
  (view `entity_current_status`); changing the status appends an event. An event's previous status
  is the new status of the event it follows (`previous_event_id`).
- **Identity by ID.** Every table is keyed by its GUID; status events and progress snapshots keep
  their recording order in an explicit `sequence`. Dependencies refer to entities by ID.
- **Atomic values.** The Tracker sync baseline is stored as rows, not a JSON document.
- **Deliberate exception.** `trackers.copied_from_tracker_id` is not a foreign key: it can name a
  Tracker in another Project that is not on this machine.

The Git snapshot is a document format, not a database: it may carry values the database derives
(an entity's status, an event's previous status, the baseline as JSON). The snapshot store converts
between the two, and `ProjectSnapshotValidator` requires such values to agree with what they derive
from.

Prefer interfaces shaped around actual application needs.

---

## 11. Dependency injection

Use .NET dependency injection at the composition root.

The WPF startup project should configure concrete implementations.

Conceptually:

```csharp
services.AddSingleton<IEntityRepository, SqliteEntityRepository>();
services.AddSingleton<IDependencyRankingService, DependencyRankingService>();
```

Domain code should never call the DI container.

Avoid service-locator patterns.

---

## 12. Error handling

Expected failures should be represented as application results or diagnostics where practical.

Examples:

- dependency cycle
- unknown dependency
- sync conflict
- missing file

Exceptions are appropriate for unexpected failures.

Do not use exceptions for ordinary validation flow.

---

## 13. Testing expectations

Business logic should be testable without:

- starting WPF
- creating a real database
- connecting to SharePoint

High-value tests include:

- graph construction
- topological ordering
- ranking tie-breaking
- cycle detection
- readiness calculation
- placeholder behavior (adding, renaming, purging, and merging)
- persistence integration tests

Prefer behavior-focused tests over testing implementation details.

---

## 14. Avoid premature abstraction

Do not introduce:

- generic repository frameworks
- event buses
- mediator libraries
- plugin systems
- custom dependency-injection frameworks
- distributed architecture
- microservices

unless a later requirement genuinely demands them.

The application is a desktop project-management tool, not a banking platform.

Keep it boring where boring is sufficient.

---

## 15. Agent behavior

Coding agents working on this repository should:

1. Read this file before making architectural changes.
2. Read the active milestone completely.
3. Implement only the active milestone unless a tiny prerequisite is unavoidable.
4. Preserve project dependency direction.
5. Prefer existing abstractions over introducing parallel ones.
6. Add tests for meaningful business behavior.
7. Avoid speculative infrastructure for future milestones.
8. Leave the repository compiling and tests passing.
9. Document any architectural decision that materially changes these rules.
10. Ask for human review rather than silently changing a core architectural rule.

---

## 16. Definition of done for every milestone

A milestone is not complete unless:

```text
dotnet build
```

succeeds and:

```text
dotnet test
```

passes.

The application should remain runnable at the end of every milestone.

No milestone should leave deliberately broken intermediate architecture.

---

## 17. Provider startup and storage

The WPF composition root composes SQLite. Application exposes the small
`IPersistenceInitializer` startup seam so the UI does not initialize `SqliteDatabase` directly.
Existing persistence interfaces remain focused on real read and atomic store operations; do not
replace them with a generic repository framework.

SQLite is the only supported provider. SharePoint configuration and provider selection were
retired in UX-08. Settings versions 1–3 remain readable so appearance and active Project/Tracker
context survive; the next settings save writes version 5 without retired provider fields.
Backend-neutral collaborative conflict value types are currently inactive historical seams, as
described in [COLLABORATIVE_STORAGE.md](COLLABORATIVE_STORAGE.md).

The [GS milestone roadmap](../milestones/git-sync/00_README.md) records portable snapshots,
manual collaboration, and automatic synchronization through user-managed existing working trees.
The Application scheduler starts after the UI is usable, runs one linked Project at a time, and
uses cancellation and a per-Project result state. Infrastructure bounds repository paths, files,
Git output, and operation time. Git work remains outside synchronous SQLite and navigation paths;
SQLite remains the runtime store for ordinary operations.

Local backup, logging, retention, and restore procedures are defined in
[RECOVERY.md](../operations/RECOVERY.md).

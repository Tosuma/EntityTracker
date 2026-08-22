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
- CSV reading
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
- parse CSV rows directly
- manage SQLite connections
- decide whether an entity is ready for implementation
- implement import synchronization rules

Views should contain no meaningful business logic in code-behind.

Small purely visual code-behind is acceptable when WPF makes it significantly simpler.

---

## 5. Domain rules

The Domain project represents concepts that exist independently of storage and UI.

Likely domain concepts include:

- tracked database entity/table
- dependency relationship
- development status
- manual dependency override
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

The following invariant must always hold:

> No entity may appear before any entity it depends on.

Topological ordering is therefore the hard constraint.

Additional importance scoring may only influence ordering among entities that are simultaneously eligible.

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

## 9. Imported data vs manual overrides

Database exports are not guaranteed to contain every real dependency.

Therefore imported facts and manual corrections must remain distinguishable.

Conceptually:

```text
Imported Dependencies
        +
Manual Overrides
        =
Effective Dependency Graph
```

A future CSV import must not silently overwrite valid manual corrections.

Manual corrections may include:

- adding a missing dependency
- suppressing an incorrect imported dependency
- correcting metadata
- adjusting display information

The exact override model should be introduced only when that milestone is implemented.

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

## 12. CSV import rules

CSV is an external input format.

The CSV parser must:

- validate required columns
- return structured diagnostics
- avoid silently guessing malformed input
- remain independent of WPF
- remain independent of SQLite

The import process should eventually support:

```text
CSV
 ↓
Parse
 ↓
Validate
 ↓
Compare with current state
 ↓
Review changes
 ↓
Commit
```

The SQL-query helper is not part of this pipeline.

It is a separate convenience feature.

---

## 13. Error handling

Expected failures should be represented as application results or diagnostics where practical.

Examples:

- invalid CSV
- dependency cycle
- unknown dependency
- import conflict
- missing file

Exceptions are appropriate for unexpected failures.

Do not use exceptions for ordinary validation flow.

---

## 14. Testing expectations

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
- schema diffing
- manual override behavior
- persistence integration tests

Prefer behavior-focused tests over testing implementation details.

---

## 15. Avoid premature abstraction

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

## 16. Agent behavior

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

## 17. Definition of done for every milestone

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

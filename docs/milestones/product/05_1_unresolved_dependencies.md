# Milestone 5.1 — Unresolved Dependencies and Blocked Entities

## Context

Milestones 1–5 are complete.

During realistic CSV testing, an important product requirement became clear:

A valid entity must still be importable even when one or more of its declared dependencies do not currently exist in the known entity set.

For example:

```text
warehouse_assignment -> facility
```

If `warehouse_assignment` is a valid entity but `facility` is not currently known, the entity must **not** be rejected simply because the dependency cannot be resolved.

Instead, the dependency must be retained as unresolved and the entity must be treated as blocked until the dependency is resolved.

This milestone corrects the behavior before Milestone 6 introduces full schema synchronization and review.

---

# Objective

Support unresolved dependency references throughout parsing, graph analysis, persistence, and the existing WPF overview.

After this milestone:

- valid entities with missing dependencies can still be imported/previewed;
- missing dependencies are preserved rather than discarded;
- affected entities are visibly marked as blocked/unresolved;
- unresolved entities do not receive a misleading normal dependency-safe rank;
- unresolved dependency information survives application restart where persistence is involved;
- existing behavior for fully resolvable graphs continues to work.

Milestone 6 remains responsible for the full compare/review/commit synchronization workflow.

---

# Core business rule

A missing dependency is **not** the same thing as an invalid entity.

The intended behavior is:

```text
Valid entity + known dependencies
    -> normal import
    -> normal graph participation

Valid entity + one or more unknown dependencies
    -> import/retain entity
    -> retain unresolved dependency names
    -> mark entity as blocked/unresolved
    -> exclude it from normal dependency-safe ranking

Malformed entity row
    -> validation error
    -> row may be rejected

Dependency cycle among resolved entities
    -> graph validation error
```

Do not silently drop unknown dependencies.

Do not fabricate placeholder entities unless explicitly required by a later milestone.

---

# Terminology

Use a clear distinction between:

## Development Status

Examples:

```text
NotStarted
InProgress
Completed
```

This describes implementation progress.

## Dependency / Readiness State

Examples:

```text
Ready
Blocked
Unresolved
```

This describes whether implementation can safely proceed.

A missing dependency must **not** be represented as a development status.

An entity may therefore be:

```text
Development status: NotStarted
Dependency state: Unresolved
```

---

# Required implementation

## 1. CSV parsing and validation

Update the CSV import behavior so an unknown dependency reference is a non-fatal diagnostic.

Given:

```text
table_name = warehouse_assignment
dependency = facility
```

when `facility` is absent from the imported/known entity set:

- preserve the dependency name `facility`;
- return the entity as part of the parsed/import candidate result;
- report the unresolved dependency in structured diagnostics;
- do not reject the complete entity solely for that reason.

The parser must still reject or diagnose genuinely malformed input according to existing Milestone 2 rules.

### Expected diagnostic severity

Unknown dependency references should normally be treated as a warning/unresolved-reference diagnostic rather than a fatal parse error.

Use the project's existing result/diagnostic model where possible rather than building a parallel validation framework.

---

## 2. Domain/application representation

Introduce the smallest clean model needed to represent an unresolved dependency.

The exact type is up to the implementation, but conceptually the application needs to distinguish:

```text
Resolved dependency
    -> references a known EntityId

Unresolved dependency
    -> retains the source dependency name
```

Possible conceptual model:

```csharp
public abstract record DependencyReference;

public sealed record ResolvedDependencyReference(
    EntityId EntityId) : DependencyReference;

public sealed record UnresolvedDependencyReference(
    string SourceName) : DependencyReference;
```

This is an example only.

Prefer a model that fits the existing codebase.

Do not perform a broad architectural rewrite solely to match this example.

---

## 3. Persistence

Persist unresolved dependency references where the current application persists imported/tracked dependency information.

A restart must not make unresolved dependencies disappear.

The persisted representation must retain enough information to allow a future import to resolve:

```text
"facility"
```

to the actual `facility` entity once it exists.

If a schema migration is required:

- make it explicit;
- preserve existing Milestone 4 data;
- add migration/integration tests where appropriate.

Do not implement SharePoint support.

---

## 4. Effective graph construction

The graph layer must distinguish between:

- resolved edges;
- unresolved dependency references.

Only resolved dependencies can form actual graph edges.

For an entity with unresolved dependencies:

```text
warehouse_assignment
    -> unresolved: facility
```

the ranking engine must know that the entity's dependency information is incomplete.

Do not simply omit `facility` and then treat `warehouse_assignment` as dependency-free.

---

## 5. Ranking behavior

The topological invariant remains unchanged:

> No ranked entity may appear before any entity it depends on.

However, an entity with one or more unresolved dependencies cannot be assigned a trustworthy normal dependency-safe rank.

Therefore:

- resolved portions of the graph should continue to rank normally;
- entities with unresolved dependencies should be excluded from the normal ranked sequence;
- unresolved entities should be returned separately or clearly flagged as unranked;
- no fake "last rank" should be assigned merely to make the UI simpler.

Example:

```text
A
C -> A
B -> Missing X
```

Expected conceptual output:

```text
Ranked:
1. A
2. C

Unresolved:
- B (missing X)
```

If the existing ranking result shape needs to evolve, make the smallest change required.

---

## 6. Transitive unresolved blocking

An entity must also be considered unsafe to implement if it depends on an entity whose own dependency chain is unresolved.

Example:

```text
A -> Missing X
B -> A
```

`A` is directly unresolved.

`B` is not directly missing a named dependency, but its implementation chain is still blocked because `A` cannot yet be safely placed.

The implementation should represent this distinction if practical:

```text
A
  Direct unresolved dependency: X

B
  Blocked by unresolved dependency chain through A
```

At minimum, `B` must not be presented as safely ready.

Do not over-engineer the UI for transitive explanation if the existing architecture does not yet support detailed blocker paths, but the underlying behavior must be correct.

---

## 7. Existing WPF overview

Extend the Milestone 5 overview to make unresolved state visible.

The UI should make it obvious that:

- the entity was successfully recognized;
- it is not currently rankable/ready;
- one or more dependencies are missing.

A reasonable presentation could include:

```text
Entity                  Rank     Status       Dependency State
----------------------------------------------------------------
Customer                1        Completed    Ready
Order                    2        NotStarted   Blocked
WarehouseAssignment     —        NotStarted   Unresolved
```

For an unresolved entity, provide a way to see the missing names, for example:

```text
Missing dependencies:
- facility
- legal_entity
```

This may be:

- a column;
- tooltip;
- expandable details;
- details panel;
- simple message below the selected row.

Choose the simplest approach that fits the current UI.

Do not build the full Milestone 7 entity editor.

---

## 8. Import preview behavior

Milestone 5 remains preview-oriented.

This milestone does **not** move the full Milestone 6 schema synchronization workflow earlier.

When previewing a CSV:

- show entities with unresolved dependencies rather than rejecting them;
- show the unresolved dependency warnings clearly;
- do not treat those warnings as a reason to discard the otherwise valid entity;
- continue to respect the current Milestone 5 save/preview behavior.

If the current implementation already persists some information through an earlier workflow, adapt consistently, but do not implement the Milestone 6 compare/review/apply workflow here.

---

# Automatic future resolution

The data model created in this milestone must make later automatic resolution possible.

If a future import contains:

```text
facility
```

and an existing unresolved dependency is:

```text
"facility"
```

the system should eventually be able to resolve that reference to the new entity.

**Do not implement the full future-import resolution workflow in this milestone unless a very small piece is naturally required by the current architecture.**

That behavior belongs primarily in Milestone 6.

This milestone's responsibility is to ensure the unresolved reference survives with sufficient identity information for that later resolution.

---

# Error behavior

Use this distinction:

## Non-fatal

```text
Unknown dependency reference
```

Result:

- preserve entity;
- preserve missing dependency name;
- diagnostic/warning;
- unresolved/blocked state.

## Fatal or invalid input

Examples:

```text
missing table/entity name
malformed required CSV field
invalid CSV structure that cannot be interpreted
```

Follow existing parser rules.

## Graph-invalid

Example:

```text
A -> B
B -> A
```

This remains a cycle and should use the existing cycle-detection behavior.

Do not confuse a missing node with a cycle.

---

# Tests

Add or update tests covering at least the following.

## CSV/import tests

### One missing dependency

Input:

```text
A -> MissingX
```

Expected:

- A is returned as a valid entity/import candidate;
- `MissingX` is retained;
- diagnostic indicates unresolved dependency;
- import does not fail solely because `MissingX` is absent.

### Multiple missing dependencies

Input:

```text
A -> MissingX, MissingY
```

Both references are retained.

### Mixed known and unknown dependencies

Input:

```text
A -> B, MissingX
```

Expected:

- resolved edge to B exists;
- unresolved reference to MissingX exists;
- A remains unresolved overall.

---

## Ranking tests

### Resolved graph unaffected

Input:

```text
A
B -> A
C -> B
```

Existing ranking behavior remains correct.

### Unresolved entity excluded from normal ranking

Input:

```text
A
B -> MissingX
C -> A
```

Expected:

```text
Ranked:
A
C

Unresolved:
B
```

Exact order should follow the established ranking rules.

### Known + unknown dependency

```text
A
B -> A, MissingX
```

B must not receive a normal rank.

### Transitive unresolved blocking

```text
A -> MissingX
B -> A
```

B must not be considered ready/safely rankable as though A were normal.

---

## Persistence tests

Persist:

```text
A -> MissingX
```

Reload the application/store.

Expected:

- A still exists;
- unresolved dependency `MissingX` still exists;
- unresolved state can be reconstructed.

---

## Regression tests

All existing tests from Milestones 1–5 must continue passing unless they encoded the old behavior that unknown dependencies are fatal.

If an old test asserts that an unknown dependency rejects the entity, update that test to reflect this milestone's new business rule.

Do not delete useful validation tests merely to make the suite pass.

---

# Acceptance criteria

Milestone 5.1 is complete when all of the following are true:

- [ ] A valid entity is not rejected solely because a dependency is unknown.
- [ ] Unknown dependency names are retained.
- [ ] Unknown dependencies produce structured non-fatal diagnostics.
- [ ] Missing dependencies are never silently discarded.
- [ ] An entity with unresolved dependencies is clearly represented as unresolved/blocked.
- [ ] Unresolved entities do not receive a misleading normal dependency-safe rank.
- [ ] Resolved parts of the graph continue to rank normally.
- [ ] An entity transitively dependent on an unresolved chain is not presented as safely ready.
- [ ] Unresolved dependency references survive persistence/restart where applicable.
- [ ] The WPF overview visibly distinguishes unresolved entities.
- [ ] The user can see which dependency names are missing.
- [ ] Fully valid CSV imports continue to behave as they did after Milestone 5.
- [ ] Existing cycle detection still works.
- [ ] `dotnet build` succeeds.
- [ ] `dotnet test` succeeds.
- [ ] Milestone 6 functionality has not been implemented prematurely.

---

# Out of scope

Do **not** implement the following as part of Milestone 5.1:

- full CSV-to-current-state diffing;
- new/changed/removed entity categorization;
- rename detection;
- transactional schema synchronization review;
- `Apply Changes` workflow from Milestone 6;
- manual dependency editing from Milestone 7;
- full readiness workflow/filtering from Milestone 8;
- charts/reporting;
- SharePoint;
- placeholder entity creation for missing dependencies;
- automatic deletion of unresolved relationships.

---

# Architectural guidance

Prefer extending existing models and services over introducing a parallel "missing dependency subsystem."

The desired conceptual flow is:

```text
CSV
 ↓
Parse entities and dependency names
 ↓
Resolve names that exist
 ↓
Retain names that do not exist
 ↓
Build resolved graph + unresolved metadata
 ↓
Rank safe/resolved portion
 ↓
Display ranked + unresolved entities
```

Do not allow the UI to perform dependency resolution or graph analysis.

Do not allow Infrastructure-specific types to leak into Domain/Application.

Avoid broad refactoring unless the current implementation genuinely cannot support the requirement cleanly.
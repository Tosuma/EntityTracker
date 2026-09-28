# Milestone 6 — Safe Schema Synchronization and Import Review

## Context

Milestones 1–5.1 are complete.

The application can now:

- parse database schema exports from CSV;
- represent entities and dependencies;
- preserve unresolved dependency references;
- compute dependency-safe rankings for resolvable entities;
- persist tracked entities and progress;
- display the current state in WPF.

Milestone 6 introduces the first **real synchronization workflow** between an imported CSV and the application's persisted tracked state.

The import must not simply overwrite existing data.

Instead, the application must compare the incoming schema against persisted state, show the user only meaningful changes, and apply approved changes safely.

---

# Goal

Update the tracked schema without destroying manual work, progress, notes, history, or previously tracked identity.

The user should be able to:

1. choose a CSV;
2. choose whether the import is **Complete** or **Partial**;
3. compare the import against persisted state;
4. review only meaningful differences;
5. understand exactly what changed;
6. cancel without modifying persistent state;
7. apply approved changes transactionally;
8. receive a newly recomputed dependency graph and ranking.

---

# Import modes

Every synchronization import must have an explicit import mode.

## Complete import

**Complete is the default mode.**

A complete import means:

> This CSV represents the complete current database schema.

When an entity exists in persisted state but is absent from the imported CSV:

- it is considered **missing / possibly removed**;
- it must appear in the review as an actionable change;
- it should be eligible for soft-archiving when the import is applied;
- its historical data, notes, and progress must not be physically deleted.

Example:

```text
Persisted:
A
B
C

Complete CSV:
A
B
D

Review:
+ D     New
- C     Missing / possibly removed
```

---

## Partial import

The user must actively select **Partial**.

A partial import means:

> This CSV contains only part of the database schema and must not be treated as authoritative for entities that are absent from it.

When an entity exists in persisted state but is absent from a partial import:

- leave it untouched;
- do not classify it as removed;
- do not archive it;
- do not show it as an actionable difference merely because it is absent.

Example:

```text
Persisted:
A
B
C

Partial CSV:
A
D

Review:
+ D     New

B and C are untouched.
```

---

# Import mode UI

Before comparison/review, the import workflow should clearly expose the mode.

A reasonable UX is:

```text
Import CSV

Import type:

(*) Complete database snapshot
    The CSV represents the entire current schema.
    Existing entities absent from the CSV may be archived.

( ) Partial database snapshot
    Only entities contained in the CSV will be reconciled.
    Existing entities absent from the CSV are untouched.

[Cancel] [Continue]
```

Complete must be selected by default.

The user should have to actively choose Partial.

---

# Core synchronization model

Milestone 6 owns comparison between:

```text
Imported schema
      +
Persisted tracked state
      ↓
Reconciliation
      ↓
Candidate post-synchronization state
      ↓
Dependency resolution
      ↓
Review
      ↓
Transactional commit
```

Do not directly mutate persisted state while parsing or diffing.

The synchronization process should first construct a candidate result that represents what the tracked state **would become** if the user applies the import.

---

# Candidate post-synchronization state

Dependency resolution during synchronization must be performed against the **candidate post-synchronization entity set**.

Do not resolve dependencies only against:

- the CSV in isolation; or
- persisted state in isolation.

Example:

```text
Persisted:
facility
warehouse_assignment

Complete CSV:
warehouse_assignment -> facility
```

Because `facility` is absent from a complete import, it is a candidate for removal/archive.

Therefore `facility` must not automatically satisfy the dependency merely because it existed before synchronization.

The final candidate state determines whether that dependency remains resolved.

Likewise, if a new entity appears in the CSV:

```text
facility
```

then unresolved references to `facility` may become resolvable in the candidate post-sync state.

---

# Diff categories

The synchronization diff must classify entities into meaningful categories.

At minimum:

- New
- Changed
- Missing / possibly removed
- Unchanged
- Unresolved / conflict where applicable
- Possible rename candidate, if rename detection is implemented cleanly

---

# New entities

An entity is **New** when:

- it is present in the import;
- no existing tracked entity is matched to it.

Example:

```text
+ InvoiceArchive
```

Show relevant imported metadata and dependencies.

---

# Changed entities

An entity is **Changed** when it already exists but imported schema information differs.

Dependency differences must be shown explicitly.

Prefer:

```text
Customer

Dependencies:
  + CustomerType
  + Country
  - LegacyCustomerGroup
```

over:

```text
Old dependencies:
...

New dependencies:
...
```

The review should answer:

> What changed?

not force the user to manually compare two large lists.

Other schema changes may also be displayed when relevant.

---

# Missing / possibly removed entities

This category applies only to **Complete imports**.

An entity is missing / possibly removed when:

- it exists in persisted state;
- it is absent from the complete import.

These entities must not be physically deleted.

Applying the import should normally soft-archive/deactivate them.

Their:

- stable identity;
- development history;
- notes;
- progress;
- historical reporting data

must remain intact.

---

# Unchanged entities

Entities that are identical between import and persisted state must **not clutter the normal review**.

The default review should not dump every existing entity.

Instead show something like:

```text
✓ 138 unchanged entities
```

Optionally provide:

```text
[Show unchanged]
```

for debugging or reassurance.

Unchanged entities should be hidden/collapsed by default.

---

# Review only actionable changes

The primary import review must show only meaningful differences.

Example:

```text
IMPORT REVIEW

Import mode: Complete

New entities (2)
----------------
+ InvoiceArchive
+ BillingPeriod

Changed entities (2)
--------------------
~ Customer
    Dependencies:
      + CustomerType
      - LegacyCustomerGroup

~ Order
    Dependencies:
      + Currency

Missing / possibly removed (1)
-------------------------------
- LegacyInvoice

Unresolved dependencies (1)
----------------------------
! WarehouseAssignment
    Missing:
      - facility

Unchanged
---------
✓ 138 entities unchanged

[Show unchanged]

[Cancel] [Apply Changes]
```

This is preferable to presenting all 144 entities in one undifferentiated table.

---

# Partial import review behavior

For a partial import, review only entities contained in or directly affected by the imported subset.

Example:

```text
Persisted:
A
B
C

Partial CSV:
A
D
```

If `A` is unchanged:

```text
New:
+ D

Unchanged:
✓ A

B and C are not part of the diff because their absence has no meaning in Partial mode.
```

Do not show:

```text
- B
- C
```

for a partial import.

---

# Dependency resolution

Milestone 5.1 introduced unresolved dependencies.

Milestone 6 must integrate that behavior into synchronization.

For each dependency name in the candidate post-sync state:

## Resolved

If the target exists in the candidate entity set:

```text
Order -> Customer
```

resolve it to the target entity.

## Unresolved

If no target exists in the candidate entity set:

```text
Order -> MissingCustomerType
```

retain the dependency by name and classify it as unresolved.

Do not reject the otherwise valid entity.

Do not silently discard the dependency.

Do not fabricate a placeholder entity.

---

# Resolution of previously unresolved dependencies

Milestone 6 should resolve previously unresolved dependencies when a matching entity becomes available through synchronization.

Example persisted state:

```text
WarehouseAssignment
  unresolved -> facility
```

New import introduces:

```text
facility
```

After reconciliation, the candidate state contains `facility`.

The relationship should become:

```text
WarehouseAssignment
  resolved -> facility
```

without requiring manual re-entry.

Use the project's established entity-name matching/normalization rules.

---

# Complete import and unresolved references

Be careful when a complete import removes a previously known dependency target.

Example persisted state:

```text
facility
WarehouseAssignment -> facility
```

Complete CSV:

```text
WarehouseAssignment -> facility
```

Since `facility` is absent from the complete snapshot:

```text
facility
    -> Missing / possibly removed
```

If the user applies the import and `facility` is archived from the active candidate schema, then:

```text
WarehouseAssignment -> facility
```

must become unresolved in the effective active graph.

Do not keep it falsely resolved merely because the historical entity record still exists in persistence.

Historical identity and active schema membership are different concepts.

---

# Existing progress and identity

Schema synchronization must not recreate unchanged tracked entities unnecessarily.

Existing stable identity must be preserved.

For an existing entity:

```text
Customer
```

a dependency change must not create:

```text
Customer v2
```

or otherwise detach:

- progress;
- status;
- notes;
- history.

The tracked entity remains the same logical entity unless the user explicitly confirms a rename/new-entity interpretation.

---

# Rename handling

Possible rename detection may be supported if it can be implemented conservatively.

A rename must never be silently assumed based only on weak similarity.

For example:

```text
customer_old
```

disappears and:

```text
customer
```

appears.

The application may present:

```text
Possible rename:
customer_old -> customer
```

but the user should confirm it.

If reliable rename support would substantially complicate this milestone, it is acceptable to surface the pair as:

```text
New: customer
Missing: customer_old
```

rather than inventing unsafe matching logic.

Correctness is more important than clever rename guessing.

---

# Import review editing

The review may allow corrections to **new or changed import candidates before commit**.

Keep this scoped to correcting the import candidate.

Examples:

- correcting an entity name;
- correcting an imported dependency name;
- resolving an obvious import mistake.

Do not turn this into the general entity-editing UI planned for later milestones.

If the distinction becomes unclear, prefer keeping Milestone 6 editing minimal.

---

# Manual entity creation

Manual creation of entirely new tracked entities is explicitly **out of scope** for Milestone 6.

That functionality belongs to:

```text
Milestone 6.1 — Manual Entity Creation
```

Milestone 6 must not add an `Add Entity` workflow outside synchronization.

Milestone 6.1 will handle:

- manually entering an entity name;
- searching existing entities while entering dependencies;
- selecting resolved dependencies;
- entering unknown dependency names;
- preserving those names as unresolved dependencies.

Do not implement that functionality here.

---

# Transactional commit

No persistent changes should occur until the user chooses:

```text
Apply Changes
```

Applying the synchronization must be transactional where practical.

Either:

```text
all approved schema changes succeed
```

or:

```text
the previous persisted state remains intact
```

Avoid partially applied synchronization.

After successful commit:

1. persist new entities;
2. update changed schema information;
3. archive entities removed by a Complete import;
4. retain entities absent from a Partial import;
5. update resolved/unresolved dependency references;
6. preserve progress, notes, history, and stable identity;
7. recompute the effective dependency graph;
8. recompute ranking;
9. refresh the WPF overview.

---

# Cancel behavior

Canceling the import review must leave persisted state unchanged.

This includes:

- entities;
- dependencies;
- statuses;
- notes;
- archive state;
- unresolved references;
- history.

The candidate synchronization state may simply be discarded.

---

# Preview / review safety

The user should be able to inspect the complete diff before applying it.

The review should make dangerous changes especially obvious.

Examples:

```text
Complete import:
17 entities will be archived
```

should be visually more prominent than:

```text
2 dependency changes
```

If an unusually large removal is detected, consider a stronger confirmation before commit.

Do not introduce excessive confirmation dialogs for ordinary small imports.

---

# Ranking after synchronization

Rank remains derived data.

Do not persist the imported or previous display rank as authoritative state.

After successful synchronization:

```text
Candidate/committed effective graph
            ↓
Ranking engine
            ↓
Fresh ranked result
```

Entities with unresolved dependencies continue to follow Milestone 5.1 behavior.

Do not assign misleading normal ranks to unresolved entities.

---

# Tests

Add or update tests covering at least the following.

## Complete import — unchanged

Persisted:

```text
A
B -> A
```

Complete import:

```text
A
B -> A
```

Expected:

- no actionable changes;
- unchanged count = 2;
- import can be treated as a no-op.

---

## Complete import — new entity

Persisted:

```text
A
```

Complete import:

```text
A
B
```

Expected:

```text
New:
B
```

---

## Complete import — removed entity

Persisted:

```text
A
B
```

Complete import:

```text
A
```

Expected:

```text
Missing / possibly removed:
B
```

Applying the import soft-archives B.

---

## Partial import — absent persisted entity

Persisted:

```text
A
B
C
```

Partial import:

```text
A
```

Expected:

- B untouched;
- C untouched;
- B and C are not categorized as missing/removed.

---

## Dependency addition

Persisted:

```text
Order -> Customer
```

Import:

```text
Order -> Customer, Currency
```

Expected review:

```text
Order
  Dependencies:
    + Currency
```

---

## Dependency removal

Persisted:

```text
Order -> Customer, LegacyType
```

Import:

```text
Order -> Customer
```

Expected review:

```text
Order
  Dependencies:
    - LegacyType
```

---

## Mixed dependency changes

Persisted:

```text
Order -> Customer, LegacyType
```

Import:

```text
Order -> Customer, Currency
```

Expected:

```text
Order
  Dependencies:
    + Currency
    - LegacyType
```

---

## Previously unresolved dependency becomes resolved

Persisted:

```text
A -> MissingX
```

Import introduces:

```text
MissingX
```

Expected:

- candidate post-sync state contains MissingX;
- A's dependency resolves automatically;
- ranking is recomputed accordingly after commit.

---

## Complete import removes dependency target

Persisted:

```text
A
B -> A
```

Complete import:

```text
B -> A
```

Expected:

- A is missing / possibly removed;
- B's dependency must not remain falsely resolved in the candidate active schema;
- B becomes unresolved if A will no longer be active after applying the synchronization.

---

## Partial import preserves external persisted dependency

Persisted:

```text
A
B -> A
```

Partial import includes only:

```text
B -> A
```

Expected:

- A remains active because absence from Partial has no removal meaning;
- B -> A remains resolved.

---

## Cancel

Produce a diff containing changes.

Cancel.

Reload persisted state.

Expected:

- no state change.

---

## Transaction failure

Where practical, simulate persistence failure during apply.

Expected:

- previous state remains intact;
- no partially synchronized state.

---

## Progress preservation

Persisted:

```text
Customer
Status: InProgress
Notes: "Manager implemented"
```

Import changes Customer's dependencies.

After apply:

- same stable entity identity;
- status remains InProgress;
- notes remain unchanged;
- dependency metadata is updated.

---

# Acceptance criteria

Milestone 6 is complete when:

- [ ] CSV import can be explicitly performed as Complete or Partial.
- [ ] Complete is the default import mode.
- [ ] Partial requires active user selection.
- [ ] Imported schema is diffed against persisted schema.
- [ ] The primary review shows actionable changes rather than every entity.
- [ ] Unchanged entities are hidden/collapsed by default.
- [ ] The review reports the number of unchanged entities.
- [ ] New entities are clearly identified.
- [ ] Existing changed entities are clearly identified as existing-but-changed.
- [ ] Dependency additions are displayed explicitly.
- [ ] Dependency removals are displayed explicitly.
- [ ] Complete imports identify persisted entities absent from the CSV as missing / possibly removed.
- [ ] Partial imports leave persisted entities absent from the CSV untouched.
- [ ] Candidate post-sync state is used for dependency resolution.
- [ ] Previously unresolved dependencies resolve automatically when their target becomes available in the candidate state.
- [ ] Dependencies whose target disappears from an applied Complete import do not remain falsely resolved.
- [ ] Unknown dependency references remain non-fatal and preserved as unresolved.
- [ ] Applying a Complete import soft-archives missing entities rather than deleting historical data.
- [ ] Existing status, notes, history, and stable identity survive synchronization.
- [ ] Canceling the review changes no persistent state.
- [ ] Applying synchronization is transactional where practical.
- [ ] Ranking is recomputed after successful synchronization.
- [ ] Identical complete re-import is effectively a no-op.
- [ ] Five genuinely new tables create five new tracked entities.
- [ ] Existing progress stays attached after ranks shift.
- [ ] Manual entity creation has not been implemented.
- [ ] Milestone 7 general editing functionality has not been implemented prematurely.
- [ ] `dotnet build` succeeds.
- [ ] `dotnet test` succeeds.

---

# Out of scope

Do not implement:

- manual entity creation;
- searchable manual dependency entry;
- free-text manual dependency creation;
- general entity editing;
- full manual override management;
- charts;
- reporting export;
- SharePoint;
- arbitrary placeholder entities for unresolved dependency names;
- automatic destructive deletion of missing entities.

Manual entity creation belongs to Milestone 6.1.

General editing and manual dependency overrides remain later functionality.

---

# Architectural guidance

Prefer a synchronization result that is explicit and reviewable.

Conceptually:

```text
ImportRequest
  - CSV candidate
  - ImportMode

CurrentPersistedState
        ↓

SynchronizationPlanner / Diff Service
        ↓

SynchronizationPlan
  - New entities
  - Changed entities
  - Missing entities
  - Unchanged count/entities
  - Dependency additions/removals
  - Unresolved references
  - Candidate post-sync state
        ↓

Review UI
        ↓

Apply service
        ↓

Transactional persistence
        ↓

Recompute graph/ranking
```

Names are examples, not mandatory class names.

Use existing architecture where possible.

Do not introduce a large generic synchronization framework merely because other backends may exist later.

Keep:

- comparison/business rules out of WPF;
- SQLite-specific implementation details in Infrastructure;
- graph/ranking logic independent of persistence;
- synchronization behavior testable without launching WPF.


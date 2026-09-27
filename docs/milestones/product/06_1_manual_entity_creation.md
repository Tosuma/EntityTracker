# Milestone 6.1 — Manual Entity Creation

## Context

Milestones 1–6 are complete.

The application can now import and synchronize schema information from CSV using Complete or Partial import modes.

However, the tracked schema must not depend entirely on CSV exports.

Users must also be able to manually create a new tracked entity when:

- a table has not yet appeared in the database export;
- database metadata is incomplete;
- an entity is known to the development team before it exists physically;
- a dependency target needs to be represented before a future import includes it.

This milestone adds **manual creation of new entities**.

It does not introduce general editing of existing entities. That remains Milestone 7.

---

# Goal

Allow a user to manually create a new tracked entity from the WPF application.

The user must be able to:

1. enter an entity name;
2. add zero or more dependencies;
3. search/select existing entities as dependencies;
4. enter a dependency name that does not currently exist;
5. have unknown dependency names preserved as unresolved dependencies;
6. review validation problems before creation;
7. save the entity to persistent storage;
8. immediately refresh dependency state and ranking.

---

# Core behavior

Manual creation must use the same dependency semantics as CSV import.

Example:

```text
New entity:
InvoiceArchive

Dependencies:
Customer
BillingPeriod
FutureTaxConfiguration
```

If:

```text
Customer
BillingPeriod
```

already exist, they become resolved dependencies.

If:

```text
FutureTaxConfiguration
```

does not exist, it must be retained as:

```text
unresolved dependency: FutureTaxConfiguration
```

The new entity is still created successfully.

Its dependency/readiness state becomes unresolved/blocked until that dependency can be resolved.

---

# Manual creation UI

Add a dedicated action to the main WPF application.

Example:

```text
[Import CSV]    [Add Entity]
```

Selecting `Add Entity` should open a focused creation dialog or page.

A reasonable conceptual design:

```text
Add Entity

Entity name
[ InvoiceArchive________________________ ]

Dependencies

[ Search or enter dependency..._________ ]

Selected dependencies:

✓ Customer
✓ BillingPeriod
⚠ FutureTaxConfiguration     Missing

[Cancel]                             [Create]
```

The exact visual design should fit the current WPF application.

Do not introduce a large navigation framework solely for this dialog.

---

# Entity name

The user must provide an entity name.

The input should:

- trim surrounding whitespace;
- use the same normalization/matching policy already established for imported entity names;
- reject an empty or whitespace-only name;
- prevent creation of a duplicate tracked entity.

---

# Duplicate entity handling

If the entered entity name matches an existing tracked entity according to the application's established identity/name-matching policy:

```text
Customer
```

the application must not create another entity with the same logical identity.

Show a clear validation message such as:

```text
An entity named "Customer" already exists.
```

Do not silently:

- create a duplicate;
- append a suffix;
- overwrite the existing entity;
- convert the action into an edit operation.

Editing existing entities belongs to Milestone 7.

---

# Dependency entry

Dependency entry must support both:

1. selection of existing entities;
2. free-text names for entities that do not exist yet.

---

# Existing dependency search

As the user types into the dependency field, search existing active tracked entities.

Example:

```text
Input:
cust
```

Suggestions:

```text
Customer
CustomerAddress
CustomerType
```

The user can choose one of these suggestions.

The selected dependency should then be represented as a resolved dependency to the existing entity.

Search should be reasonably responsive for the expected project size.

No remote API or SharePoint lookup is required.

---

# Free-text dependency entry

The user must not be forced to select a suggestion.

If the user types:

```text
FutureTaxConfiguration
```

and no matching entity exists, the UI should allow that value to be added.

It should be clearly marked:

```text
FutureTaxConfiguration    ⚠ Missing
```

This becomes an unresolved dependency reference using the same model introduced in Milestone 5.1.

Do not create a placeholder entity named `FutureTaxConfiguration`.

---

# Explicit unresolved dependency confirmation

The UI should make it clear when the user is about to add a dependency that does not currently exist.

A lightweight interaction is sufficient.

For example:

```text
No existing entity matches "FutureTaxConfiguration".

[Add as unresolved dependency]
```

Avoid a disruptive confirmation dialog for every unresolved dependency if an inline action is sufficient.

The user must deliberately choose to add the unmatched value rather than having arbitrary text silently converted into a dependency.

---

# Selected dependency list

Once dependencies are added, show them explicitly before creation.

For example:

```text
Dependencies

✓ Customer
✓ Currency
⚠ FutureTaxConfiguration    Missing
```

The user must be able to remove a dependency from the creation form before saving.

General editing after the entity has been created belongs to Milestone 7.

---

# Self-dependency

Prevent:

```text
InvoiceArchive -> InvoiceArchive
```

This applies even when the entity being created does not yet have a persistent ID.

Compare according to the established normalized entity-name rules.

Show an ordinary validation error.

Do not use an exception for this user input case.

---

# Duplicate dependencies

Do not allow the same dependency to be added multiple times.

This includes duplicates caused by equivalent normalized names.

Example:

```text
Customer
customer
 Customer 
```

should not result in three dependencies if the project's matching policy considers them equivalent.

Use the existing normalization policy rather than introducing a second one.

---

# Development state

A manually created entity should begin with the application's normal initial development status.

For example:

```text
NotStarted
```

Do not ask the user to configure workflow history during creation unless the current application already requires it.

If all dependencies are resolved and satisfy the current readiness rules, readiness may be derived normally.

If any dependency is unresolved:

```text
Dependency state: Unresolved / Blocked
```

Missing dependency state must remain separate from development status.

---

# Provenance / source information

The application should retain enough information to know that the entity was created manually.

Do not make source/provenance the entity's identity.

Conceptually:

```text
Tracked Entity
  Stable ID: ...
  Name: InvoiceArchive

Known from:
  Manual creation
```

This provenance must not prevent the entity from later being matched to an imported database entity.

---

# Later CSV synchronization

A manually created entity may later appear in a CSV import.

Example:

```text
Manual state:
FutureTaxConfiguration
```

Later CSV:

```text
FutureTaxConfiguration
```

Milestone 6 synchronization should treat this as the same logical tracked entity when the established entity matching rules identify it as the same entity.

It must not create a duplicate merely because one copy originated manually and the other originated from import.

Preserve:

- stable entity identity;
- development status;
- notes;
- history;
- existing manual information where appropriate.

Imported schema information may then become associated with that same tracked entity.

If the current Milestone 6 implementation does not yet support this scenario cleanly, make the smallest necessary adjustment and add regression coverage.

Do not create a broad provenance framework.

---

# Previously unresolved dependencies resolving to the new entity

Creating a manual entity may resolve unresolved dependencies elsewhere in the tracked schema.

Example before creation:

```text
Order
  unresolved -> FutureTaxConfiguration
```

User manually creates:

```text
FutureTaxConfiguration
```

After successful creation, the application should attempt to resolve matching unresolved dependency references.

Expected result:

```text
Order
  resolved -> FutureTaxConfiguration
```

This should use the same established dependency matching rules as Milestone 6 synchronization.

After resolution:

- rebuild the effective graph;
- recompute unresolved/blocker state;
- recompute ranking.

---

# Persistence

Manual entity creation must be persisted immediately after the user confirms creation.

Persist:

- stable entity identity;
- entity name;
- source/provenance information if modeled;
- resolved dependencies;
- unresolved dependency names;
- initial development state;
- relevant timestamps according to the existing persistence model.

Creation should be transactional where practical.

If persistence fails:

- do not leave a partially created entity;
- show a clear application-level error.

---

# Main overview refresh

After successful creation:

1. close or reset the creation form;
2. reload/refresh the tracked state;
3. resolve newly satisfiable unresolved references;
4. rebuild the effective dependency graph;
5. recompute ranking;
6. update the WPF overview.

The new entity should appear immediately.

If it has unresolved dependencies, it should appear according to Milestone 5.1 behavior rather than receiving a misleading normal rank.

---

# Search behavior

Dependency search should use the currently known active entity set.

At minimum:

- search by entity name;
- match case-insensitively if that is consistent with the project's existing normalization;
- allow partial substring or prefix matching;
- return a manageable set of relevant suggestions.

Do not introduce fuzzy-search infrastructure unless it is already available or trivially justified.

Simple predictable matching is sufficient.

---

# Archived entities

Do not silently select archived entities as normal dependency suggestions.

By default, dependency autocomplete should prioritize or restrict itself to active entities.

If an exact entered name matches only an archived entity, surface that situation clearly rather than silently treating it as a new unresolved dependency.

A reasonable behavior is:

```text
"LegacyCustomer" exists but is archived.
```

Do not automatically reactivate it.

Reactivation/editing policy is outside this milestone unless the current persistence model already has a clear safe behavior.

---

# Validation summary

The Create action must be disabled or rejected when the form contains fatal validation problems.

Examples:

```text
Missing entity name
Duplicate entity name
Self-dependency
Invalid dependency entry
```

Unresolved dependency references are **not** fatal.

They should be visible warnings.

Example:

```text
Warnings:
- FutureTaxConfiguration does not exist and will be added as an unresolved dependency.
```

The user may still create the entity.

---

# Tests

Add or update tests covering at least the following.

## Create entity without dependencies

Create:

```text
A
```

Expected:

- A is persisted;
- A receives stable identity;
- initial development status is correct;
- graph/ranking refresh succeeds.

---

## Create entity with existing dependency

Existing:

```text
A
```

Create:

```text
B -> A
```

Expected:

- B is created;
- dependency resolves to A;
- normal ranking/topological behavior applies.

---

## Create entity with unresolved dependency

Create:

```text
B -> MissingX
```

Expected:

- B is created successfully;
- MissingX is retained;
- B is unresolved/blocked;
- B does not receive a misleading normal dependency-safe rank.

---

## Mixed dependencies

Existing:

```text
A
```

Create:

```text
B -> A, MissingX
```

Expected:

- A is resolved;
- MissingX is unresolved;
- B is unresolved overall.

---

## Duplicate name

Existing:

```text
Customer
```

Attempt to create:

```text
Customer
```

Expected:

- validation error;
- no new entity;
- persisted state unchanged.

Test equivalent normalized-name variants according to the project's naming policy.

---

## Self-dependency

Create:

```text
A -> A
```

Expected:

- validation error;
- no entity created.

---

## Duplicate dependency

Attempt:

```text
B -> A, A
```

Expected:

- no duplicate dependency relationship.

---

## Remove dependency before creation

Add a dependency to the creation form and remove it before Create.

Expected persisted entity does not contain that dependency.

UI-level testing may be used where appropriate; business behavior should remain testable outside WPF.

---

## Manual entity resolves existing unresolved dependency

Existing:

```text
A -> MissingX
```

Manually create:

```text
MissingX
```

Expected:

- A's unresolved reference resolves to the new entity;
- graph/ranking refreshes;
- no duplicate MissingX representation exists.

---

## Later import matches manual entity

Existing manually created:

```text
A
```

Later schema synchronization includes:

```text
A
```

Expected:

- same stable entity identity;
- no duplicate entity;
- existing progress/history preserved.

This may require an integration test involving the Milestone 6 synchronization service.

---

## Persistence failure

Where practical, simulate failure while creating.

Expected:

- no partially created entity/dependency state;
- useful error result.

---

# Acceptance criteria

Milestone 6.1 is complete when:

- [ ] The WPF application has a clear `Add Entity` action.
- [ ] A user can manually enter a new entity name.
- [ ] Empty entity names are rejected.
- [ ] Duplicate tracked entity names are rejected according to established matching rules.
- [ ] Existing entities can be searched while entering dependencies.
- [ ] Existing dependencies can be selected from autocomplete/search results.
- [ ] The user may enter a dependency name that does not currently exist.
- [ ] Unknown dependency names can be deliberately added as unresolved dependencies.
- [ ] Unresolved dependencies are visibly marked before creation.
- [ ] Unresolved dependencies do not prevent otherwise valid entity creation.
- [ ] Self-dependencies are rejected.
- [ ] Duplicate dependencies are prevented.
- [ ] Selected dependencies can be removed before creation.
- [ ] Manual creation is persisted.
- [ ] Manual entities receive stable identity and normal initial development state.
- [ ] Manually created entities with unresolved dependencies remain blocked/unranked according to Milestone 5.1.
- [ ] Creating an entity resolves matching unresolved dependencies elsewhere where applicable.
- [ ] Graph and ranking are recomputed after creation.
- [ ] The WPF overview refreshes and shows the new entity immediately.
- [ ] A manually created entity can later be matched by Milestone 6 CSV synchronization without creating a duplicate.
- [ ] Existing progress/history is preserved when a future import matches a manually created entity.
- [ ] Archived entities are not silently treated as ordinary active dependency suggestions.
- [ ] Creation failure does not leave partially persisted data.
- [ ] General editing of existing entities has not been implemented.
- [ ] `dotnet build` succeeds.
- [ ] `dotnet test` succeeds.

---

# Out of scope

Do not implement:

- editing the name of an existing entity;
- editing dependencies after creation;
- removing existing persisted dependencies;
- manual override management;
- rename workflows;
- entity deletion;
- entity archival/reactivation controls;
- bulk manual creation;
- CSV generation from the manual form;
- SharePoint;
- charting/reporting;
- general entity details/editor functionality from Milestone 7.

If a user makes a mistake during the creation form, they may correct it before clicking Create.

Once the entity exists, editing belongs to Milestone 7.

---

# Architectural guidance

The UI should collect a creation request.

It should not contain entity-creation business rules.

Conceptually:

```text
WPF Add Entity Dialog
        ↓
CreateEntityRequest
        ↓
Application Service
        ↓
Validate name/dependencies
        ↓
Resolve known dependency names
        ↓
Preserve unknown names as unresolved
        ↓
Persist transactionally
        ↓
Resolve existing unresolved references
        ↓
Recompute graph/ranking
        ↓
Refresh UI
```

Names are illustrative.

Reuse existing:

- entity normalization;
- dependency-reference models;
- persistence ports;
- unresolved dependency logic;
- ranking services;
- synchronization matching rules.

Do not introduce parallel implementations of those behaviors.


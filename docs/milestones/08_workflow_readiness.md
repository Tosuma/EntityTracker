# Milestone 8 — Status, Readiness, and Blockers

## Goal
Turn the dependency graph into an implementation-planning tool.

## Tasks
- Finalize the persisted workflow statuses as `Not started`, `In progress`,
  `Dev. completed`, and `Reconciled`.
- Treat both `Dev. completed` and `Reconciled` as dependency-success states. `Reconciled`
  is the higher state and means the implementation has been verified.
- Allow status corrections in either direction; transition history belongs to Milestone 9.
- Make `Ready` a derived value, not a manually maintained field.
- An entity becomes ready when all effective dependencies are `Dev. completed` or `Reconciled`.
- Change the overview's `Missing Dependencies` column to show every effective direct dependency
  that has not been implemented, rather than only unresolved references.
- Include unresolved dependency references in `Missing Dependencies` because they cannot be
  considered implemented while no active tracked entity resolves them.
- Show incomplete blockers for blocked entities.
- Recompute readiness after status/dependency changes.
- Add exclusive manager filters for Ready, Blocked, In Progress, Dev. completed,
  Reconciled, and Archived. Ready and Blocked classify active `Not started` entities;
  the persisted states take precedence for the other active filters.
- Show overall successful progress and the reconciled subset in one layered progress bar;
  archived entities are excluded from its denominator.
- Allow archived entities to be inspected and explicitly restored from the Archived view.
- When manual creation matches an archived entity, offer restoration of the existing tracked
  entity instead of permitting a duplicate.
- Optionally show how many downstream entities each entity blocks.

## Missing Dependencies semantics

For this milestone, `Missing Dependencies` is an implementation-readiness view over an entity's
effective direct dependencies after manual additions and suppressions have been applied.

- A resolved dependency is included while its tracked entity is `Not started` or `In progress`.
- A resolved dependency is omitted once its tracked entity is `Dev. completed` or `Reconciled`.
- An unresolved dependency name is always included. It has no tracked development status and
  therefore cannot satisfy the completion rule.
- Mandatory and optional effective dependencies are both included. A dependency kind does not
  mean that the dependency has already been implemented.
- Results should be deterministic and should not contain duplicate dependency names.
- Readiness requires this list to be empty.

The persisted dependency and unresolved-reference models remain distinct. Completing a dependency
changes readiness and the contents of this column; it does not remove the relationship from the
effective graph. For a direct dependency that is itself blocked, show that direct entity as the
blocker. Do not replace it with all of its transitive root causes in this column.

## Archived entity restoration

Archiving remains a reversible lifecycle change, not deletion. Archived entities must be available
through the `Archived` manager filter but must remain excluded from normal active overview results
and active dependency suggestions until restored.

Restoration must be an explicit user action:

- An archived entity's details must provide a clearly labelled `Restore` action.
- Restoring changes the existing entity's lifecycle state back to `Active`; it must never create a
  new entity or assign a new identity.
- Stable identity, development status, progress, notes, history, imported dependency facts, and
  manual dependency overrides must be preserved.
- Restoring must recompute the effective graph, dependency resolution, ranks, readiness, and
  blockers.
- Previously unresolved references whose normalized source name matches the restored entity must
  resolve where possible.
- Canceling or closing the restore flow must modify nothing.
- Hard deletion is not part of this milestone.

Manual creation must use the same normalized-name matching rules across active and archived
entities. If the entered name matches an archived entity, creation must be blocked and the UI must
clearly offer restoration of that existing entity. It must not silently restore it, create a
duplicate, append a suffix, or replace its preserved state with creation defaults.

CSV synchronization retains its existing reactivation behavior: when an imported entity matches an
archived tracked entity, the synchronization review reactivates that same entity while preserving
its stable state. Manual restoration and CSV reactivation must produce the same active lifecycle
outcome even though their user flows differ.

## Acceptance criteria
- Completing a dependency updates affected readiness automatically.
- `Dev. completed` and `Reconciled` both satisfy dependencies while remaining distinct
  persisted statuses.
- Users can see exactly why an entity is blocked.
- `Missing Dependencies` includes every effective direct dependency that has not met the
  completion rule.
- `Missing Dependencies` includes unresolved dependency references.
- Dev. completed and reconciled dependencies remain in the effective graph but no longer appear in
  `Missing Dependencies`.
- A dependent entity cannot be `Ready` while `Missing Dependencies` contains any resolved or
  unresolved entry.
- Dependencies are not deleted merely because they are completed; completion changes readiness, not graph truth.
- Archived entities can be inspected through the Archived filter without appearing in normal
  active results or dependency suggestions.
- An archived entity can be explicitly restored without changing its identity or losing status,
  progress, notes, history, imported relationships, or manual overrides.
- Restoring an entity refreshes dependency resolution, ranking, readiness, and blockers, including
  resolving matching unresolved references where possible.
- Manual creation cannot duplicate an archived normalized name and provides an explicit path to
  restore the existing entity.
- Canceling restoration changes nothing, and no hard-delete behavior is introduced.
- CSV synchronization continues to reactivate matching archived entities without creating
  duplicates.

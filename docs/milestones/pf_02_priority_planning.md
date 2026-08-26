# Product Feedback Milestone PF-02 — Priority Planning and Replaceable Ranking

## Goal

Supplement dependency-derived rank with customer-driven priority while preserving dependency-safe
work ordering and making the ranking algorithm replaceable through dependency injection.

## Priority model

- Add an optional requested priority from `1` through `5`, where `1` is highest and no value means
  that the entity is not itself a prioritized target.
- Persist `RequestedPriority` on the stable entity. Do not persist effective priority or incorporate
  priority into entity identity.
- Derive `EffectivePriority` over the active effective dependency graph. An entity inherits the
  highest priority, represented by the lowest numeric value, requested by itself or by any active
  entity that transitively depends on it.
- Use imported dependencies after manual additions and suppressions have been applied. Archived
  entities do not participate in the active calculation, but their requested priority is preserved
  for restoration.
- Recompute effective priorities after requested-priority, dependency, synchronization, archive, or
  restore changes. Clearing or changing a target priority must remove obsolete inherited priority
  while retaining priority inherited from other targets.
- Unresolved dependency names cannot receive a priority. Show them in the target preview so the user
  understands that the requested feature tree is incomplete.

## Planning experience

- Add a `Priority` column showing effective priority, while the entity editor distinguishes the
  target's requested priority from its derived effective priority.
- Before applying a requested priority, show the selected target and its resolved transitive
  prerequisites in dependency-safe order, together with any unresolved names affecting the tree.
- Order the normal active overview by effective priority `1` through `5`, followed by unprioritized
  entities. Within each priority segment, retain dependency-safe rank order and deterministic
  handling of unranked entities.
- Preserve the invariant that every dependency appears before its dependent. The derived propagation
  rule ensures a dependency can never have a lower effective priority than an entity requiring it.
- Preserve requested priority through CSV synchronization, backup, archive, and restore. Treat it as
  an independently mergeable scalar field in collaborative storage.

## Ranking abstraction

- Add `IDependencyRankingService` in the Application ranking namespace with the complete contract:

  ```csharp
  DependencyRankingResult Rank(
      IEnumerable<TrackedEntity> entities,
      IEnumerable<DependencyEdge> dependencyEdges,
      IEnumerable<UnresolvedDependency> unresolvedDependencies);
  ```

- Make the current `DependencyRanker` implement the interface without changing its algorithm or
  result types. Its two-argument convenience overload may remain available for direct algorithm
  tests.
- Change every production consumer to depend on `IDependencyRankingService`, including overview,
  synchronization planning, manual creation, dependency editing, and lifecycle operations.
- Register `DependencyRanker` as the default interface implementation in the WPF and screenshot-tool
  composition roots. Production services must not instantiate the concrete ranker directly.
- Keep ranking a pure, provider-neutral computation. Priority changes overview segmentation; it does
  not weaken the topological constraint promised by the ranking interface.

## Tests and verification

- Verify requested priority propagates through direct and transitive prerequisites.
- Verify shared prerequisites inherit the highest priority requested by any dependent target.
- Verify changing and clearing a target priority recalculates inherited priorities without leaving
  copied values behind.
- Verify manual additions and suppressions, archive/restore, and synchronization trigger a fresh
  effective-priority calculation.
- Verify unresolved names are reported in the target preview and do not receive fabricated entities.
- Verify priority-first overview ordering still places every dependency before its dependent.
- Verify SQLite migration and round-trip behavior for null and priorities `1` through `5`, including
  rejection of values outside the range.
- Inject a fake `IDependencyRankingService` into application services and verify its result is used.
- Run the existing ranking regression suite unchanged against `DependencyRanker`.

## Acceptance criteria

- A Team Lead can prioritize a target feature and see everything required to complete it receive the
  same effective planning urgency.
- Priority is visible and controls overview segmentation without replacing or corrupting rank.
- Multiple prioritized targets and later graph changes produce correct, reversible effective values.
- The current ranking engine can be replaced at the composition root through
  `IDependencyRankingService`.

## Out of scope

- User-defined priority scales or values outside `1` through `5`.
- Persisting derived rank or effective priority.
- Allowing a ranking implementation to violate dependency ordering.
- Assigning priority to unresolved placeholder entities.

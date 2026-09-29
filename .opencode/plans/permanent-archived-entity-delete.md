# Permanently delete archived entities

## Goal

Allow a user to permanently delete an entity from the Archived view. The operation must require typing the exact entity name and must preserve incoming references from other entities as unresolved dependencies using the deleted entity’s source name.

## Current state

- Entities only have `Active` and `Archived` lifecycle states (`src/EntityTracker.Domain/EntityLifecycleState.cs`).
- `EntityLifecycleService` supports `TryArchiveAsync` and `RestoreAsync`, but has no purge/delete method (`src/EntityTracker.Application/Lifecycle/EntityLifecycleService.cs`).
- Archived entity details expose only **Restore entity** (`src/EntityTracker.Wpf/ViewModels/EntityDependencyEditorViewModel.cs`, `src/EntityTracker.Wpf/Views/TrackerWorkspaceView.xaml`).
- `TrackedStateChangeSet` and `ITrackedStateStore` only support archive/restore and reconciliation; neither has entity deletion semantics (`src/EntityTracker.Application/Persistence/TrackedStateChangeSet.cs`).
- SQLite has foreign-key cascade deletion for resolved dependency rows and dependent-owned unresolved/manual override rows. It also contains entity status history and progress snapshots. (`src/EntityTracker.Infrastructure/Persistence/SqliteDatabase.cs`, `SqliteTrackedStateStore.cs`.)
- Project/tracker catalog purge already uses a typed-name confirmation and permanent destructive operation pattern, but it does not implement a single-entity purge (`CatalogManagementViewModel`, `SqliteProjectTrackerStore`).
- Unresolved dependencies are represented by owner ID, source name/key, and imported dependency kind. This supports converting an incoming `schema_dependencies` edge to an unresolved dependency without changing its mandatory/optional classification.

## Chosen behavior

1. Permanent deletion is available **only for archived entities**.
2. User must type the exact archived entity name to enable the delete button.
3. Incoming dependency references (`other entity -> deleted entity`) are retained as unresolved references to the deleted entity’s `SourceName`, preserving each relationship’s `ImportedDependencyKind`.
4. The deleted entity’s own dependencies, manual dependency overrides, entity status history, and entity row are permanently removed.
5. The operation recalculates and stores the tracker progress snapshot after removal/conversion.

## Implementation steps

1. **Add an explicit entity-purge persistence operation.**
   - Add a small application-level request/result model if needed (for example `EntityPurgeResult`) that distinguishes: deleted, not found/not archived, and validation failure.
   - Prefer a dedicated method on `ITrackedStateStore` (for example `PurgeArchivedEntityAsync`) rather than overloading `TrackedStateChangeSet`, because permanent deletion has ordering-sensitive SQL cleanup and conversion semantics that do not fit ordinary change-set reconciliation.
   - Include enough input for the persistence layer to assert tracker ownership and archived state, and to receive a precomputed replacement dependency/progress state if application-layer validation remains there.

2. **Implement deletion semantics in `EntityLifecycleService`.**
   - Add `PurgeArchivedAsync(TrackerId, EntityId, CancellationToken)`.
   - Load the same consistent snapshot as archive/restore: entities, resolved dependencies, unresolved dependencies, and manual overrides; validate ownership.
   - Reject if the target is missing or not archived.
   - Build replacement dependency data:
     - For every resolved dependency whose `DependencyEntityId` equals the target ID, replace it with `PersistedUnresolvedDependency(new UnresolvedDependency(dependentId, target.SourceName), original.Kind)`.
     - Preserve already-unresolved dependencies, unless one has the same dependent/source key as a converted relationship; define deterministic collision handling (prefer existing unresolved entry when same key/kind; otherwise retain a single canonical row and surface/resolve kind conflict deliberately).
     - Exclude all resolved/unresolved relationships owned by the target, because the target is being removed.
     - Remove manual overrides owned by the target. For overrides owned by other entities that name the deleted entity, retain them only if they still carry meaning with the unresolved replacement; otherwise remove/normalize them to avoid an override referring to a now-deleted entity. This needs an explicit rule and test coverage.
   - Recompute `EffectiveDependencyState`, ranking, and `ProgressSnapshotState` for the remaining entities. Since the target is archived, calculate against remaining active entities while preserving unresolved references for dependents.
   - Persist the converted relationship state and deletion atomically via the new persistence method.

3. **Add atomic SQLite implementation in `SqliteTrackedStateStore`.**
   - In one transaction, verify the target belongs to the supplied tracker and has `lifecycle_state = 'Archived'`.
   - Reconcile affected dependency owners: delete their resolved incoming edges to the archived target and upsert the corresponding unresolved entries with the target’s original source key/name and original dependency kind.
   - Delete target-owned manual overrides and target-owned dependency rows; remove any no-longer-valid override rows according to the lifecycle-service rule.
   - Delete `entity_status_history` rows for the entity.
   - Delete the entity row; foreign keys should cascade remaining rows directly owned by it, but issue explicit cleanup where necessary and do not depend on cascade for conversion behavior.
   - Insert the recalculated progress snapshot if changed, then commit.
   - Do not delete tracker-wide snapshots/import summaries; those remain tracker history.

4. **Expose typed-name purge confirmation in `EntityDependencyEditorViewModel`.**
   - Add state for a permanent-delete confirmation dialog: visibility, typed confirmation text, error message, and commands to request/cancel/confirm.
   - Add `CanPermanentlyDeleteEntity` requiring: editor open, archived mode, not busy, operation permitted, selected archived entity present, and typed confirmation exactly equal to `ArchivedDetails.Entity.SourceName`.
   - Add a permanent-delete confirmation message that clearly states: irreversible, own history/dependencies are removed, and dependent entities will retain the deleted name as unresolved.
   - On successful purge: invoke a new host callback to refresh overview/archived data, then close editor. Handle errors in the dialog rather than crashing/closing.
   - Ensure Escape cancels the permanent-delete confirmation before closing the editor.

5. **Wire view and host callbacks.**
   - In `TrackerWorkspaceView.xaml`, add a visible **Permanently delete entity** danger action only in archived-details mode, plus a modal confirmation overlay with exact-name input, Cancel, and disabled-until-valid delete button.
   - Update `TrackerWorkspaceView.xaml.cs` focus restoration to account for the new confirmation overlay, analogous to archive confirmation.
   - Update `TrackerWorkspaceViewModelFactory` / `MainWindowViewModel` construction callbacks so purge refreshes active and archived tables, clears relevant selection/details state, and leaves the user on the Archived tab.

6. **Tests.**
   - `EntityLifecycleService` tests:
     - rejects active entity and missing entity;
     - permanently removes archived target;
     - converts an incoming mandatory and optional resolved edge into unresolved dependencies retaining source name/kind;
     - removes target-owned dependencies, overrides, and status history;
     - recalculates dependent workflow/progress state;
     - validates collision behavior where an unresolved reference with the same source key already exists.
   - SQLite integration tests verifying transaction persistence, no target row/history remains, and converted unresolved rows exist.
   - `EntityDependencyEditorViewModel` tests for typed confirmation gating, cancellation, success callback/close, and failure display.
   - Presentation tests verifying delete control is only shown for archived entity mode and confirmation controls are wired.

## Risks / open design detail

Manual overrides on *other* entities that refer to the deleted entity’s source name need a defined normalization policy. The safest default is to preserve `Add` overrides (they remain a deliberate unresolved addition) and remove `Suppress` overrides only when there is no imported/unresolved relationship to suppress after conversion. Confirm this behavior before implementation if these overrides are common in your data.

using EntityTracker.Application.Importing;
using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Planning;
using EntityTracker.Application.Synchronization;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests;

internal static class TestTrackerExtensions
{
    internal static Task<ManualDependencySearchResult> SearchDependenciesAsync(
        this ManualEntityCreationService service,
        string query,
        string? proposedEntityName = null,
        CancellationToken cancellationToken = default) =>
        service.SearchDependenciesAsync(
            TestTrackerId,
            query,
            proposedEntityName,
            cancellationToken);

    internal static Task<IReadOnlyList<string>> SearchGroupNamesAsync(
        this ManualEntityCreationService service,
        string query,
        CancellationToken cancellationToken = default) =>
        service.SearchGroupNamesAsync(TestTrackerId, query, cancellationToken);

    internal static Task<ManualEntityCreationResult> CreateAsync(
        this ManualEntityCreationService service,
        ManualEntityCreationRequest request,
        CancellationToken cancellationToken = default) =>
        service.CreateAsync(TestTrackerId, request, cancellationToken);

    internal static Task<EntityDependencyEditPlan> LoadAsync(
        this EntityDependencyEditorService service,
        EntityId ownerId,
        CancellationToken cancellationToken = default) =>
        service.LoadAsync(TestTrackerId, ownerId, cancellationToken);

    internal static Task<ArchivedEntityDetails> LoadArchivedDetailsAsync(
        this EntityDependencyEditorService service,
        EntityId ownerId,
        CancellationToken cancellationToken = default) =>
        service.LoadArchivedDetailsAsync(TestTrackerId, ownerId, cancellationToken);

    internal static Task<ManualDependencySearchResult> SearchDependenciesAsync(
        this EntityDependencyEditorService service,
        EntityId ownerId,
        string query,
        CancellationToken cancellationToken = default) =>
        service.SearchDependenciesAsync(TestTrackerId, ownerId, query, cancellationToken);

    internal static Task<IReadOnlyList<string>> SearchGroupNamesAsync(
        this EntityDependencyEditorService service,
        string query,
        CancellationToken cancellationToken = default) =>
        service.SearchGroupNamesAsync(TestTrackerId, query, cancellationToken);

    internal static EntityDependencyEditPlan CreatePlan(
        this EntityDependencyEditorService service,
        EntityId ownerId,
        IEnumerable<TrackedEntity> entities,
        IEnumerable<PersistedDependency> importedResolvedDependencies,
        IEnumerable<PersistedUnresolvedDependency> importedUnresolvedDependencies,
        IEnumerable<ManualDependencyOverride> allOverrides,
        IEnumerable<ManualDependencyOverride> desiredOwnerOverrides) =>
        service.CreatePlan(
            TestTrackerId,
            ownerId,
            entities,
            importedResolvedDependencies,
            importedUnresolvedDependencies,
            allOverrides,
            desiredOwnerOverrides);

    internal static Task SaveAsync(
        this EntityDependencyEditorService service,
        EntityDependencyEditPlan plan,
        DevelopmentStatus status,
        string notes,
        int? requestedPriority,
        string? responsibleDeveloper,
        string? groupName,
        CancellationToken cancellationToken = default) =>
        service.SaveAsync(
            TestTrackerId,
            plan,
            status,
            notes,
            requestedPriority,
            responsibleDeveloper,
            groupName,
            cancellationToken);

    internal static PriorityPlanningPreview CreatePriorityPreview(
        this EntityDependencyEditorService service,
        EntityDependencyEditPlan plan,
        int? candidateRequestedPriority) =>
        service.CreatePriorityPreview(TestTrackerId, plan, candidateRequestedPriority);

    internal static Task<bool> TryArchiveAsync(
        this EntityLifecycleService service,
        EntityId entityId,
        CancellationToken cancellationToken = default) =>
        service.TryArchiveAsync(TestTrackerId, entityId, cancellationToken);

    internal static Task<EntityRestorationResult> RestoreAsync(
        this EntityLifecycleService service,
        EntityId entityId,
        CancellationToken cancellationToken = default) =>
        service.RestoreAsync(TestTrackerId, entityId, cancellationToken);

    internal static Task<EntityOverviewResult> GetAsync(
        this EntityOverviewService service,
        CancellationToken cancellationToken = default) =>
        service.GetAsync(TestTrackerId, cancellationToken);

    internal static Task<BulkStatusUpdateResult> ApplyAsync(
        this BulkStatusUpdateService service,
        IReadOnlyCollection<EntityId> entityIds,
        DevelopmentStatus targetStatus,
        CancellationToken cancellationToken = default) =>
        service.ApplyAsync(TestTrackerId, entityIds, targetStatus, cancellationToken);

    internal static SchemaSynchronizationPlan CreatePlan(
        this SchemaSynchronizationPlanner planner,
        SchemaImportCandidate importCandidate,
        SchemaImportMode mode,
        IEnumerable<TrackedEntity> persistedEntities,
        IEnumerable<PersistedDependency> persistedDependencies,
        IEnumerable<PersistedUnresolvedDependency> persistedUnresolvedDependencies,
        IEnumerable<ManualDependencyOverride>? manualDependencyOverrides = null) =>
        planner.CreatePlan(
            TestTrackerId,
            importCandidate,
            mode,
            persistedEntities,
            persistedDependencies,
            persistedUnresolvedDependencies,
            manualDependencyOverrides);

    internal static Task<SchemaSynchronizationResult> PlanAsync(
        this SchemaSynchronizationService service,
        string filePath,
        SchemaImportMode mode,
        CancellationToken cancellationToken = default) =>
        service.PlanAsync(TestTrackerId, filePath, mode, cancellationToken);

    internal static EntityDependencyEditPlan PreviewDependencyEdit(
        this SchemaSynchronizationService service,
        SchemaSynchronizationPlan plan,
        EntityId ownerId,
        IEnumerable<ManualDependencyOverride> desiredOwnerOverrides) =>
        service.PreviewDependencyEdit(TestTrackerId, plan, ownerId, desiredOwnerOverrides);

    internal static SchemaSynchronizationPlan StageDependencyEdit(
        this SchemaSynchronizationService service,
        SchemaSynchronizationPlan plan,
        EntityDependencyEditPlan editPlan) =>
        service.StageDependencyEdit(TestTrackerId, plan, editPlan);

    internal static SchemaSynchronizationPlan StageProgressDecision(
        this SchemaSynchronizationService service,
        SchemaSynchronizationPlan plan,
        EntityId entityId,
        SynchronizationProgressDecision decision) =>
        service.StageProgressDecision(TestTrackerId, plan, entityId, decision);

    internal static Task<SchemaImportSummary> ApplyAsync(
        this SchemaSynchronizationService service,
        SchemaSynchronizationPlan plan,
        string sourceFileName,
        CancellationToken cancellationToken = default) =>
        service.ApplyAsync(TestTrackerId, plan, sourceFileName, cancellationToken);

    internal static Task<SchemaImportSummary?> GetLatestImportAsync(
        this SchemaSynchronizationService service,
        CancellationToken cancellationToken = default) =>
        service.GetLatestImportAsync(TestTrackerId, cancellationToken);
}

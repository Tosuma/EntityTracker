using EntityTracker.Application.Dependencies;
using EntityTracker.Application.Lifecycle;
using EntityTracker.Application.ManualCreation;
using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.Overview;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Planning;
using EntityTracker.Application.Workflow;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests;

internal static class TestTrackerExtensions
{
    internal static Task<ManualDependencySearchResult> SearchDependenciesAsync(
        this ManualEntityCreationService service,
        string query,
        string? proposedEntityName = null,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<EntitySourceKey>? excludedKeys = null) =>
        service.SearchDependenciesAsync(
            TestTrackerId,
            query,
            proposedEntityName,
            cancellationToken,
            excludedKeys);

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
        IEnumerable<EntityDependency> allDependencies,
        IEnumerable<DependencyTarget> desiredTargets) =>
        service.CreatePlan(TestTrackerId, ownerId, entities, allDependencies, desiredTargets);

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

    internal static Task<BulkEntityUpdateResult> ApplyAsync(
        this BulkEntityUpdateService service,
        IReadOnlyCollection<EntityId> entityIds,
        BulkEntityUpdate update,
        CancellationToken cancellationToken = default) =>
        service.ApplyAsync(TestTrackerId, entityIds, update, cancellationToken);
}

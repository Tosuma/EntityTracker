using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Synchronization;
using EntityTracker.Domain;

namespace EntityTracker.Application.Collaboration;

public sealed class ProjectMutationCoordinator(
    IProjectMutationBackend backend,
    ITrackerRepository trackerRepository,
    TimeProvider? timeProvider = null) : IProjectTrackerStore, ITrackedStateStore, ISchemaSynchronizationStore
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public Task CreateProjectAsync(Project project, CancellationToken cancellationToken = default) =>
        backend.ApplyAsync(new CreateProjectMutation(project, OperationId.New(), Now()), cancellationToken);

    public Task CreateTrackerAsync(TrackerCreationState creation, CancellationToken cancellationToken = default)
    {
        RepositoryOperationKind kind = creation.Tracker.CopiedFromTrackerId is null
            ? RepositoryOperationKind.TrackerCreated
            : RepositoryOperationKind.TrackerCopied;
        return backend.ApplyAsync(new CreateTrackerMutation(
            creation.Tracker.ProjectId,
            creation,
            creation.ChangeSet.OperationId,
            kind,
            Now()), cancellationToken);
    }

    public Task RenameProjectAsync(ProjectId projectId, string name, CancellationToken cancellationToken = default) =>
        backend.ApplyAsync(new RenameProjectMutation(projectId, name, OperationId.New(), Now()), cancellationToken);

    public Task SetProjectLifecycleAsync(ProjectId projectId, CatalogLifecycleState lifecycleState, CancellationToken cancellationToken = default) =>
        backend.ApplyAsync(new SetProjectLifecycleMutation(projectId, lifecycleState, OperationId.New(), Now()), cancellationToken);

    public Task PurgeProjectAsync(ProjectId projectId, CancellationToken cancellationToken = default) =>
        backend.ApplyAsync(new PurgeProjectMutation(projectId, OperationId.New(), Now()), cancellationToken);

    public async Task RenameTrackerAsync(TrackerId trackerId, string name, CancellationToken cancellationToken = default)
    {
        Tracker tracker = await RequireTrackerAsync(trackerId, cancellationToken);
        await backend.ApplyAsync(new RenameTrackerMutation(tracker.ProjectId, trackerId, name, OperationId.New(), Now()), cancellationToken);
    }

    public async Task SetTrackerLifecycleAsync(TrackerId trackerId, CatalogLifecycleState lifecycleState, CancellationToken cancellationToken = default)
    {
        Tracker tracker = await RequireTrackerAsync(trackerId, cancellationToken);
        await backend.ApplyAsync(new SetTrackerLifecycleMutation(tracker.ProjectId, trackerId, lifecycleState, OperationId.New(), Now()), cancellationToken);
    }

    public async Task PurgeTrackerAsync(TrackerId trackerId, CancellationToken cancellationToken = default)
    {
        Tracker tracker = await RequireTrackerAsync(trackerId, cancellationToken);
        await backend.ApplyAsync(new PurgeTrackerMutation(tracker.ProjectId, trackerId, OperationId.New(), Now()), cancellationToken);
    }

    public async Task ApplyAsync(TrackerId trackerId, TrackedStateChangeSet changeSet, CancellationToken cancellationToken = default)
    {
        Tracker tracker = await RequireTrackerAsync(trackerId, cancellationToken);
        await backend.ApplyAsync(new ChangeTrackedStateMutation(
            tracker.ProjectId,
            trackerId,
            changeSet,
            null,
            Classify(changeSet),
            Now()), cancellationToken);
    }

    public async Task<SchemaImportSummary> ApplyAsync(TrackerId trackerId, TrackedStateChangeSet changeSet, SchemaImportCompletion completion, CancellationToken cancellationToken = default)
    {
        Tracker tracker = await RequireTrackerAsync(trackerId, cancellationToken);
        ProjectMutationResult result = await backend.ApplyAsync(new ChangeTrackedStateMutation(
            tracker.ProjectId,
            trackerId,
            changeSet,
            completion,
            RepositoryOperationKind.SchemaImported,
            Now()), cancellationToken);
        return result.ImportSummary ?? throw new InvalidOperationException("The schema import did not produce a summary.");
    }

    public Task EnsureHistoryBaselineAsync(TrackerId trackerId, IEnumerable<TrackedEntity> entities, ProgressSnapshotState snapshot, CancellationToken cancellationToken = default) =>
        backend.EnsureHistoryBaselineAsync(trackerId, entities, snapshot, cancellationToken);

    public Task<SchemaImportSummary?> GetLatestImportAsync(TrackerId trackerId, CancellationToken cancellationToken = default) =>
        backend.GetLatestImportAsync(trackerId, cancellationToken);

    private async Task<Tracker> RequireTrackerAsync(TrackerId trackerId, CancellationToken cancellationToken) =>
        await trackerRepository.GetAsync(trackerId, cancellationToken)
        ?? throw new InvalidOperationException("The tracker no longer exists.");

    private DateTimeOffset Now() => _timeProvider.GetUtcNow().ToUniversalTime();

    private static RepositoryOperationKind Classify(TrackedStateChangeSet changeSet)
    {
        if (changeSet.EntitiesToAdd.Count > 0) return RepositoryOperationKind.EntityCreated;
        if (changeSet.EntityIdsToArchive.Count > 0) return RepositoryOperationKind.EntityArchived;
        if (changeSet.EntityIdsToRestore.Count > 0) return RepositoryOperationKind.EntityRestored;
        if (changeSet.EntitiesWithProgressToUpdate.Count > 1) return RepositoryOperationKind.BulkStatusUpdated;
        bool statusOnly = changeSet.EntitiesWithProgressToUpdate.Count == 1 &&
                          changeSet.EntitiesToUpdate.Count == 0 &&
                          changeSet.ReconciledOwnerIds.Count == 0 &&
                          changeSet.ReconciledOverrideOwnerIds.Count == 0 &&
                          changeSet.EntitiesWithRequestedPriorityToUpdate.Count == 0 &&
                          changeSet.EntitiesWithResponsibleDeveloperToUpdate.Count == 0 &&
                          changeSet.EntitiesWithGroupNameToUpdate.Count == 0;
        if (statusOnly) return RepositoryOperationKind.StatusUpdated;
        bool dependenciesOnly = changeSet.ReconciledOwnerIds.Count > 0 || changeSet.ReconciledOverrideOwnerIds.Count > 0;
        return dependenciesOnly && changeSet.EntitiesWithProgressToUpdate.Count == 0 &&
               changeSet.EntitiesWithRequestedPriorityToUpdate.Count == 0 &&
               changeSet.EntitiesWithResponsibleDeveloperToUpdate.Count == 0 &&
               changeSet.EntitiesWithGroupNameToUpdate.Count == 0
            ? RepositoryOperationKind.DependencyEdited
            : RepositoryOperationKind.EntityEdited;
    }
}

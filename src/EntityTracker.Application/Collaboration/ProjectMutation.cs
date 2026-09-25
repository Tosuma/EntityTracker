using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Synchronization;
using EntityTracker.Domain;

namespace EntityTracker.Application.Collaboration;

public abstract record ProjectMutation(
    ProjectId ProjectId,
    OperationId OperationId,
    RepositoryOperationKind Kind,
    DateTimeOffset OccurredAtUtc);

public sealed record CreateProjectMutation(Project Project, OperationId OperationId, DateTimeOffset OccurredAtUtc)
    : ProjectMutation(Project.Id, OperationId, RepositoryOperationKind.ProjectCreated, OccurredAtUtc);

public sealed record RenameProjectMutation(ProjectId ProjectId, string Name, OperationId OperationId, DateTimeOffset OccurredAtUtc)
    : ProjectMutation(ProjectId, OperationId, RepositoryOperationKind.ProjectRenamed, OccurredAtUtc);

public sealed record SetProjectLifecycleMutation(ProjectId ProjectId, CatalogLifecycleState LifecycleState, OperationId OperationId, DateTimeOffset OccurredAtUtc)
    : ProjectMutation(ProjectId, OperationId, LifecycleState == CatalogLifecycleState.Recycled ? RepositoryOperationKind.ProjectRecycled : RepositoryOperationKind.ProjectRestored, OccurredAtUtc);

public sealed record PurgeProjectMutation(ProjectId ProjectId, OperationId OperationId, DateTimeOffset OccurredAtUtc)
    : ProjectMutation(ProjectId, OperationId, RepositoryOperationKind.ProjectPurged, OccurredAtUtc);

public sealed record CreateTrackerMutation(ProjectId ProjectId, TrackerCreationState Creation, OperationId OperationId, RepositoryOperationKind Kind, DateTimeOffset OccurredAtUtc)
    : ProjectMutation(ProjectId, OperationId, Kind, OccurredAtUtc);

public sealed record RenameTrackerMutation(ProjectId ProjectId, TrackerId TrackerId, string Name, OperationId OperationId, DateTimeOffset OccurredAtUtc)
    : ProjectMutation(ProjectId, OperationId, RepositoryOperationKind.TrackerRenamed, OccurredAtUtc);

public sealed record SetTrackerLifecycleMutation(ProjectId ProjectId, TrackerId TrackerId, CatalogLifecycleState LifecycleState, OperationId OperationId, DateTimeOffset OccurredAtUtc)
    : ProjectMutation(ProjectId, OperationId, LifecycleState == CatalogLifecycleState.Recycled ? RepositoryOperationKind.TrackerRecycled : RepositoryOperationKind.TrackerRestored, OccurredAtUtc);

public sealed record PurgeTrackerMutation(ProjectId ProjectId, TrackerId TrackerId, OperationId OperationId, DateTimeOffset OccurredAtUtc)
    : ProjectMutation(ProjectId, OperationId, RepositoryOperationKind.TrackerPurged, OccurredAtUtc);

public sealed record ChangeTrackedStateMutation(
    ProjectId ProjectId,
    TrackerId TrackerId,
    TrackedStateChangeSet ChangeSet,
    SchemaImportCompletion? ImportCompletion,
    RepositoryOperationKind Kind,
    DateTimeOffset OccurredAtUtc)
    : ProjectMutation(ProjectId, ChangeSet.OperationId, Kind, OccurredAtUtc);

public sealed record ProjectMutationResult(SchemaImportSummary? ImportSummary = null);

public interface IProjectMutationBackend
{
    Task<ProjectMutationResult> ApplyAsync(
        ProjectMutation mutation,
        CancellationToken cancellationToken = default);

    Task EnsureHistoryBaselineAsync(
        TrackerId trackerId,
        IEnumerable<TrackedEntity> entities,
        ProgressSnapshotState snapshot,
        CancellationToken cancellationToken = default);

    Task<SchemaImportSummary?> GetLatestImportAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken = default);
}

public enum ProjectRepositoryStatusKind
{
    SQLiteOnly,
    GitClean,
    Unavailable,
    StaleCache,
    Blocked
}

public sealed record ProjectRepositoryStatus(
    ProjectId ProjectId,
    ProjectRepositoryStatusKind Kind,
    string? RepositoryPath = null,
    string? ManagedBranch = null,
    string? Diagnostic = null)
{
    public bool IsGitBacked => Kind != ProjectRepositoryStatusKind.SQLiteOnly;
    public bool CanUseProject => Kind is ProjectRepositoryStatusKind.SQLiteOnly or ProjectRepositoryStatusKind.GitClean;
}

public interface IProjectRepositoryManager
{
    Task<IReadOnlyList<ProjectRepositoryStatus>> GetStatusesAsync(CancellationToken cancellationToken = default);
    Task<ProjectRepositoryStatus> GetStatusAsync(ProjectId projectId, CancellationToken cancellationToken = default);
    Task LinkAsync(ProjectId projectId, string repositoryPath, CancellationToken cancellationToken = default);
    Task<ProjectId> OpenAsync(string repositoryPath, CancellationToken cancellationToken = default);
    Task LocateAsync(ProjectId projectId, string repositoryPath, CancellationToken cancellationToken = default);
    Task RebuildCacheAsync(ProjectId projectId, CancellationToken cancellationToken = default);
}

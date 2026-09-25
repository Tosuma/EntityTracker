using EntityTracker.Application.Collaboration;
using EntityTracker.Domain;

namespace EntityTracker.Screenshots;

internal sealed class ScreenshotRepositoryManager : IProjectRepositoryManager, IProjectSynchronizationService
{
    private readonly Dictionary<ProjectId, ProjectRepositoryStatus> _statuses = [];

    internal void SetStatus(ProjectId projectId, ProjectRepositoryStatusKind kind, string diagnostic) =>
        _statuses[projectId] = new ProjectRepositoryStatus(
            projectId,
            kind,
            @"C:\Work\EntityTracker\Order Modernization",
            "main",
            diagnostic);

    internal void ClearStatus(ProjectId projectId) => _statuses.Remove(projectId);

    internal void SetSyncStatus(
        ProjectId projectId,
        ProjectSyncState syncState,
        string? diagnostic = null,
        int? ahead = null,
        int? behind = null) =>
        _statuses[projectId] = new ProjectRepositoryStatus(
            projectId,
            ProjectRepositoryStatusKind.GitClean,
            @"C:\Work\EntityTracker\Order Modernization",
            "main",
            diagnostic,
            syncState,
            syncState == ProjectSyncState.NoUpstream ? null : "team/main",
            ahead ?? (syncState == ProjectSyncState.UpToDate ? 0 : null),
            behind ?? (syncState == ProjectSyncState.UpToDate ? 0 : null),
            syncState == ProjectSyncState.NoUpstream
                ? null
                : new DateTimeOffset(2026, 9, 25, 8, 30, 0, TimeSpan.Zero),
            syncState == ProjectSyncState.NoUpstream
                ? null
                : new DateTimeOffset(2026, 9, 24, 15, 45, 0, TimeSpan.Zero));

    public Task<IReadOnlyList<ProjectRepositoryStatus>> GetStatusesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProjectRepositoryStatus>>(_statuses.Values.ToArray());

    public Task<ProjectRepositoryStatus> GetStatusAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_statuses.TryGetValue(projectId, out ProjectRepositoryStatus? status)
            ? status
            : new ProjectRepositoryStatus(projectId, ProjectRepositoryStatusKind.SQLiteOnly));

    public Task LinkAsync(ProjectId projectId, string repositoryPath, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ProjectId> OpenAsync(string repositoryPath, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task LocateAsync(ProjectId projectId, string repositoryPath, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task RebuildCacheAsync(ProjectId projectId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ProjectSyncResult> SyncAsync(ProjectId projectId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

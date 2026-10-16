using EntityTracker.Application.GitSync;
using EntityTracker.Application.Snapshots;
using EntityTracker.Domain;
using EntityTracker.Wpf.ViewModels;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntityTracker.Wpf.Services;

/// <summary>
/// The sync icon of every linked Project, shared by the header, the Portfolio and the Project dashboard
/// (each <see cref="Controls.SyncStatusIcon"/> finds it as the app resource <see cref="ResourceKey"/>).
/// <see cref="RefreshAsync"/> reads the Git links and each Project's revision, which are small reads,
/// and raises <see cref="Changed"/> on the UI thread when an icon changes.
/// </summary>
public sealed class ProjectSyncIndicators(IProjectSyncLinkStore links, IProjectSnapshotStore snapshots,
    ProjectAutoSyncService? autoSync = null, TimeProvider? clock = null,
    ILogger<ProjectSyncIndicators>? logger = null)
{
    public const string ResourceKey = "ProjectSyncIndicators";

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ILogger _logger = logger ?? NullLogger<ProjectSyncIndicators>.Instance;
    private readonly SemaphoreSlim _refreshing = new(1, 1);
    private IReadOnlyDictionary<Guid, ProjectSyncIndicator> _indicators = new Dictionary<Guid, ProjectSyncIndicator>();

    public event EventHandler? Changed;

    public ProjectSyncIndicator Get(Guid projectId) =>
        _indicators.TryGetValue(projectId, out ProjectSyncIndicator? indicator) ? indicator : ProjectSyncIndicator.None;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        // A refresh already running reads the latest state anyway.
        if (!await _refreshing.WaitAsync(0, cancellationToken)) return;
        try
        {
            Dictionary<Guid, ProjectSyncIndicator> next = [];
            DateTimeOffset now = _clock.GetUtcNow();
            foreach (ProjectSyncLink link in await links.ReadAllAsync(cancellationToken))
            {
                ProjectId projectId = new(link.ProjectId);
                ProjectSyncState state = autoSync?.GetState(projectId) is { Kind: not ProjectSyncStateKind.Unlinked } known
                    ? known
                    : ProjectAutoSyncService.FromLink(link);
                long revision = await snapshots.GetRevisionAsync(projectId, cancellationToken);
                next[link.ProjectId] = ProjectSyncIndicator.From(link, state, revision, now);
            }
            if (next.Count == _indicators.Count &&
                next.All(pair => _indicators.TryGetValue(pair.Key, out ProjectSyncIndicator? old) && old == pair.Value))
                return;
            _indicators = next;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // The icons keep their last state; the next refresh tries again.
            _logger.LogWarning(error, "The Project sync icons could not be refreshed.");
        }
        finally
        {
            _refreshing.Release();
        }
    }
}

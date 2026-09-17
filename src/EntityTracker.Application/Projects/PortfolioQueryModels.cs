using EntityTracker.Application.History;
using EntityTracker.Domain;

namespace EntityTracker.Application.Projects;

public sealed record TrackerProgressSummary(
    int ActiveEntityCount,
    int ImplementedEntityCount,
    double? ImplementedPercentage,
    int ReadyCount,
    int BlockedCount,
    int ReworkNeededCount,
    int DependencyIssueCount)
{
    public string ImplementedProgressText => ImplementedPercentage is null
        ? "No active entities"
        : $"{ImplementedPercentage.Value:0}% implemented";

    public static TrackerProgressSummary From(
        ProgressSnapshotState state,
        int dependencyIssueCount)
    {
        double? percentage = state.TotalActiveCount == 0
            ? null
            : state.ImplementedCount * 100d / state.TotalActiveCount;
        return new TrackerProgressSummary(
            state.TotalActiveCount,
            state.ImplementedCount,
            percentage,
            state.ReadyCount,
            state.BlockedCount,
            state.ReworkNeededCount,
            dependencyIssueCount);
    }
}

public sealed record TrackerDashboardSummary(
    TrackerId TrackerId,
    ProjectId ProjectId,
    string Name,
    TrackerProgressSummary Progress,
    TrackerId? CopiedFromTrackerId);

public sealed record ProjectPortfolioSummary(
    ProjectId ProjectId,
    string Name,
    int ActiveTrackerCount,
    TrackerProgressSummary Progress);

public sealed record PortfolioDashboard(
    IReadOnlyList<ProjectPortfolioSummary> Projects);

public sealed record ProjectDashboard(
    Project Project,
    IReadOnlyList<TrackerDashboardSummary> Trackers,
    TrackerProgressSummary Progress);

public sealed record CatalogPurgeImpact(
    int TrackerCount,
    int ActiveEntityCount,
    int ArchivedEntityCount,
    int StatusHistoryCount,
    int ProgressSnapshotCount,
    int ImportSummaryCount)
{
    public int EntityCount => ActiveEntityCount + ArchivedEntityCount;

    public int HistoryCount => StatusHistoryCount + ProgressSnapshotCount;
}

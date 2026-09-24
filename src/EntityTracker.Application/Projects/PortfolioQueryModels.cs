using EntityTracker.Application.History;
using EntityTracker.Domain;

namespace EntityTracker.Application.Projects;

public sealed record TrackerProgressSummary(
    ProgressSnapshotState State,
    int UnresolvedReferenceCount,
    DateTimeOffset? LastActivityUtc)
{
    public int ActiveEntityCount => State.TotalActiveCount;

    public int ImplementedEntityCount => State.ImplementedCount;

    public int NotStartedCount => State.NotStartedCount;

    public int InProgressCount => State.InProgressCount;

    public int ReworkNeededCount => State.ReworkNeededCount;

    public int DevelopmentCompletedCount => State.DevelopmentCompletedCount;

    public int ReconciledCount => State.ReconciledCount;

    public int ReadyCount => State.ReadyCount;

    public int BlockedCount => State.BlockedCount;

    public int DependencyIssueCount => UnresolvedReferenceCount;

    public double? ImplementedPercentage => ActiveEntityCount == 0
        ? null
        : ImplementedEntityCount * 100d / ActiveEntityCount;

    public double? ReconciledPercentage => ActiveEntityCount == 0
        ? null
        : ReconciledCount * 100d / ActiveEntityCount;

    public string ImplementedProgressText => ImplementedPercentage is null
        ? "No active entities"
        : $"{ImplementedPercentage.Value:0}% implemented";

    public string ReconciledProgressText => ReconciledPercentage is null
        ? "No active entities"
        : $"{ReconciledPercentage.Value:0}% reconciled";

    public string LastActivityText => LastActivityUtc is null
        ? "No recorded activity"
        : $"Last activity {LastActivityUtc.Value.ToLocalTime():g}";

    public static TrackerProgressSummary From(
        ProgressSnapshotState state,
        int unresolvedReferenceCount,
        DateTimeOffset? lastActivityUtc) =>
        new(state, unresolvedReferenceCount, lastActivityUtc);
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
    IReadOnlyList<ProjectPortfolioSummary> Projects,
    TrackerProgressSummary Progress);

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

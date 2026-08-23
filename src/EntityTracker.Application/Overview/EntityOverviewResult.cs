using EntityTracker.Application.Ranking;

namespace EntityTracker.Application.Overview;

public sealed class EntityOverviewResult
{
    internal EntityOverviewResult(
        IEnumerable<EntityOverviewItem> items,
        IEnumerable<DependencyRankingDiagnostic> diagnostics,
        IEnumerable<EntityOverviewItem>? archivedItems = null)
    {
        Items = Array.AsReadOnly(items.ToArray());
        ArchivedItems = Array.AsReadOnly((archivedItems ?? []).ToArray());
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    public bool IsSuccess => Diagnostics.Count == 0;

    public IReadOnlyList<EntityOverviewItem> Items { get; }

    public IReadOnlyList<EntityOverviewItem> ArchivedItems { get; }

    public IReadOnlyList<DependencyRankingDiagnostic> Diagnostics { get; }
}

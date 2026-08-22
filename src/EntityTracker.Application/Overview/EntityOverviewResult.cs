using EntityTracker.Application.Ranking;

namespace EntityTracker.Application.Overview;

public sealed class EntityOverviewResult
{
    internal EntityOverviewResult(
        IEnumerable<EntityOverviewItem> items,
        IEnumerable<DependencyRankingDiagnostic> diagnostics)
    {
        Items = Array.AsReadOnly(items.ToArray());
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }

    public bool IsSuccess => Diagnostics.Count == 0;

    public IReadOnlyList<EntityOverviewItem> Items { get; }

    public IReadOnlyList<DependencyRankingDiagnostic> Diagnostics { get; }
}

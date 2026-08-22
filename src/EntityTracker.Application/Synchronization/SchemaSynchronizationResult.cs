using EntityTracker.Application.Importing;
using EntityTracker.Application.Ranking;

namespace EntityTracker.Application.Synchronization;

public sealed class SchemaSynchronizationResult
{
    private SchemaSynchronizationResult(
        SchemaSynchronizationPlan? plan,
        IReadOnlyList<ImportDiagnostic> importDiagnostics,
        IReadOnlyList<DependencyRankingDiagnostic> rankingDiagnostics)
    {
        Plan = plan;
        ImportDiagnostics = importDiagnostics;
        RankingDiagnostics = rankingDiagnostics;
    }

    public bool IsSuccess => Plan is not null;

    public SchemaSynchronizationPlan? Plan { get; }

    public IReadOnlyList<ImportDiagnostic> ImportDiagnostics { get; }

    public IReadOnlyList<DependencyRankingDiagnostic> RankingDiagnostics { get; }

    public static SchemaSynchronizationResult Success(
        SchemaSynchronizationPlan plan,
        IEnumerable<ImportDiagnostic> diagnostics) =>
        new(plan, diagnostics.ToArray(), []);

    public static SchemaSynchronizationResult Review(
        SchemaSynchronizationPlan plan,
        IEnumerable<ImportDiagnostic> importDiagnostics,
        IEnumerable<DependencyRankingDiagnostic> rankingDiagnostics) =>
        new(plan, importDiagnostics.ToArray(), rankingDiagnostics.ToArray());

    public static SchemaSynchronizationResult ImportFailure(
        IEnumerable<ImportDiagnostic> diagnostics) =>
        new(null, diagnostics.ToArray(), []);

    public static SchemaSynchronizationResult RankingFailure(
        IEnumerable<ImportDiagnostic> importDiagnostics,
        IEnumerable<DependencyRankingDiagnostic> rankingDiagnostics) =>
        new(null, importDiagnostics.ToArray(), rankingDiagnostics.ToArray());
}

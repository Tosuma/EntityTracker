namespace EntityTracker.Application.Ranking;

public sealed class DependencyRankingResult
{
    private DependencyRankingResult(
        IReadOnlyList<EntityRanking> rankings,
        IReadOnlyList<DependencyRankingDiagnostic> diagnostics)
    {
        Rankings = rankings;
        Diagnostics = diagnostics;
    }

    public bool IsSuccess => Diagnostics.Count == 0;

    public IReadOnlyList<EntityRanking> Rankings { get; }

    public IReadOnlyList<DependencyRankingDiagnostic> Diagnostics { get; }

    internal static DependencyRankingResult Success(IEnumerable<EntityRanking> rankings)
    {
        EntityRanking[] rankingArray = rankings.ToArray();
        return new DependencyRankingResult(
            Array.AsReadOnly(rankingArray),
            Array.Empty<DependencyRankingDiagnostic>());
    }

    internal static DependencyRankingResult Failure(
        IEnumerable<DependencyRankingDiagnostic> diagnostics)
    {
        DependencyRankingDiagnostic[] diagnosticArray = diagnostics.ToArray();
        return new DependencyRankingResult(
            Array.Empty<EntityRanking>(),
            Array.AsReadOnly(diagnosticArray));
    }
}

namespace EntityTracker.Application.Ranking;

public sealed class DependencyRankingResult
{
    private DependencyRankingResult(
        IReadOnlyList<EntityRanking> rankings,
        IReadOnlyList<UnrankedEntity> unrankedEntities,
        IReadOnlyList<DependencyRankingDiagnostic> diagnostics)
    {
        Rankings = rankings;
        UnrankedEntities = unrankedEntities;
        Diagnostics = diagnostics;
    }

    public bool IsSuccess => Diagnostics.Count == 0;

    public IReadOnlyList<EntityRanking> Rankings { get; }

    public IReadOnlyList<UnrankedEntity> UnrankedEntities { get; }

    public IReadOnlyList<DependencyRankingDiagnostic> Diagnostics { get; }

    internal static DependencyRankingResult Success(
        IEnumerable<EntityRanking> rankings,
        IEnumerable<UnrankedEntity> unrankedEntities)
    {
        EntityRanking[] rankingArray = rankings.ToArray();
        UnrankedEntity[] unrankedEntityArray = unrankedEntities.ToArray();
        return new DependencyRankingResult(
            Array.AsReadOnly(rankingArray),
            Array.AsReadOnly(unrankedEntityArray),
            Array.Empty<DependencyRankingDiagnostic>());
    }

    internal static DependencyRankingResult Failure(
        IEnumerable<DependencyRankingDiagnostic> diagnostics)
    {
        DependencyRankingDiagnostic[] diagnosticArray = diagnostics.ToArray();
        return new DependencyRankingResult(
            Array.Empty<EntityRanking>(),
            Array.Empty<UnrankedEntity>(),
            Array.AsReadOnly(diagnosticArray));
    }
}

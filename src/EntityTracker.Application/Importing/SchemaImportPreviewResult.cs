using EntityTracker.Application.Ranking;

namespace EntityTracker.Application.Importing;

public sealed class SchemaImportPreviewResult
{
    internal SchemaImportPreviewResult(
        IEnumerable<SchemaImportPreviewItem> items,
        IEnumerable<ImportDiagnostic> importDiagnostics,
        IEnumerable<DependencyRankingDiagnostic> rankingDiagnostics)
    {
        Items = Array.AsReadOnly(items.ToArray());
        ImportDiagnostics = Array.AsReadOnly(importDiagnostics.ToArray());
        RankingDiagnostics = Array.AsReadOnly(rankingDiagnostics.ToArray());
    }

    public bool IsSuccess => ImportDiagnostics.Count == 0 && RankingDiagnostics.Count == 0;

    public IReadOnlyList<SchemaImportPreviewItem> Items { get; }

    public IReadOnlyList<ImportDiagnostic> ImportDiagnostics { get; }

    public IReadOnlyList<DependencyRankingDiagnostic> RankingDiagnostics { get; }
}

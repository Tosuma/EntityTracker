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

    public bool IsSuccess =>
        RankingDiagnostics.Count == 0
        && !ImportDiagnostics.Any(static diagnostic =>
            diagnostic.Severity == ImportDiagnosticSeverity.Error);

    public IReadOnlyList<SchemaImportPreviewItem> Items { get; }

    public IReadOnlyList<ImportDiagnostic> ImportDiagnostics { get; }

    public IReadOnlyList<DependencyRankingDiagnostic> RankingDiagnostics { get; }
}

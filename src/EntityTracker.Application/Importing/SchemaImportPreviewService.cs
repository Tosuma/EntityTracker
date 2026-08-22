using EntityTracker.Application.Ranking;
using EntityTracker.Domain;

namespace EntityTracker.Application.Importing;

public sealed class SchemaImportPreviewService
{
    private readonly ISchemaImportFileParser _fileParser;
    private readonly DependencyRanker _dependencyRanker;

    public SchemaImportPreviewService(
        ISchemaImportFileParser fileParser,
        DependencyRanker dependencyRanker)
    {
        ArgumentNullException.ThrowIfNull(fileParser);
        ArgumentNullException.ThrowIfNull(dependencyRanker);

        _fileParser = fileParser;
        _dependencyRanker = dependencyRanker;
    }

    public async Task<SchemaImportPreviewResult> PreviewAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        SchemaImportResult importResult =
            await _fileParser.ParseAsync(filePath, cancellationToken);

        if (!importResult.IsSuccess)
        {
            return new SchemaImportPreviewResult([], importResult.Diagnostics, []);
        }

        SchemaImportCandidate candidate = importResult.Candidate!;
        Dictionary<EntitySourceKey, TrackedEntity> entitiesBySourceKey =
            candidate.Entities.ToDictionary(
                static entity => entity.SourceKey,
                static entity => new TrackedEntity(EntityId.New(), entity.SourceName));

        DependencyEdge[] edges = candidate.Dependencies
            .Select(dependency => new DependencyEdge(
                entitiesBySourceKey[dependency.DependentSourceKey].Id,
                entitiesBySourceKey[dependency.DependencySourceKey].Id))
            .ToArray();

        DependencyRankingResult rankingResult = await Task.Run(
            () => _dependencyRanker.Rank(entitiesBySourceKey.Values, edges),
            cancellationToken);

        if (!rankingResult.IsSuccess)
        {
            return new SchemaImportPreviewResult([], [], rankingResult.Diagnostics);
        }

        IReadOnlyDictionary<EntityId, ImportedEntity> importedEntitiesById =
            candidate.Entities.ToDictionary(
                entity => entitiesBySourceKey[entity.SourceKey].Id);

        Dictionary<EntitySourceKey, (int Mandatory, int Optional)> countsByEntity =
            candidate.Entities.ToDictionary(
                static entity => entity.SourceKey,
                static _ => (0, 0));

        foreach (ImportedDependency dependency in candidate.Dependencies)
        {
            (int mandatory, int optional) = countsByEntity[dependency.DependentSourceKey];
            countsByEntity[dependency.DependentSourceKey] =
                dependency.Kind == ImportedDependencyKind.Mandatory
                    ? (mandatory + 1, optional)
                    : (mandatory, optional + 1);
        }

        SchemaImportPreviewItem[] items = rankingResult.Rankings
            .Select(ranking =>
            {
                ImportedEntity entity = importedEntitiesById[ranking.EntityId];
                (int mandatory, int optional) = countsByEntity[entity.SourceKey];
                return new SchemaImportPreviewItem(
                    ranking.Rank,
                    entity.SourceName,
                    mandatory,
                    optional);
            })
            .ToArray();

        return new SchemaImportPreviewResult(items, [], []);
    }
}

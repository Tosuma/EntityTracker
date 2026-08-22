using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Synchronization;

public sealed class SchemaSynchronizationService
{
    private readonly ISchemaImportFileParser _fileParser;
    private readonly IEntityRepository _entityRepository;
    private readonly IDependencyRepository _dependencyRepository;
    private readonly SchemaSynchronizationPlanner _planner;
    private readonly ITrackedSchemaStore _store;

    public SchemaSynchronizationService(
        ISchemaImportFileParser fileParser,
        IEntityRepository entityRepository,
        IDependencyRepository dependencyRepository,
        SchemaSynchronizationPlanner planner,
        ITrackedSchemaStore store)
    {
        ArgumentNullException.ThrowIfNull(fileParser);
        ArgumentNullException.ThrowIfNull(entityRepository);
        ArgumentNullException.ThrowIfNull(dependencyRepository);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(store);

        _fileParser = fileParser;
        _entityRepository = entityRepository;
        _dependencyRepository = dependencyRepository;
        _planner = planner;
        _store = store;
    }

    public async Task<SchemaSynchronizationResult> PlanAsync(
        string filePath,
        SchemaImportMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        SchemaImportResult importResult =
            await _fileParser.ParseAsync(filePath, cancellationToken);
        if (!importResult.IsSuccess)
        {
            return SchemaSynchronizationResult.ImportFailure(importResult.Diagnostics);
        }

        Task<IReadOnlyList<TrackedEntity>> entitiesTask =
            _entityRepository.GetAllAsync(cancellationToken);
        Task<IReadOnlyList<PersistedDependency>> dependenciesTask =
            _dependencyRepository.GetAllAsync(cancellationToken);
        Task<IReadOnlyList<PersistedUnresolvedDependency>> unresolvedTask =
            _dependencyRepository.GetAllUnresolvedAsync(cancellationToken);
        await Task.WhenAll(entitiesTask, dependenciesTask, unresolvedTask);

        SchemaSynchronizationPlan plan = _planner.CreatePlan(
            importResult.Candidate!,
            mode,
            await entitiesTask,
            await dependenciesTask,
            await unresolvedTask);
        ImportDiagnostic[] applicableDiagnostics = importResult.Diagnostics
            .Where(static diagnostic => diagnostic.Code != ImportDiagnosticCode.UnknownDependency)
            .ToArray();

        return plan.CandidateRanking.IsSuccess
            ? SchemaSynchronizationResult.Success(plan, applicableDiagnostics)
            : SchemaSynchronizationResult.RankingFailure(
                applicableDiagnostics,
                plan.CandidateRanking.Diagnostics);
    }

    public Task ApplyAsync(
        SchemaSynchronizationPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.ChangeSet.HasChanges
            ? _store.ApplyAsync(plan.ChangeSet, cancellationToken)
            : Task.CompletedTask;
    }
}

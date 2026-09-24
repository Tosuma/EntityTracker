using EntityTracker.Application.Importing;
using EntityTracker.Application.ManualOverrides;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;

namespace EntityTracker.Application.Synchronization;

public sealed class SchemaSynchronizationService
{
    private readonly ISchemaImportFileParser _fileParser;
    private readonly IEntityRepository _entityRepository;
    private readonly IDependencyRepository _dependencyRepository;
    private readonly IManualDependencyOverrideRepository _overrideRepository;
    private readonly SchemaSynchronizationPlanner _planner;
    private readonly EntityDependencyEditorService _dependencyEditorService;
    private readonly ISchemaSynchronizationStore _synchronizationStore;

    public SchemaSynchronizationService(
        ISchemaImportFileParser fileParser,
        IEntityRepository entityRepository,
        IDependencyRepository dependencyRepository,
        IManualDependencyOverrideRepository overrideRepository,
        SchemaSynchronizationPlanner planner,
        EntityDependencyEditorService dependencyEditorService,
        ISchemaSynchronizationStore synchronizationStore)
    {
        ArgumentNullException.ThrowIfNull(fileParser);
        ArgumentNullException.ThrowIfNull(entityRepository);
        ArgumentNullException.ThrowIfNull(dependencyRepository);
        ArgumentNullException.ThrowIfNull(overrideRepository);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(dependencyEditorService);
        ArgumentNullException.ThrowIfNull(synchronizationStore);

        _fileParser = fileParser;
        _entityRepository = entityRepository;
        _dependencyRepository = dependencyRepository;
        _overrideRepository = overrideRepository;
        _planner = planner;
        _dependencyEditorService = dependencyEditorService;
        _synchronizationStore = synchronizationStore;
    }

    public async Task<SchemaSynchronizationResult> PlanAsync(
        TrackerId trackerId,
        string filePath,
        SchemaImportMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
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
            _entityRepository.GetAllAsync(trackerId, cancellationToken);
        Task<IReadOnlyList<PersistedDependency>> dependenciesTask =
            _dependencyRepository.GetAllAsync(trackerId, cancellationToken);
        Task<IReadOnlyList<PersistedUnresolvedDependency>> unresolvedTask =
            _dependencyRepository.GetAllUnresolvedAsync(trackerId, cancellationToken);
        Task<IReadOnlyList<ManualDependencyOverride>> overridesTask =
            _overrideRepository.GetAllAsync(trackerId, cancellationToken);
        await Task.WhenAll(entitiesTask, dependenciesTask, unresolvedTask, overridesTask);

        SchemaSynchronizationPlan plan = _planner.CreatePlan(
            trackerId,
            importResult.Candidate!,
            mode,
            await entitiesTask,
            await dependenciesTask,
            await unresolvedTask,
            await overridesTask);
        ImportDiagnostic[] applicableDiagnostics = importResult.Diagnostics
            .Where(static diagnostic => diagnostic.Code != ImportDiagnosticCode.UnknownDependency)
            .ToArray();

        return SchemaSynchronizationResult.Review(
            plan,
            applicableDiagnostics,
            plan.CandidateRanking.Diagnostics);
    }

    public EntityDependencyEditPlan PreviewDependencyEdit(
        TrackerId trackerId,
        SchemaSynchronizationPlan plan,
        EntityId ownerId,
        IEnumerable<ManualDependencyOverride> desiredOwnerOverrides)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(plan);
        EnsurePlanTracker(trackerId, plan);
        return _dependencyEditorService.CreatePlan(
            trackerId,
            ownerId,
            plan.CandidateEntities,
            plan.CandidateImportedResolvedDependencies,
            plan.CandidateImportedUnresolvedDependencies,
            plan.CandidateManualOverrides,
            desiredOwnerOverrides);
    }

    public SchemaSynchronizationPlan StageDependencyEdit(
        TrackerId trackerId,
        SchemaSynchronizationPlan plan,
        EntityDependencyEditPlan editPlan)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(plan);
        EnsurePlanTracker(trackerId, plan);
        ArgumentNullException.ThrowIfNull(editPlan);
        if (!editPlan.IsValid)
        {
            throw new InvalidOperationException(
                "Dependency edits cannot be staged while validation errors remain.");
        }

        return _planner.ReviseManualOverrides(
            plan,
            editPlan.Entity.Id,
            editPlan.DesiredOverrides);
    }

    public SchemaSynchronizationPlan StageProgressDecision(
        TrackerId trackerId,
        SchemaSynchronizationPlan plan,
        EntityId entityId,
        SynchronizationProgressDecision decision)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        EnsurePlanTracker(trackerId, plan);
        return _planner.ReviseProgressDecision(plan, entityId, decision);
    }

    public Task<SchemaImportSummary> ApplyAsync(
        TrackerId trackerId,
        SchemaSynchronizationPlan plan,
        string sourceFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackerId);
        ArgumentNullException.ThrowIfNull(plan);
        EnsurePlanTracker(trackerId, plan);
        if (!plan.CanApply)
        {
            throw new InvalidOperationException(
                "Synchronization cannot be applied until dependency errors are corrected and all progress decisions are made.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileName);
        string normalizedFileName = Path.GetFileName(sourceFileName);
        if (string.IsNullOrWhiteSpace(normalizedFileName))
        {
            throw new ArgumentException("An import source file name is required.", nameof(sourceFileName));
        }

        SchemaImportCompletion completion = new(
            normalizedFileName,
            plan.Mode,
            plan.NewEntities.Count,
            plan.ChangedEntities.Count,
            plan.ChangeSet.EntityIdsToArchive.Count,
            plan.UnchangedEntityCount,
            plan.UnresolvedEntities.Count);

        return _synchronizationStore.ApplyAsync(
            trackerId,
            plan.ChangeSet,
            completion,
            cancellationToken);
    }

    public Task<SchemaImportSummary?> GetLatestImportAsync(
        TrackerId trackerId,
        CancellationToken cancellationToken = default) =>
        _synchronizationStore.GetLatestImportAsync(trackerId, cancellationToken);

    private static void EnsurePlanTracker(TrackerId trackerId, SchemaSynchronizationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.TrackerId != trackerId)
        {
            throw new InvalidOperationException("The synchronization plan belongs to another tracker.");
        }
    }
}

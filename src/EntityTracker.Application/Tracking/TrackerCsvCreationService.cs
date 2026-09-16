using EntityTracker.Application.Importing;
using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Synchronization;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tracking;

public sealed record PreparedCsvTrackerCreation(
    Tracker Tracker,
    string SourceFileName,
    SchemaSynchronizationPlan Plan,
    IReadOnlyList<ImportDiagnostic> ImportDiagnostics);

public sealed class TrackerCsvPreparationResult
{
    private TrackerCsvPreparationResult(
        PreparedCsvTrackerCreation? prepared,
        IEnumerable<ImportDiagnostic> diagnostics)
    {
        Prepared = prepared;
        Diagnostics = diagnostics.ToArray();
    }

    public PreparedCsvTrackerCreation? Prepared { get; }

    public IReadOnlyList<ImportDiagnostic> Diagnostics { get; }

    public bool IsSuccess => Prepared is not null;

    public static TrackerCsvPreparationResult Success(
        PreparedCsvTrackerCreation prepared) => new(prepared, prepared.ImportDiagnostics);

    public static TrackerCsvPreparationResult Failure(
        IEnumerable<ImportDiagnostic> diagnostics) => new(null, diagnostics);
}

public sealed class TrackerCsvCreationService(
    IProjectRepository projectRepository,
    ISchemaImportFileParser fileParser,
    SchemaSynchronizationPlanner planner,
    IProjectTrackerStore store,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<TrackerCsvPreparationResult> PrepareAsync(
        ProjectId projectId,
        string trackerName,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        Project project = await projectRepository.GetAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("The destination project no longer exists.");
        if (project.LifecycleState != CatalogLifecycleState.Active)
        {
            throw new InvalidOperationException("A tracker cannot be created in a recycled project.");
        }

        SchemaImportResult import = await fileParser.ParseAsync(filePath, cancellationToken);
        if (!import.IsSuccess)
        {
            return TrackerCsvPreparationResult.Failure(import.Diagnostics);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();
        Tracker tracker = new(TrackerId.New(), projectId, trackerName, now, now);
        SchemaSynchronizationPlan plan = planner.CreatePlan(
            tracker.Id,
            import.Candidate!,
            SchemaImportMode.Complete,
            [],
            [],
            []);
        string sourceFileName = Path.GetFileName(filePath);
        PreparedCsvTrackerCreation prepared = new(
            tracker,
            sourceFileName,
            plan,
            import.Diagnostics
                .Where(static diagnostic => diagnostic.Code != ImportDiagnosticCode.UnknownDependency)
                .ToArray());
        return TrackerCsvPreparationResult.Success(prepared);
    }

    public async Task<Tracker> CommitAsync(
        PreparedCsvTrackerCreation prepared,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (!prepared.Plan.CanApply || prepared.Plan.TrackerId != prepared.Tracker.Id)
        {
            throw new InvalidOperationException("The reviewed tracker candidate cannot be committed.");
        }

        SchemaImportCompletion completion = new(
            prepared.SourceFileName,
            SchemaImportMode.Complete,
            prepared.Plan.NewEntities.Count,
            prepared.Plan.ChangedEntities.Count,
            prepared.Plan.ChangeSet.EntityIdsToArchive.Count,
            prepared.Plan.UnchangedEntityCount,
            prepared.Plan.UnresolvedEntities.Count);
        ProgressSnapshotState baseline =
            prepared.Plan.ChangeSet.ProgressSnapshotAfterChanges
            ?? throw new InvalidOperationException("The tracker candidate has no creation baseline.");
        await store.CreateTrackerAsync(
            new TrackerCreationState(
                prepared.Tracker,
                prepared.Plan.ChangeSet,
                baseline,
                completion),
            cancellationToken);
        return prepared.Tracker;
    }
}

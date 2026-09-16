using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Importing;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Projects;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Synchronization;
using EntityTracker.Application.Tracking;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Importing;
using EntityTracker.Infrastructure.Persistence;

namespace EntityTracker.Infrastructure.Tests.Persistence;

public sealed class ProjectTrackerOperationsTests
{
    [Fact]
    public async Task EntityState_IsIsolatedByTracker_AndCrossTrackerEdgesAreRejected()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteProjectRepository projects = new(database);
        SqliteTrackerRepository trackers = new(database);
        SqliteEntityRepository entities = new(database);
        SqliteDependencyRepository dependencies = new(database);
        SqliteTrackedStateStore stateStore = new(database);
        Tracker defaultTracker = Assert.Single(await trackers.GetAllAsync());
        TrackerManagementService service = CreateTrackerService(database);
        Tracker otherTracker = await service.CreateBlankAsync(defaultTracker.ProjectId, "Other");
        TrackedEntity first = new(EntityId.New(), defaultTracker.Id, "Shared Name");
        TrackedEntity second = new(EntityId.New(), otherTracker.Id, " shared name ");

        await stateStore.ApplyAsync(defaultTracker.Id, Add(first));
        await stateStore.ApplyAsync(otherTracker.Id, Add(second));

        Assert.Equal(first.Id, Assert.Single(await entities.GetAllAsync(defaultTracker.Id)).Id);
        Assert.Equal(second.Id, Assert.Single(await entities.GetAllAsync(otherTracker.Id)).Id);
        Assert.Null(await entities.GetAsync(defaultTracker.Id, second.Id));
        Assert.Null(await entities.GetAsync(otherTracker.Id, first.Id));

        TrackedEntity duplicate = new(EntityId.New(), defaultTracker.Id, " SHARED NAME ");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            stateStore.ApplyAsync(defaultTracker.Id, Add(duplicate)));
        Assert.Equal(first.Id, Assert.Single(await entities.GetAllAsync(defaultTracker.Id)).Id);

        TrackedStateChangeSet crossTrackerChange = new(
            [],
            [],
            [],
            [first.Id],
            [new PersistedDependency(
                new DependencyEdge(first.Id, second.Id),
                ImportedDependencyKind.Mandatory)],
            []);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            stateStore.ApplyAsync(defaultTracker.Id, crossTrackerChange));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dependencies.SaveAsync(
                defaultTracker.Id,
                new PersistedDependency(
                    new DependencyEdge(first.Id, second.Id),
                    ImportedDependencyKind.Mandatory)));
        Assert.Empty(await dependencies.GetAllAsync(defaultTracker.Id));
        Assert.Empty(await dependencies.GetAllAsync(otherTracker.Id));

        Assert.Single(await projects.GetAllAsync());
    }

    [Fact]
    public async Task CopyAsync_RemapsActiveStructure_ResetsExecutionData_AndIsIndependent()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteTrackerRepository trackers = new(database);
        SqliteEntityRepository entities = new(database);
        SqliteDependencyRepository dependencies = new(database);
        SqliteManualDependencyOverrideRepository overrides = new(database);
        SqliteTrackedStateStore stateStore = new(database);
        SqliteProgressHistoryRepository history = new(database);
        Tracker sourceTracker = Assert.Single(await trackers.GetAllAsync());
        TrackedEntity owner = new(
            EntityId.New(),
            sourceTracker.Id,
            "Owner",
            DevelopmentStatus.InProgress,
            "Source notes",
            requestedPriority: 3,
            responsibleDeveloper: "Ada",
            groupName: "Core");
        TrackedEntity target = new(
            EntityId.New(),
            sourceTracker.Id,
            "Target",
            DevelopmentStatus.Reconciled,
            "Done",
            requestedPriority: 5,
            responsibleDeveloper: "Grace",
            groupName: "Core");
        TrackedEntity archived = new(
            EntityId.New(),
            sourceTracker.Id,
            "Legacy",
            DevelopmentStatus.DevelopmentCompleted,
            "Archive notes",
            EntityLifecycleState.Archived,
            requestedPriority: 2,
            responsibleDeveloper: "Linus",
            groupName: "Legacy");
        PersistedDependency ownerToTarget = new(
            new DependencyEdge(owner.Id, target.Id),
            ImportedDependencyKind.Mandatory);
        PersistedDependency ownerToArchived = new(
            new DependencyEdge(owner.Id, archived.Id),
            ImportedDependencyKind.Optional);
        PersistedUnresolvedDependency external = new(
            new UnresolvedDependency(owner.Id, "External"),
            ImportedDependencyKind.Optional);
        ManualDependencyOverride[] sourceOverrides =
        [
            new(owner.Id, "Manual target", ManualDependencyOverrideAction.Add),
            new(target.Id, "Suppressed target", ManualDependencyOverrideAction.Suppress)
        ];
        TrackedStateChangeSet sourceState = new(
            [owner, target, archived],
            [],
            [],
            [owner.Id],
            [ownerToTarget, ownerToArchived],
            [external],
            [owner.Id, target.Id],
            sourceOverrides,
            progressSnapshotAfterChanges: new ProgressSnapshotState(0, 0, 1, 0, 0, 1));
        await stateStore.ApplyAsync(sourceTracker.Id, sourceState);

        Project destinationProject = await new ProjectManagementService(
            new SqliteProjectRepository(database),
            new SqliteProjectTrackerStore(database)).CreateAsync("Destination");
        Tracker copy = await CreateTrackerService(database).CopyAsync(
            sourceTracker.Id,
            destinationProject.Id,
            "Copy");

        Assert.Equal(sourceTracker.Id, copy.CopiedFromTrackerId);
        Assert.Equal(destinationProject.Id, copy.ProjectId);
        TrackedEntity[] copiedEntities = (await entities.GetAllAsync(copy.Id)).ToArray();
        Assert.Equal(2, copiedEntities.Length);
        Assert.DoesNotContain(copiedEntities, entity =>
            entity.Id == owner.Id || entity.Id == target.Id || entity.Id == archived.Id);
        TrackedEntity copiedOwner = Assert.Single(copiedEntities, entity => entity.SourceName == "Owner");
        TrackedEntity copiedTarget = Assert.Single(copiedEntities, entity => entity.SourceName == "Target");
        Assert.All(copiedEntities, entity =>
        {
            Assert.Equal(copy.Id, entity.TrackerId);
            Assert.Equal(EntityProvenance.Copied, entity.Provenance);
            Assert.Equal(DevelopmentStatus.NotStarted, entity.Status);
            Assert.Equal(string.Empty, entity.Notes);
            Assert.Equal(string.Empty, entity.ResponsibleDeveloper);
        });
        Assert.Equal(3, copiedOwner.RequestedPriority);
        Assert.Equal(5, copiedTarget.RequestedPriority);
        Assert.Equal("Core", copiedOwner.GroupName);
        Assert.Equal("Core", copiedTarget.GroupName);

        PersistedDependency copiedResolved = Assert.Single(await dependencies.GetAllAsync(copy.Id));
        Assert.Equal(copiedOwner.Id, copiedResolved.Edge.DependentEntityId);
        Assert.Equal(copiedTarget.Id, copiedResolved.Edge.DependencyEntityId);
        Assert.Equal(ImportedDependencyKind.Mandatory, copiedResolved.Kind);
        IReadOnlyList<PersistedUnresolvedDependency> copiedUnresolved =
            await dependencies.GetAllUnresolvedAsync(copy.Id);
        Assert.Equal(2, copiedUnresolved.Count);
        Assert.Contains(copiedUnresolved, item =>
            item.Dependency.DependentEntityId == copiedOwner.Id &&
            item.Dependency.DependencySourceName == "External" &&
            item.Kind == ImportedDependencyKind.Optional);
        Assert.Contains(copiedUnresolved, item =>
            item.Dependency.DependentEntityId == copiedOwner.Id &&
            item.Dependency.DependencySourceName == "Legacy" &&
            item.Kind == ImportedDependencyKind.Optional);
        IReadOnlyList<ManualDependencyOverride> copiedOverrides = await overrides.GetAllAsync(copy.Id);
        Assert.Equal(2, copiedOverrides.Count);
        Assert.All(copiedOverrides, item =>
            Assert.True(
                item.DependentEntityId == copiedOwner.Id ||
                item.DependentEntityId == copiedTarget.Id));

        IReadOnlyList<EntityStatusHistoryEntry> copiedHistory =
            await history.GetStatusHistoryAsync(copy.Id);
        Assert.Equal(2, copiedHistory.Count);
        Assert.All(copiedHistory, entry =>
        {
            Assert.Equal(StatusHistoryEntryKind.Baseline, entry.Kind);
            Assert.Equal(DevelopmentStatus.NotStarted, entry.NewStatus);
        });
        Assert.Single(await history.GetProgressSnapshotsAsync(copy.Id));

        owner.ChangeStatus(DevelopmentStatus.ReworkNeeded);
        owner.ChangeNotes("Changed after copy");
        await stateStore.ApplyAsync(
            sourceTracker.Id,
            new TrackedStateChangeSet(
                [], [], [], [], [], [],
                entitiesWithProgressToUpdate: [owner]));
        TrackedEntity unchangedCopy = Assert.IsType<TrackedEntity>(
            await entities.GetAsync(copy.Id, copiedOwner.Id));
        Assert.Equal(DevelopmentStatus.NotStarted, unchangedCopy.Status);
        Assert.Equal(string.Empty, unchangedCopy.Notes);
    }

    [Fact]
    public async Task CatalogLifecycle_ReservesNames_CascadesVisibility_AndPurgesHierarchy()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteProjectRepository projects = new(database);
        SqliteTrackerRepository trackers = new(database);
        SqliteEntityRepository entities = new(database);
        SqliteTrackedStateStore stateStore = new(database);
        ProjectManagementService projectService = new(projects, new SqliteProjectTrackerStore(database));
        TrackerManagementService trackerService = CreateTrackerService(database);
        CompatibilityTrackerResolver compatibility = new(projects, trackers);
        Tracker defaultTracker = await compatibility.ResolveAsync();
        Project project = await projectService.CreateAsync("Alpha");
        Tracker tracker = await trackerService.CreateBlankAsync(project.Id, "Release");
        TrackedEntity entity = new(EntityId.New(), tracker.Id, "Owned");
        await stateStore.ApplyAsync(tracker.Id, Add(entity));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            projectService.CreateAsync(" alpha "));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            trackerService.CreateBlankAsync(project.Id, " release "));
        await Assert.ThrowsAsync<InvalidOperationException>(() => compatibility.ResolveAsync());

        await projectService.RecycleAsync(project.Id);
        Assert.Equal(defaultTracker.Id, (await compatibility.ResolveAsync()).Id);
        Assert.Equal(
            CatalogLifecycleState.Active,
            Assert.IsType<Tracker>(await trackers.GetAsync(tracker.Id)).LifecycleState);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            projectService.CreateAsync("ALPHA"));

        await projectService.RestoreAsync(project.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => compatibility.ResolveAsync());
        await trackerService.RecycleAsync(tracker.Id);
        Assert.Equal(defaultTracker.Id, (await compatibility.ResolveAsync()).Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            trackerService.CreateBlankAsync(project.Id, "RELEASE"));
        await trackerService.RestoreAsync(tracker.Id);
        await trackerService.RecycleAsync(tracker.Id);
        await trackerService.PurgeAsync(new PurgeTrackerRequest(tracker.Id));
        Assert.Null(await trackers.GetAsync(tracker.Id));
        Assert.Empty(await entities.GetAllAsync(tracker.Id));
        Tracker replacement = await trackerService.CreateBlankAsync(project.Id, "release");
        Assert.NotEqual(tracker.Id, replacement.Id);

        await projectService.RecycleAsync(project.Id);
        await projectService.PurgeAsync(new PurgeProjectRequest(project.Id));
        Assert.Null(await projects.GetAsync(project.Id));
        Assert.Null(await trackers.GetAsync(replacement.Id));
        Project reused = await projectService.CreateAsync("ALPHA");
        Assert.NotEqual(project.Id, reused.Id);
    }

    [Fact]
    public async Task CsvCreation_PreparesWithoutWrites_AndCommitsAllStateAtomically()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        SqliteProjectRepository projects = new(database);
        SqliteTrackerRepository trackers = new(database);
        SqliteEntityRepository entities = new(database);
        SqliteDependencyRepository dependencies = new(database);
        SqliteTrackedStateStore stateStore = new(database);
        Tracker defaultTracker = Assert.Single(await trackers.GetAllAsync());
        TrackerCsvCreationService service = new(
            projects,
            new CsvSchemaImportFileParser(new CsvSchemaImportParser()),
            new SchemaSynchronizationPlanner(new DependencyRanker()),
            new SqliteProjectTrackerStore(database));
        string validPath = FixturePath("valid", "extracted_dependencies.csv");
        string invalidPath = FixturePath("invalid", "malformed.csv");

        TrackerCsvPreparationResult failed = await service.PrepareAsync(
            defaultTracker.ProjectId,
            "Failed",
            invalidPath);
        Assert.False(failed.IsSuccess);
        Assert.Single(await trackers.GetAllAsync());

        using (CancellationTokenSource cancellation = new())
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.PrepareAsync(
                    defaultTracker.ProjectId,
                    "Cancelled",
                    validPath,
                    cancellation.Token));
        }
        Assert.Single(await trackers.GetAllAsync());

        TrackerCsvPreparationResult result = await service.PrepareAsync(
            defaultTracker.ProjectId,
            "Imported",
            validPath);
        Assert.True(result.IsSuccess);
        Assert.Single(await trackers.GetAllAsync());

        TrackerCsvPreparationResult cancelledCommit = await service.PrepareAsync(
            defaultTracker.ProjectId,
            "Cancelled commit",
            validPath);
        using (CancellationTokenSource cancellation = new())
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.CommitAsync(cancelledCommit.Prepared!, cancellation.Token));
        }
        Assert.Single(await trackers.GetAllAsync());
        Assert.Empty(await entities.GetAllAsync(cancelledCommit.Prepared!.Tracker.Id));

        Tracker imported = await service.CommitAsync(result.Prepared!);
        Assert.Equal(6, (await entities.GetAllAsync(imported.Id)).Count);
        Assert.Equal(6, (await dependencies.GetAllAsync(imported.Id)).Count);
        Assert.Empty(await dependencies.GetAllUnresolvedAsync(imported.Id));
        Assert.Equal(
            "extracted_dependencies.csv",
            Assert.IsType<SchemaImportSummary>(
                await stateStore.GetLatestImportAsync(imported.Id)).SourceFileName);

        TrackerCsvPreparationResult duplicate = await service.PrepareAsync(
            defaultTracker.ProjectId,
            " imported ",
            validPath);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CommitAsync(duplicate.Prepared!));
        Assert.Equal(2, (await trackers.GetAllAsync()).Count);
        Assert.Empty(await entities.GetAllAsync(duplicate.Prepared!.Tracker.Id));
    }

    private static TrackerManagementService CreateTrackerService(SqliteDatabase database) => new(
        new SqliteProjectRepository(database),
        new SqliteTrackerRepository(database),
        new SqliteEntityRepository(database),
        new SqliteDependencyRepository(database),
        new SqliteManualDependencyOverrideRepository(database),
        new SqliteProjectTrackerStore(database),
        new EffectiveDependencyResolver(),
        new ProgressSnapshotCalculator());

    private static TrackedStateChangeSet Add(TrackedEntity entity) => new(
        [entity],
        [],
        [],
        [],
        [],
        []);

    private static string FixturePath(params string[] parts) => Path.Combine(
        [AppContext.BaseDirectory, "Fixtures", .. parts]);
}

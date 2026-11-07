using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Projects;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Schemas;
using EntityTracker.Application.Tracking;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Persistence;

namespace EntityTracker.Infrastructure.Tests.Persistence;

public sealed class SchemaConversionTests
{
    [Fact]
    public async Task Converting_MakesSchemasLinksCopiesAndRecyclesTheSource()
    {
        await using TemporarySqliteFile file = new();
        Fixture fixture = await Fixture.CreateAsync(file);
        Project other = await new ProjectManagementService(new SqliteProjectRepository(fixture.Database),
            new SqliteProjectTrackerStore(fixture.Database)).CreateAsync("Other project");
        Tracker source = await fixture.SourceAsync();
        Tracker copy = await fixture.Trackers.CopyAsync(source.Id, fixture.Project.Id, "Team A");
        Tracker remote = await fixture.Trackers.CopyAsync(source.Id, other.Id, "Team B");

        // Team A works on Order; afterwards the source renames Customer and adds Invoice, which Team A
        // never synced.
        await fixture.EditAsync(copy.Id, entities =>
        {
            TrackedEntity order = entities.Single(e => e.SourceName == "Order");
            order.ChangeStatus(DevelopmentStatus.InProgress);
            return new TrackedStateChangeSet([], [], [], entitiesWithProgressToUpdate: [order]);
        });
        await fixture.EditAsync(source.Id, entities =>
        {
            TrackedEntity customer = entities.Single(e => e.SourceName == "Customer");
            TrackedEntity order = entities.Single(e => e.SourceName == "Order");
            customer.ChangeSourceName("Client");
            TrackedEntity invoice = new(EntityId.New(), source.Id, "Invoice");
            return new TrackedStateChangeSet([invoice], [customer], [], [invoice.Id],
                [new EntityDependency(invoice.Id, order.Id)]);
        });

        SchemaConversionPreview preview = await fixture.Conversion.PreviewAsync(source.Id);
        Assert.Equal(3, preview.EntityCount);
        Assert.Equal(1, preview.PlaceholderCount);
        Assert.Equal(1, preview.ArchivedCount);
        Assert.Equal(["Team A"], preview.SourceProject.Copies.Select(t => t.Name));
        SchemaConversionProject otherProject = Assert.Single(preview.OtherProjects);
        Assert.Equal("Other project", otherProject.Project.Name);
        Assert.Equal(["Team B"], otherProject.Copies.Select(t => t.Name));

        SchemaId schemaId = await fixture.Conversion.ConvertAsync(source.Id);

        // The schema has the active entities and the placeholder, but not the archived entity.
        SchemaContent schema = (await new SqliteSchemaStore(fixture.Database).GetContentAsync(schemaId))!;
        Assert.Equal("Core", schema.Schema.Name);
        Assert.Equal(["Client", "Invoice", "Legacy", "Order"], schema.Entities.Select(e => e.Name).Order());
        Assert.Equal("Sales", schema.Entities.Single(e => e.Name == "Order").GroupName);
        Assert.Equal(3, schema.Dependencies.Count);

        SqliteTrackerRepository trackers = new(fixture.Database);
        Assert.Equal(CatalogLifecycleState.Recycled, (await trackers.GetAsync(source.Id))!.LifecycleState);
        Tracker linked = (await trackers.GetAsync(copy.Id))!;
        Assert.Equal(schemaId, linked.SchemaId);
        Assert.Null(linked.CopiedFromTrackerId);
        Assert.Null(await new SqliteTrackedStateStore(fixture.Database).ReadBaselineAsync(copy.Id));
        Assert.Equal(DevelopmentStatus.InProgress,
            (await fixture.EntitiesAsync(copy.Id)).Single(e => e.SourceName == "Order").Status);

        // The other project got its own copy of the schema, and Team B is linked to it.
        SchemaSummary otherSchema = Assert.Single(await new SqliteSchemaStore(fixture.Database).GetByProjectAsync(other.Id));
        Assert.Equal("Core", otherSchema.Schema.Name);
        Assert.Equal(4, otherSchema.EntityCount);
        Assert.Equal(otherSchema.Schema.Id, (await trackers.GetAsync(remote.Id))!.SchemaId);

        // The source's changes Team A hadn't synced show up in the first push; its progress stays.
        SchemaPushReview review = await fixture.Push.ReviewPushAsync(schemaId);
        TrackerPushPlan plan = Assert.Single(review.Trackers);
        Assert.Contains("Renamed Customer to Client", plan.AutomaticChanges);
        Assert.Contains("Added Invoice", plan.AutomaticChanges);
        Assert.Empty(review.Conflicts);
        await fixture.Push.ApplyPushAsync(review);
        TrackedEntity[] teamA = await fixture.EntitiesAsync(copy.Id);
        Assert.Contains(teamA, e => e.SourceName == "Client");
        Assert.Equal(DevelopmentStatus.InProgress, teamA.Single(e => e.SourceName == "Order").Status);

        // Restoring the source brings it back as an independent tracker.
        await fixture.Trackers.RestoreAsync(source.Id);
        Tracker restored = (await trackers.GetAsync(source.Id))!;
        Assert.Equal(CatalogLifecycleState.Active, restored.LifecycleState);
        Assert.Null(restored.SchemaId);
    }

    [Fact]
    public async Task Converting_NamesTheSchemaApartFromAnExistingOneAndRefusesALinkedTracker()
    {
        await using TemporarySqliteFile file = new();
        Fixture fixture = await Fixture.CreateAsync(file);
        Tracker source = await fixture.SourceAsync();
        await new SchemaManagementService(new SqliteProjectRepository(fixture.Database),
            new SqliteSchemaStore(fixture.Database)).CreateAsync(fixture.Project.Id, "Core");

        SchemaConversionPreview preview = await fixture.Conversion.PreviewAsync(source.Id);
        Assert.Equal("Core (2)", preview.SourceProject.SchemaName);
        Assert.Empty(preview.SourceProject.Copies);
        SchemaId schemaId = await fixture.Conversion.ConvertAsync(source.Id);

        Tracker fromSchema = await fixture.Push.CreateTrackerAsync(schemaId, "Team C");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Conversion.PreviewAsync(fromSchema.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Conversion.ConvertAsync(source.Id));
    }

    [Fact]
    public async Task PurgingASource_ClearsItsCopiesDormantLinks()
    {
        await using TemporarySqliteFile file = new();
        Fixture fixture = await Fixture.CreateAsync(file);
        Tracker source = await fixture.SourceAsync();
        Tracker copy = await fixture.Trackers.CopyAsync(source.Id, fixture.Project.Id, "Team A");
        SqliteTrackerRepository trackers = new(fixture.Database);
        SqliteTrackedStateStore state = new(fixture.Database);
        Assert.Equal(source.Id, (await trackers.GetAsync(copy.Id))!.CopiedFromTrackerId);
        Assert.NotNull(await state.ReadBaselineAsync(copy.Id));

        await fixture.Trackers.RecycleAsync(source.Id);
        // While the source is only recycled, the link stays, so it can still be converted later.
        Assert.Equal(source.Id, (await trackers.GetAsync(copy.Id))!.CopiedFromTrackerId);
        await fixture.Trackers.PurgeAsync(new PurgeTrackerRequest(source.Id));

        Assert.Null((await trackers.GetAsync(copy.Id))!.CopiedFromTrackerId);
        Assert.Null(await state.ReadBaselineAsync(copy.Id));
        Assert.Equal(2, (await fixture.EntitiesAsync(copy.Id)).Count(e => !e.IsPlaceholder));
    }

    private sealed class Fixture
    {
        private Fixture(SqliteDatabase database, Project project)
        {
            Database = database;
            Project = project;
            Trackers = new TrackerManagementService(new SqliteProjectRepository(database),
                new SqliteTrackerRepository(database), new SqliteEntityRepository(database),
                new SqliteDependencyRepository(database), new SqliteProjectTrackerStore(database),
                new EffectiveDependencyResolver(), new ProgressSnapshotCalculator());
            Conversion = new SchemaConversionService(new SqliteProjectRepository(database),
                new SqliteTrackerRepository(database), new SqliteEntityRepository(database),
                new SqliteDependencyRepository(database), new SqliteSchemaStore(database),
                new SqliteTrackedStateStore(database), new SqliteSchemaLinkStore(database));
            Push = new SchemaTrackerService(new SqliteProjectRepository(database), new SqliteTrackerRepository(database),
                new SqliteEntityRepository(database), new SqliteDependencyRepository(database),
                new SqliteSchemaStore(database), new SqliteSchemaLinkStore(database), new SqliteProjectTrackerStore(database),
                new EffectiveDependencyResolver(), new ProgressSnapshotCalculator(), new DependencyRanker());
        }

        public SqliteDatabase Database { get; }
        public Project Project { get; }
        public TrackerManagementService Trackers { get; }
        public SchemaConversionService Conversion { get; }
        public SchemaTrackerService Push { get; }

        public static async Task<Fixture> CreateAsync(TemporarySqliteFile file)
        {
            SqliteDatabase database = new(file.DatabasePath);
            await database.InitializeAsync();
            Project project = await new ProjectManagementService(
                new SqliteProjectRepository(database), new SqliteProjectTrackerStore(database))
                .CreateAsync("Conversion project");
            return new Fixture(database, project);
        }

        /// <summary>
        /// "Core": Order (Sales) depends on Customer and on Legacy, a name no entity has; Old is archived.
        /// </summary>
        public async Task<Tracker> SourceAsync()
        {
            Tracker tracker = await Trackers.CreateBlankAsync(Project.Id, "Core");
            TrackedEntity order = new(EntityId.New(), tracker.Id, "Order", groupName: "Sales");
            TrackedEntity customer = new(EntityId.New(), tracker.Id, "Customer");
            TrackedEntity old = new(EntityId.New(), tracker.Id, "Old");
            TrackedEntity legacy = TrackedEntity.Placeholder(tracker.Id, "Legacy");
            await new SqliteTrackedStateStore(Database).ApplyAsync(tracker.Id, new TrackedStateChangeSet(
                [order, customer, old, legacy], [], [], [order.Id],
                [new EntityDependency(order.Id, customer.Id), new EntityDependency(order.Id, legacy.Id)]));
            await new SqliteTrackedStateStore(Database).ApplyAsync(tracker.Id,
                new TrackedStateChangeSet([], [], [old.Id]));
            return tracker;
        }

        public async Task<TrackedEntity[]> EntitiesAsync(TrackerId trackerId) =>
            (await new SqliteEntityRepository(Database).GetAllAsync(trackerId)).ToArray();

        public async Task EditAsync(TrackerId trackerId, Func<TrackedEntity[], TrackedStateChangeSet> change) =>
            await new SqliteTrackedStateStore(Database).ApplyAsync(trackerId, change(await EntitiesAsync(trackerId)));
    }
}

using EntityTracker.Application.Dependencies;
using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Application.Projects;
using EntityTracker.Application.Ranking;
using EntityTracker.Application.Schemas;
using EntityTracker.Application.Snapshots;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Persistence;
using EntityTracker.Infrastructure.Snapshots;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Tests.Persistence;

public sealed class SchemaTrackerTests
{
    [Fact]
    public async Task CreatingFromSchema_GivesTheSchemasEntitiesNotStartedAndLinksThem()
    {
        await using TemporarySqliteFile file = new();
        Fixture fixture = await Fixture.CreateAsync(file);
        Schema schema = await fixture.SchemaAsync("Model", ("Order", "Sales", ["Customer"]));

        Tracker tracker = await fixture.Trackers.CreateTrackerAsync(schema.Id, "Team A");

        Tracker stored = Assert.IsType<Tracker>(await new SqliteTrackerRepository(fixture.Database).GetAsync(tracker.Id));
        Assert.Equal(schema.Id, stored.SchemaId);
        TrackedEntity[] entities = await fixture.EntitiesAsync(tracker.Id);
        Assert.Equal(["Customer", "Order"], entities.Select(e => e.SourceName).Order());
        Assert.All(entities, entity => Assert.Equal(DevelopmentStatus.NotStarted, entity.Status));
        Assert.Equal("Sales", entities.Single(e => e.SourceName == "Order").GroupName);
        EntityDependency dependency = Assert.Single(await fixture.DependenciesAsync(tracker.Id));
        Assert.Equal(entities.Single(e => e.SourceName == "Customer").Id, dependency.DependencyEntityId);

        TrackerSchemaLink link = Assert.IsType<TrackerSchemaLink>(await fixture.Links.GetLinkAsync(tracker.Id));
        Assert.Equal(2, link.Entities.Count);
        Assert.All(link.Entities, entity => Assert.Contains(entities, e => e.Id == entity.EntityId));

        SchemaPendingChanges pending = await fixture.Trackers.GetPendingChangesAsync(schema.Id);
        Assert.False(pending.HasChanges);
        Assert.Equal(1, pending.LinkedTrackerCount);
        ProjectDashboard dashboard = Assert.IsType<ProjectDashboard>(await fixture.PortfolioAsync());
        Assert.Equal("Schema: Model", Assert.Single(dashboard.Trackers).SchemaText);
    }

    [Fact]
    public async Task Push_AppliesSchemaChangesKeepsTrackerChangesAndAsksOnlyAboutConflicts()
    {
        await using TemporarySqliteFile file = new();
        Fixture fixture = await Fixture.CreateAsync(file);
        Schema schema = await fixture.SchemaAsync("Model", ("Order", "Sales", ["Customer"]));
        Tracker a = await fixture.Trackers.CreateTrackerAsync(schema.Id, "Team A");
        Tracker b = await fixture.Trackers.CreateTrackerAsync(schema.Id, "Team B");

        // Team A works on Order, regroups it and renames Customer.
        await fixture.EditAsync(a.Id, entities =>
        {
            TrackedEntity order = entities.Single(e => e.SourceName == "Order");
            TrackedEntity customer = entities.Single(e => e.SourceName == "Customer");
            order.ChangeStatus(DevelopmentStatus.InProgress);
            order.ChangeNotes("Started");
            order.ChangeRequestedPriority(2);
            order.ChangeGroupName("Billing");
            customer.ChangeSourceName("Client");
            return new TrackedStateChangeSet([], [customer], [], entitiesWithProgressToUpdate: [order],
                entitiesWithRequestedPriorityToUpdate: [order], entitiesWithGroupNameToUpdate: [order]);
        });

        // The schema renames and regroups Order, drops its dependency and adds Invoice.
        SchemaContent content = await fixture.Editing.GetContentAsync(schema.Id);
        SchemaEntity schemaOrder = content.Entities.Single(e => e.Name == "Order");
        Assert.True((await fixture.Editing.SaveEntityAsync(schema.Id,
            new SchemaEntityEdit(schemaOrder.Id, "Sales order", "Commerce", []))).Succeeded);
        Assert.True((await fixture.Editing.SaveEntityAsync(schema.Id,
            new SchemaEntityEdit(null, "Invoice", "Commerce", ["Sales order"]))).Succeeded);

        SchemaPendingChanges pending = await fixture.Trackers.GetPendingChangesAsync(schema.Id);
        Assert.Contains("Renamed Order to Sales order", pending.Changes);
        Assert.Contains("Added Invoice", pending.Changes);
        Assert.Contains("Sales order no longer depends on Customer", pending.Changes);
        Assert.Equal(2, pending.TrackersBehindCount);

        SchemaPushReview review = await fixture.Trackers.ReviewPushAsync(schema.Id);
        Assert.Equal(["Team A", "Team B"], review.Trackers.Select(t => t.Tracker.Name));
        // Only Team A changed Order's group too; everything else applies without asking.
        SchemaPushConflict conflict = Assert.Single(review.Conflicts);
        Assert.Equal(a.Id, conflict.TrackerId);
        Assert.Equal(SchemaPushConflictKind.Group, conflict.Kind);
        Assert.Contains("Renamed Order to Sales order", review.Trackers[0].AutomaticChanges);
        Assert.False(review.CanApply);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Trackers.ApplyPushAsync(review));
        conflict.Choice = SchemaPushChoice.Tracker;
        await fixture.Trackers.ApplyPushAsync(review);

        TrackedEntity[] teamA = await fixture.EntitiesAsync(a.Id);
        TrackedEntity orderA = teamA.Single(e => e.SourceName == "Sales order");
        Assert.Equal("Billing", orderA.GroupName);
        Assert.Equal(DevelopmentStatus.InProgress, orderA.Status);
        Assert.Equal("Started", orderA.Notes);
        Assert.Equal(2, orderA.RequestedPriority);
        Assert.Contains(teamA, e => e.SourceName == "Client");
        TrackedEntity invoice = teamA.Single(e => e.SourceName == "Invoice");
        Assert.Equal(DevelopmentStatus.NotStarted, invoice.Status);
        EntityDependency[] dependenciesA = await fixture.DependenciesAsync(a.Id);
        Assert.Equal(new EntityDependency(invoice.Id, orderA.Id), Assert.Single(dependenciesA));
        Assert.Equal("Commerce", (await fixture.EntitiesAsync(b.Id)).Single(e => e.SourceName == "Sales order").GroupName);

        // A kept difference isn't raised again until the schema changes it again.
        Assert.False((await fixture.Trackers.GetPendingChangesAsync(schema.Id)).HasChanges);
        Assert.Empty((await fixture.Trackers.ReviewPushAsync(schema.Id)).Conflicts);
        Assert.True((await fixture.Editing.SaveEntityAsync(schema.Id,
            new SchemaEntityEdit(schemaOrder.Id, "Sales order", "Retail", []))).Succeeded);
        review = await fixture.Trackers.ReviewPushAsync(schema.Id);
        Assert.Equal("Use group Retail", Assert.Single(review.Conflicts).UseSchemaLabel);
    }

    [Fact]
    public async Task Push_ArchivesRemovedEntitiesAndLinksAddedOnesByName()
    {
        await using TemporarySqliteFile file = new();
        Fixture fixture = await Fixture.CreateAsync(file);
        Schema schema = await fixture.SchemaAsync("Model",
            ("Order", "", ["Customer"]), ("Payment", "", []));
        Tracker tracker = await fixture.Trackers.CreateTrackerAsync(schema.Id, "Team A");
        await fixture.EditAsync(tracker.Id, entities =>
        {
            TrackedEntity payment = entities.Single(e => e.SourceName == "Payment");
            payment.ChangeGroupName("Money");
            payment.ChangeStatus(DevelopmentStatus.InProgress);
            TrackedEntity product = new(EntityId.New(), tracker.Id, "Product", DevelopmentStatus.InProgress);
            return new TrackedStateChangeSet([product], [], [], entitiesWithGroupNameToUpdate: [payment],
                entitiesWithProgressToUpdate: [payment]);
        });

        SchemaContent content = await fixture.Editing.GetContentAsync(schema.Id);
        await fixture.Editing.RemoveEntityAsync(schema.Id, content.Entities.Single(e => e.Name == "Customer").Id);
        await fixture.Editing.RemoveEntityAsync(schema.Id, content.Entities.Single(e => e.Name == "Payment").Id);
        Assert.True((await fixture.Editing.SaveEntityAsync(schema.Id,
            new SchemaEntityEdit(null, "product", "", []))).Succeeded);

        SchemaPushReview review = await fixture.Trackers.ReviewPushAsync(schema.Id);
        TrackerPushPlan plan = Assert.Single(review.Trackers);
        Assert.Contains("Archived Customer, which was removed from the schema", plan.AutomaticChanges);
        Assert.Contains("Linked Product to the schema's product", plan.AutomaticChanges);
        // Payment was changed in the tracker, so its removal is asked about; the name's case too.
        Assert.Equal([SchemaPushConflictKind.Name, SchemaPushConflictKind.Removed],
            review.Conflicts.Select(c => c.Kind).Order());
        review.Conflicts.Single(c => c.Kind == SchemaPushConflictKind.Removed).Choice = SchemaPushChoice.Tracker;
        review.Conflicts.Single(c => c.Kind == SchemaPushConflictKind.Name).Choice = SchemaPushChoice.Tracker;
        await fixture.Trackers.ApplyPushAsync(review);

        TrackedEntity[] entities = await fixture.EntitiesAsync(tracker.Id);
        Assert.Equal(EntityLifecycleState.Archived, entities.Single(e => e.SourceName == "Customer").LifecycleState);
        Assert.Equal(EntityLifecycleState.Active, entities.Single(e => e.SourceName == "Payment").LifecycleState);
        TrackedEntity product = entities.Single(e => e.SourceName == "Product");
        Assert.Equal(DevelopmentStatus.InProgress, product.Status);
        TrackerSchemaLink link = Assert.IsType<TrackerSchemaLink>(await fixture.Links.GetLinkAsync(tracker.Id));
        Assert.Equal(["Order", "product"], link.Entities.Select(e => e.Name).Order());
        Assert.Contains(link.Entities, e => e.EntityId == product.Id);
        Assert.DoesNotContain(link.Entities, e => e.EntityId == entities.Single(x => x.SourceName == "Payment").Id);
    }

    [Fact]
    public async Task Push_IsRefusedWithoutWritingWhenTheSchemaOrATrackerChangedAfterTheReview()
    {
        await using TemporarySqliteFile file = new();
        Fixture fixture = await Fixture.CreateAsync(file);
        Schema schema = await fixture.SchemaAsync("Model", ("Order", "", []));
        Tracker tracker = await fixture.Trackers.CreateTrackerAsync(schema.Id, "Team A");
        Assert.True((await fixture.Editing.SaveEntityAsync(schema.Id, new SchemaEntityEdit(null, "Invoice", "", [])))
            .Succeeded);

        SchemaPushReview review = await fixture.Trackers.ReviewPushAsync(schema.Id);
        await fixture.EditAsync(tracker.Id, entities =>
        {
            TrackedEntity order = entities.Single();
            order.ChangeStatus(DevelopmentStatus.InProgress);
            return new TrackedStateChangeSet([], [], [], entitiesWithProgressToUpdate: [order]);
        });
        InvalidOperationException stale = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Trackers.ApplyPushAsync(review));
        Assert.Contains("Team A changed", stale.Message);
        Assert.Single(await fixture.EntitiesAsync(tracker.Id));

        review = await fixture.Trackers.ReviewPushAsync(schema.Id);
        Assert.True((await fixture.Editing.SaveEntityAsync(schema.Id, new SchemaEntityEdit(null, "Payment", "", [])))
            .Succeeded);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Trackers.ApplyPushAsync(review));
        Assert.Single(await fixture.EntitiesAsync(tracker.Id));
    }

    [Fact]
    public async Task DetachingOrPurgingTheSchema_MakesTrackersIndependent()
    {
        await using TemporarySqliteFile file = new();
        Fixture fixture = await Fixture.CreateAsync(file);
        Schema schema = await fixture.SchemaAsync("Model", ("Order", "", ["Customer"]));
        Tracker detached = await fixture.Trackers.CreateTrackerAsync(schema.Id, "Team A");
        Tracker linked = await fixture.Trackers.CreateTrackerAsync(schema.Id, "Team B");
        SqliteTrackerRepository trackers = new(fixture.Database);

        await fixture.Trackers.DetachAsync(detached.Id);
        Assert.Null((await trackers.GetAsync(detached.Id))!.SchemaId);
        Assert.Null(await fixture.Links.GetLinkAsync(detached.Id));
        Assert.Equal(2, (await fixture.EntitiesAsync(detached.Id)).Length);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Trackers.DetachAsync(detached.Id));
        Assert.Equal(1, (await fixture.Trackers.GetPendingChangesAsync(schema.Id)).LinkedTrackerCount);

        // Pushes are off while the schema is recycled; purging it unlinks its trackers.
        await fixture.Management.RecycleAsync(schema.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Trackers.ReviewPushAsync(schema.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Trackers.CreateTrackerAsync(schema.Id, "Team C"));
        await fixture.Management.PurgeAsync(schema.Id);
        Assert.Null((await trackers.GetAsync(linked.Id))!.SchemaId);
        Assert.Null(await fixture.Links.GetLinkAsync(linked.Id));
        Assert.Equal(2, (await fixture.EntitiesAsync(linked.Id)).Length);
    }

    [Fact]
    public async Task SchemaLink_IsPartOfTheProjectSnapshot()
    {
        await using TemporarySqliteFile file = new();
        Fixture fixture = await Fixture.CreateAsync(file);
        Schema schema = await fixture.SchemaAsync("Model", ("Order", "Sales", ["Customer"]));
        SqliteProjectSnapshotStore snapshots = new(fixture.Database);
        long before = await snapshots.GetRevisionAsync(fixture.Project.Id);
        Tracker tracker = await fixture.Trackers.CreateTrackerAsync(schema.Id, "Team A");
        await fixture.Trackers.DetachAsync((await fixture.Trackers.CreateTrackerAsync(schema.Id, "Team B")).Id);
        long afterDetach = await snapshots.GetRevisionAsync(fixture.Project.Id);
        Assert.True(afterDetach > before);

        ProjectSnapshot snapshot = (await snapshots.ReadAsync(fixture.Project.Id)).Snapshot!;
        SnapshotSchemaLink link = Assert.IsType<SnapshotSchemaLink>(
            snapshot.Trackers.Single(t => t.Id == tracker.Id.Value).SchemaLink);
        Assert.Equal(schema.Id.Value, link.SchemaId);
        Assert.Null(snapshot.Trackers.Single(t => t.Name == "Team B").SchemaLink);
        Assert.Single(link.Entities.Single(e => e.Name == "Order").Dependencies);

        ProjectSnapshotJsonCodec codec = new();
        ProjectSnapshotPackage package = codec.Encode(snapshot);
        ProjectSnapshot decoded = codec.Decode(package.Files);
        Assert.Equal(package.Sha256, codec.Encode(decoded).Sha256);
        Assert.Throws<InvalidDataException>(() => codec.Encode(snapshot with { FormatVersion = 11 }));

        await using TemporarySqliteFile otherFile = new();
        SqliteDatabase other = new(otherFile.DatabasePath) { StartNewDatabaseWithoutProjects = true };
        await other.InitializeAsync();
        await new SqliteProjectSnapshotStore(other).ApplyAsync(decoded, 0);
        TrackerSchemaLink copied = Assert.IsType<TrackerSchemaLink>(
            await new SqliteSchemaLinkStore(other).GetLinkAsync(tracker.Id));
        Assert.Equal(2, copied.Entities.Count);
        Assert.Equal(schema.Id, (await new SqliteTrackerRepository(other).GetAsync(tracker.Id))!.SchemaId);
    }

    [Fact]
    public async Task Version27Database_GainsTheSchemaLinkTables()
    {
        await using TemporarySqliteFile file = new();
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        await using (SqliteConnection connection = await database.OpenConnectionAsync(CancellationToken.None))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                DROP TABLE tracker_schema_dependencies;
                DROP TABLE tracker_schema_entities;
                DROP TABLE tracker_schemas;
                PRAGMA user_version = 27;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await new SqliteDatabase(file.DatabasePath).InitializeAsync();

        await using SqliteConnection upgraded = await database.OpenConnectionAsync(CancellationToken.None);
        using SqliteCommand check = upgraded.CreateCommand();
        check.CommandText = """
            SELECT (SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name LIKE 'tracker_schema%'),
                   (SELECT user_version FROM pragma_user_version);
            """;
        await using SqliteDataReader reader = await check.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(3, reader.GetInt32(0));
        Assert.Equal(SqliteDatabase.CurrentSchemaVersion, reader.GetInt32(1));
    }

    private sealed class Fixture
    {
        private Fixture(SqliteDatabase database, Project project)
        {
            Database = database;
            Project = project;
            Editing = new SchemaEditingService(new SqliteSchemaStore(database));
            Management = new SchemaManagementService(new SqliteProjectRepository(database), new SqliteSchemaStore(database));
            Links = new SqliteSchemaLinkStore(database);
            Trackers = new SchemaTrackerService(new SqliteProjectRepository(database), new SqliteTrackerRepository(database),
                new SqliteEntityRepository(database), new SqliteDependencyRepository(database),
                new SqliteSchemaStore(database), Links, new SqliteProjectTrackerStore(database),
                new EffectiveDependencyResolver(), new ProgressSnapshotCalculator(), new DependencyRanker());
        }

        public SqliteDatabase Database { get; }
        public Project Project { get; }
        public SchemaEditingService Editing { get; }
        public SchemaManagementService Management { get; }
        public SqliteSchemaLinkStore Links { get; }
        public SchemaTrackerService Trackers { get; }

        public static async Task<Fixture> CreateAsync(TemporarySqliteFile file)
        {
            SqliteDatabase database = new(file.DatabasePath);
            await database.InitializeAsync();
            Project project = await new ProjectManagementService(
                new SqliteProjectRepository(database), new SqliteProjectTrackerStore(database))
                .CreateAsync("Schema project");
            return new Fixture(database, project);
        }

        public async Task<Schema> SchemaAsync(string name, params (string Name, string Group, string[] Dependencies)[] entities)
        {
            Schema schema = await Management.CreateAsync(Project.Id, name);
            foreach ((string entity, string group, string[] dependencies) in entities)
                Assert.True((await Editing.SaveEntityAsync(schema.Id,
                    new SchemaEntityEdit(null, entity, group, dependencies))).Succeeded);
            return schema;
        }

        public async Task<TrackedEntity[]> EntitiesAsync(TrackerId trackerId) =>
            (await new SqliteEntityRepository(Database).GetAllAsync(trackerId)).ToArray();

        public async Task<EntityDependency[]> DependenciesAsync(TrackerId trackerId) =>
            (await new SqliteDependencyRepository(Database).GetAllAsync(trackerId)).ToArray();

        /// <summary>Changes the Tracker the way its pages would.</summary>
        public async Task EditAsync(TrackerId trackerId, Func<TrackedEntity[], TrackedStateChangeSet> change) =>
            await new SqliteTrackedStateStore(Database).ApplyAsync(trackerId, change(await EntitiesAsync(trackerId)));

        public Task<ProjectDashboard?> PortfolioAsync() => new PortfolioQueryService(
            new SqliteProjectRepository(Database), new SqliteTrackerRepository(Database),
            new SqliteEntityRepository(Database), new SqliteDependencyRepository(Database),
            new SqliteProgressHistoryRepository(Database), new EffectiveDependencyResolver(),
            new ProgressSnapshotCalculator(), new SqliteSchemaStore(Database)).GetProjectAsync(Project.Id);
    }
}

using EntityTracker.Application.Projects;
using EntityTracker.Application.Schemas;
using EntityTracker.Application.Snapshots;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Persistence;
using EntityTracker.Infrastructure.Snapshots;

namespace EntityTracker.Infrastructure.Tests.Persistence;

public sealed class SchemaTests
{
    [Fact]
    public async Task Schema_IsCreatedRenamedRecycledRestoredAndPurged()
    {
        await using TemporarySqliteFile file = new();
        (SqliteDatabase database, Project project) = await OpenAsync(file);
        SchemaManagementService schemas = Management(database);

        Schema schema = await schemas.CreateAsync(project.Id, "  Core model ");
        Assert.Equal("Core model", schema.Name);
        Assert.False((await schemas.ValidateNameAsync(project.Id, "core MODEL")).IsValid);
        Assert.True((await schemas.ValidateNameAsync(project.Id, "core MODEL", schema.Id)).IsValid);

        await schemas.RenameAsync(schema.Id, "Shared model");
        SchemaSummary summary = Assert.Single(await schemas.GetByProjectAsync(project.Id));
        Assert.Equal("Shared model", summary.Schema.Name);
        Assert.Equal(0, summary.EntityCount);

        await Assert.ThrowsAsync<InvalidOperationException>(() => schemas.PurgeAsync(schema.Id));
        await schemas.RecycleAsync(schema.Id);
        Assert.Equal(CatalogLifecycleState.Recycled,
            Assert.Single(await schemas.GetByProjectAsync(project.Id)).Schema.LifecycleState);
        Assert.False((await schemas.ValidateNameAsync(project.Id, "Shared model")).IsValid);
        await schemas.RestoreAsync(schema.Id);
        Assert.Equal(CatalogLifecycleState.Active,
            Assert.Single(await schemas.GetByProjectAsync(project.Id)).Schema.LifecycleState);

        await new SchemaEditingService(new SqliteSchemaStore(database)).SaveEntityAsync(schema.Id,
            new SchemaEntityEdit(null, "Order", "", ["Customer"]));
        await schemas.RecycleAsync(schema.Id);
        await schemas.PurgeAsync(schema.Id);
        Assert.Empty(await schemas.GetByProjectAsync(project.Id));
    }

    [Fact]
    public async Task Editing_CreatesUnknownDependenciesAndRefusesCyclesSelfAndDuplicateNames()
    {
        await using TemporarySqliteFile file = new();
        (SqliteDatabase database, Project project) = await OpenAsync(file);
        Schema schema = await Management(database).CreateAsync(project.Id, "Model");
        SchemaEditingService editing = new(new SqliteSchemaStore(database));

        SchemaEditResult order = await editing.SaveEntityAsync(schema.Id,
            new SchemaEntityEdit(null, "Order", " Sales ", ["Customer", "customer", "Product"]));
        Assert.True(order.Succeeded);
        Assert.Equal(["Customer", "Product"], order.CreatedDependencies.Select(e => e.Name));

        SchemaContent content = await editing.GetContentAsync(schema.Id);
        Assert.Equal(["Customer", "Order", "Product"], content.Entities.Select(e => e.Name));
        Assert.Equal("Sales", content.Entities.Single(e => e.Name == "Order").GroupName);
        Assert.Equal(2, content.Dependencies.Count);

        SchemaEntity customer = content.Entities.Single(e => e.Name == "Customer");
        SchemaEditResult cycle = await editing.SaveEntityAsync(schema.Id,
            new SchemaEntityEdit(customer.Id, "Customer", "", ["Order"]));
        Assert.False(cycle.Succeeded);
        Assert.Contains("Order already depends on Customer", cycle.ErrorMessage);

        Assert.False((await editing.SaveEntityAsync(schema.Id,
            new SchemaEntityEdit(customer.Id, "Customer", "", ["CUSTOMER"]))).Succeeded);
        Assert.Equal("Another entity in this schema is already called Order.",
            (await editing.SaveEntityAsync(schema.Id, new SchemaEntityEdit(customer.Id, " order ", "", [])))
            .ErrorMessage);

        // Renaming keeps the entity and its dependants.
        Assert.True((await editing.SaveEntityAsync(schema.Id,
            new SchemaEntityEdit(customer.Id, "Client", "Sales", []))).Succeeded);
        content = await editing.GetContentAsync(schema.Id);
        SchemaEntity client = content.Entities.Single(e => e.Id == customer.Id);
        Assert.Equal("Client", client.Name);
        Assert.Equal(["Order"], SchemaEditingService.Dependants(content, client.Id).Select(e => e.Name));

        // Removing an entity drops the dependencies on it.
        await editing.RemoveEntityAsync(schema.Id, client.Id);
        content = await editing.GetContentAsync(schema.Id);
        Assert.Equal(["Order", "Product"], content.Entities.Select(e => e.Name));
        SchemaDependency remaining = Assert.Single(content.Dependencies);
        Assert.Equal(content.Entities.Single(e => e.Name == "Product").Id, remaining.DependencyEntityId);
    }

    [Fact]
    public async Task RecycledSchema_CannotBeEdited()
    {
        await using TemporarySqliteFile file = new();
        (SqliteDatabase database, Project project) = await OpenAsync(file);
        SchemaManagementService schemas = Management(database);
        Schema schema = await schemas.CreateAsync(project.Id, "Model");
        await schemas.RecycleAsync(schema.Id);

        SchemaEditResult result = await new SchemaEditingService(new SqliteSchemaStore(database))
            .SaveEntityAsync(schema.Id, new SchemaEntityEdit(null, "Order", "", []));

        Assert.False(result.Succeeded);
        Assert.Contains("recycle bin", result.ErrorMessage);
    }

    [Fact]
    public async Task Schema_IsPartOfTheProjectSnapshotAndAdvancesItsRevision()
    {
        await using TemporarySqliteFile file = new();
        (SqliteDatabase database, Project project) = await OpenAsync(file);
        SqliteProjectSnapshotStore snapshots = new(database);
        long before = await snapshots.GetRevisionAsync(project.Id);
        Schema schema = await Management(database).CreateAsync(project.Id, "Model");
        await new SchemaEditingService(new SqliteSchemaStore(database)).SaveEntityAsync(schema.Id,
            new SchemaEntityEdit(null, "Order", "Sales", ["Customer"]));
        Assert.True(await snapshots.GetRevisionAsync(project.Id) > before);

        ProjectSnapshot persisted = Assert.IsType<ProjectSnapshot>((await snapshots.ReadAsync(project.Id)).Snapshot);
        SnapshotSchema snapshotSchema = Assert.Single(persisted.Schemas!);
        Assert.Equal("Model", snapshotSchema.Name);
        SnapshotSchemaEntity order = snapshotSchema.Entities.Single(e => e.Name == "Order");
        Assert.Equal(snapshotSchema.Entities.Single(e => e.Name == "Customer").Id, Assert.Single(order.Dependencies));

        ProjectSnapshotJsonCodec codec = new();
        ProjectSnapshotPackage package = codec.Encode(persisted);
        Assert.Contains(package.Files.Keys, path => path == $".entitytracker/schemas/{schema.Id.Value:D}/schema.json");
        ProjectSnapshot decoded = codec.Decode(package.Files);
        Assert.Equal(package.Sha256, codec.Encode(decoded).Sha256);
        Assert.Throws<InvalidDataException>(() => codec.Encode(persisted with { FormatVersion = 10 }));

        // Applying the snapshot to another machine's database brings the schema with it.
        await using TemporarySqliteFile otherFile = new();
        SqliteDatabase other = new(otherFile.DatabasePath) { StartNewDatabaseWithoutProjects = true };
        await other.InitializeAsync();
        await new SqliteProjectSnapshotStore(other).ApplyAsync(decoded, 0);
        SchemaContent copied = Assert.IsType<SchemaContent>(
            await new SqliteSchemaStore(other).GetContentAsync(schema.Id));
        Assert.Equal(["Customer", "Order"], copied.Entities.Select(e => e.Name));
        Assert.Single(copied.Dependencies);
    }

    private static SchemaManagementService Management(SqliteDatabase database) =>
        new(new SqliteProjectRepository(database), new SqliteSchemaStore(database));

    private static async Task<(SqliteDatabase, Project)> OpenAsync(TemporarySqliteFile file)
    {
        SqliteDatabase database = new(file.DatabasePath);
        await database.InitializeAsync();
        Project project = await new ProjectManagementService(
            new SqliteProjectRepository(database), new SqliteProjectTrackerStore(database))
            .CreateAsync("Schema project");
        return (database, project);
    }
}

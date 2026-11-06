using EntityTracker.Application.Persistence;
using EntityTracker.Application.Schemas;
using EntityTracker.Domain;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

public sealed class SqliteSchemaStore(SqliteDatabase database) : ISchemaStore
{
    private const string SelectSql = """
        SELECT schema_row.id, schema_row.project_id, schema_row.name,
               schema_row.created_at_utc, schema_row.updated_at_utc, schema_row.recycled_at_utc,
               (SELECT COUNT(*) FROM schema_entities entity WHERE entity.schema_id = schema_row.id)
        FROM schemas schema_row
        """;

    public async Task<IReadOnlyList<SchemaSummary>> GetByProjectAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = SelectSql +
            " WHERE schema_row.project_id = $projectId ORDER BY schema_row.name, schema_row.id;";
        command.Parameters.AddWithValue("$projectId", SqlitePersistenceValues.Format(projectId));
        List<SchemaSummary> schemas = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            schemas.Add(new SchemaSummary(Read(reader), reader.GetInt32(6)));
        return schemas;
    }

    public async Task<Schema?> GetAsync(SchemaId schemaId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schemaId);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        return await ReadSchemaAsync(connection, null, schemaId, cancellationToken);
    }

    public async Task<SchemaContent?> GetContentAsync(
        SchemaId schemaId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schemaId);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: true);
        Schema? schema = await ReadSchemaAsync(connection, transaction, schemaId, cancellationToken);
        if (schema is null)
            return null;

        string id = SqlitePersistenceValues.Format(schemaId);
        List<SchemaEntity> entities = [];
        using (SqliteCommand command = Command(connection, transaction, """
                   SELECT id, name, group_name FROM schema_entities
                   WHERE schema_id = $schemaId ORDER BY name, id;
                   """))
        {
            command.Parameters.AddWithValue("$schemaId", id);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                entities.Add(new SchemaEntity(SqlitePersistenceValues.ParseSchemaEntityId(reader.GetString(0)),
                    schemaId, reader.GetString(1), reader.GetString(2)));
        }

        List<SchemaDependency> dependencies = [];
        using (SqliteCommand command = Command(connection, transaction, """
                   SELECT dependency.dependent_entity_id, dependency.dependency_entity_id
                   FROM schema_entity_dependencies dependency
                   JOIN schema_entities entity ON entity.id = dependency.dependent_entity_id
                   WHERE entity.schema_id = $schemaId
                   ORDER BY dependency.dependent_entity_id, dependency.dependency_entity_id;
                   """))
        {
            command.Parameters.AddWithValue("$schemaId", id);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                dependencies.Add(new SchemaDependency(
                    SqlitePersistenceValues.ParseSchemaEntityId(reader.GetString(0)),
                    SqlitePersistenceValues.ParseSchemaEntityId(reader.GetString(1))));
        }

        await transaction.CommitAsync(cancellationToken);
        return new SchemaContent(schema, entities, dependencies);
    }

    public async Task<bool> IsNameReservedAsync(
        ProjectId projectId,
        string name,
        SchemaId? excludingSchemaId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1 FROM schemas
                WHERE project_id = $projectId AND name = $name
                  AND ($excludedId IS NULL OR id <> $excludedId));
            """;
        command.Parameters.AddWithValue("$projectId", SqlitePersistenceValues.Format(projectId));
        command.Parameters.AddWithValue("$name", name.Trim());
        command.Parameters.AddWithValue("$excludedId",
            excludingSchemaId is null ? DBNull.Value : SqlitePersistenceValues.Format(excludingSchemaId));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 0;
    }

    public async Task CreateAsync(Schema schema, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schema);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO schemas (id, project_id, name, created_at_utc, updated_at_utc, recycled_at_utc)
            SELECT $id, $projectId, $name, $created, $updated, $recycled
            FROM projects WHERE id = $projectId AND recycled_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(schema.Id));
        command.Parameters.AddWithValue("$projectId", SqlitePersistenceValues.Format(schema.ProjectId));
        command.Parameters.AddWithValue("$name", schema.Name);
        command.Parameters.AddWithValue("$created", SqlitePersistenceValues.FormatTimestamp(schema.CreatedAtUtc));
        command.Parameters.AddWithValue("$updated", SqlitePersistenceValues.FormatTimestamp(schema.UpdatedAtUtc));
        command.Parameters.AddWithValue("$recycled", schema.RecycledAtUtc is { } recycled
            ? SqlitePersistenceValues.FormatTimestamp(recycled)
            : DBNull.Value);
        try
        {
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("The project is not active.");
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("The schema name is already reserved.", exception);
        }
    }

    public async Task RenameAsync(SchemaId schemaId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schemaId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE schemas SET name = $name, updated_at_utc = $timestamp WHERE id = $id;";
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(schemaId));
        command.Parameters.AddWithValue("$name", name.Trim());
        command.Parameters.AddWithValue("$timestamp", Now());
        try
        {
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("The schema no longer exists.");
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("The schema name is already reserved.", exception);
        }
    }

    public async Task SetLifecycleAsync(
        SchemaId schemaId,
        CatalogLifecycleState lifecycleState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schemaId);
        if (!Enum.IsDefined(lifecycleState))
            throw new ArgumentOutOfRangeException(nameof(lifecycleState));
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE schemas
            SET recycled_at_utc = CASE WHEN $state = 'Recycled' THEN $timestamp ELSE NULL END,
                updated_at_utc = $timestamp
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(schemaId));
        command.Parameters.AddWithValue("$state", lifecycleState.ToString());
        command.Parameters.AddWithValue("$timestamp", Now());
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("The schema no longer exists.");
    }

    public async Task PurgeAsync(SchemaId schemaId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schemaId);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await DeleteAsync(connection, transaction, OneSchema, SqlitePersistenceValues.Format(schemaId),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ApplyAsync(
        SchemaId schemaId,
        SchemaChangeSet changes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schemaId);
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.UpsertedEntities.Any(entity => entity.SchemaId != schemaId))
            throw new InvalidOperationException("An edited entity belongs to another schema.");

        string id = SqlitePersistenceValues.Format(schemaId);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            using (SqliteCommand touch = Command(connection, transaction,
                       "UPDATE schemas SET updated_at_utc = $timestamp WHERE id = $id AND recycled_at_utc IS NULL;"))
            {
                touch.Parameters.AddWithValue("$id", id);
                touch.Parameters.AddWithValue("$timestamp", Now());
                if (await touch.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException("The schema no longer exists or is recycled.");
            }

            foreach (SchemaDependency dependency in changes.RemovedDependencies)
            {
                using SqliteCommand command = Command(connection, transaction, """
                    DELETE FROM schema_entity_dependencies
                    WHERE dependent_entity_id = $dependent AND dependency_entity_id = $dependency;
                    """);
                AddDependency(command, dependency);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (SchemaEntityId removed in changes.RemovedEntityIds)
            {
                using SqliteCommand command = Command(connection, transaction,
                    "DELETE FROM schema_entities WHERE id = $id AND schema_id = $schemaId;");
                command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(removed));
                command.Parameters.AddWithValue("$schemaId", id);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (SchemaEntity entity in changes.UpsertedEntities)
            {
                using SqliteCommand command = Command(connection, transaction, """
                    INSERT INTO schema_entities (id, schema_id, name, group_name)
                    VALUES ($id, $schemaId, $name, $group)
                    ON CONFLICT (id) DO UPDATE SET name = excluded.name, group_name = excluded.group_name
                    WHERE schema_entities.schema_id = excluded.schema_id;
                    """);
                command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(entity.Id));
                command.Parameters.AddWithValue("$schemaId", id);
                command.Parameters.AddWithValue("$name", entity.Name);
                command.Parameters.AddWithValue("$group", entity.GroupName);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (SchemaDependency dependency in changes.AddedDependencies)
            {
                using SqliteCommand command = Command(connection, transaction, """
                    INSERT OR IGNORE INTO schema_entity_dependencies (dependent_entity_id, dependency_entity_id)
                    VALUES ($dependent, $dependency);
                    """);
                AddDependency(command, dependency);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException(
                "The schema changed while it was being edited, or an entity name is already used.", exception);
        }
    }

    internal const string OneSchema = "SELECT $id";

    internal const string ProjectSchemas = "SELECT id FROM schemas WHERE project_id = $id";

    /// <summary>Deletes the schemas <paramref name="schemaIds"/> selects (<see cref="OneSchema"/> or
    /// <see cref="ProjectSchemas"/>, given <paramref name="id"/>), with their entities and dependencies.</summary>
    internal static async Task DeleteAsync(SqliteConnection connection, SqliteTransaction transaction,
        string schemaIds, string id, CancellationToken cancellationToken)
    {
        string entityIds = $"SELECT id FROM schema_entities WHERE schema_id IN ({schemaIds})";
        foreach (string sql in new[]
                 {
                     $"DELETE FROM schema_entity_dependencies WHERE dependent_entity_id IN ({entityIds}) OR dependency_entity_id IN ({entityIds});",
                     $"DELETE FROM schema_entities WHERE schema_id IN ({schemaIds});",
                     $"DELETE FROM schemas WHERE id IN ({schemaIds});"
                 })
        {
            using SqliteCommand command = Command(connection, transaction, sql);
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<Schema?> ReadSchemaAsync(SqliteConnection connection, SqliteTransaction? transaction,
        SchemaId schemaId, CancellationToken cancellationToken)
    {
        using SqliteCommand command = Command(connection, transaction, SelectSql + " WHERE schema_row.id = $id;");
        command.Parameters.AddWithValue("$id", SqlitePersistenceValues.Format(schemaId));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    private static Schema Read(SqliteDataReader reader) => new(
        SqlitePersistenceValues.ParseSchemaId(reader.GetString(0)),
        SqlitePersistenceValues.ParseProjectId(reader.GetString(1)),
        reader.GetString(2),
        SqlitePersistenceValues.ParseTimestamp(reader.GetString(3), "schema creation timestamp"),
        SqlitePersistenceValues.ParseTimestamp(reader.GetString(4), "schema update timestamp"),
        reader.IsDBNull(5) ? CatalogLifecycleState.Active : CatalogLifecycleState.Recycled,
        reader.IsDBNull(5)
            ? null
            : SqlitePersistenceValues.ParseTimestamp(reader.GetString(5), "schema recycle timestamp"));

    private static void AddDependency(SqliteCommand command, SchemaDependency dependency)
    {
        command.Parameters.AddWithValue("$dependent", SqlitePersistenceValues.Format(dependency.DependentEntityId));
        command.Parameters.AddWithValue("$dependency", SqlitePersistenceValues.Format(dependency.DependencyEntityId));
    }

    private string Now() => SqlitePersistenceValues.FormatTimestamp(database.TimeProvider.GetUtcNow());

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }
}

using EntityTracker.Application.Persistence;
using EntityTracker.Domain;
using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

internal static class SqliteResponsibilityWriter
{
    public static async Task ApplySelectionAsync(SqliteConnection connection,
        SqliteTransaction transaction, TrackerId trackerId, ResponsibilitySelection selection,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        string entityId = SqlitePersistenceValues.Format(selection.EntityId);
        string projectId;
        using (SqliteCommand scope = connection.CreateCommand())
        {
            scope.Transaction = transaction;
            scope.CommandText = """
                SELECT tracker.project_id FROM tracked_entities entity
                JOIN trackers tracker ON tracker.id = entity.tracker_id
                WHERE entity.id = $entity AND tracker.id = $tracker;
                """;
            scope.Parameters.AddWithValue("$entity", entityId);
            scope.Parameters.AddWithValue("$tracker", SqlitePersistenceValues.Format(trackerId));
            projectId = (string?)await scope.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("The entity does not belong to this Tracker.");
        }
        string[] desired = selection.DeveloperIds.Select(id => id.Value.ToString("D"))
            .Distinct(StringComparer.Ordinal).ToArray();
        foreach (string developerId in desired)
        {
            using SqliteCommand validate = connection.CreateCommand();
            validate.Transaction = transaction;
            validate.CommandText = """
                SELECT 1 FROM project_developers
                WHERE id = $developer AND project_id = $project AND is_retired = 0;
                """;
            validate.Parameters.AddWithValue("$developer", developerId);
            validate.Parameters.AddWithValue("$project", projectId);
            if (await validate.ExecuteScalarAsync(cancellationToken) is null)
                throw new InvalidOperationException("A selected Developer is unavailable in this Project.");
        }
        List<(string Id, string Developer)> open = [];
        using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT id, developer_id FROM responsibility_periods
                WHERE entity_id = $entity AND ended_at_utc IS NULL;
                """;
            read.Parameters.AddWithValue("$entity", entityId);
            await using SqliteDataReader reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                open.Add((reader.GetString(0), reader.GetString(1)));
        }
        foreach ((string id, string developer) in open.Where(item => !desired.Contains(item.Developer)))
        {
            using SqliteCommand close = connection.CreateCommand();
            close.Transaction = transaction;
            close.CommandText = "UPDATE responsibility_periods SET ended_at_utc = $now WHERE id = $id;";
            close.Parameters.AddWithValue("$id", id);
            close.Parameters.AddWithValue("$now", SqlitePersistenceValues.FormatTimestamp(now));
            await close.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (string developer in desired.Where(id => open.All(item => item.Developer != id)))
        {
            using SqliteCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO responsibility_periods
                    (id, entity_id, developer_id, started_at_utc)
                VALUES ($id, $entity, $developer, $now);
                """;
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            insert.Parameters.AddWithValue("$entity", entityId);
            insert.Parameters.AddWithValue("$developer", developer);
            insert.Parameters.AddWithValue("$now", SqlitePersistenceValues.FormatTimestamp(now));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public static async Task InsertCopiedPeriodAsync(SqliteConnection connection,
        SqliteTransaction transaction, ProjectId projectId, ResponsibilityPeriod period,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO responsibility_periods
                (id, entity_id, developer_id, started_at_utc, ended_at_utc)
            SELECT $id, $entity, developer.id, $started, $ended
            FROM project_developers developer
            WHERE developer.id = $developer AND developer.project_id = $project;
            """;
        command.Parameters.AddWithValue("$id", period.Id.ToString("D"));
        command.Parameters.AddWithValue("$entity", SqlitePersistenceValues.Format(period.EntityId));
        command.Parameters.AddWithValue("$developer", period.DeveloperId.Value.ToString("D"));
        command.Parameters.AddWithValue("$project", SqlitePersistenceValues.Format(projectId));
        command.Parameters.AddWithValue("$started", SqlitePersistenceValues.FormatTimestamp(period.StartedAtUtc));
        command.Parameters.AddWithValue("$ended", period.EndedAtUtc is { } end
            ? SqlitePersistenceValues.FormatTimestamp(end) : DBNull.Value);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("A copied period references a Developer outside this Project.");
    }
}

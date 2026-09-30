using EntityTracker.Application.Persistence;
using EntityTracker.Domain;
using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

public sealed class SqliteProjectDeveloperStore(SqliteDatabase database) : IProjectDeveloperStore
{
    public async Task<IReadOnlyList<ProjectDeveloper>> GetByProjectAsync(ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, initials, display_name, is_retired
            FROM project_developers WHERE project_id = $projectId
            ORDER BY is_retired, initials_key, id;
            """;
        command.Parameters.AddWithValue("$projectId", SqlitePersistenceValues.Format(projectId));
        List<ProjectDeveloper> developers = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            developers.Add(new ProjectDeveloper(new DeveloperId(Guid.Parse(reader.GetString(0))),
                projectId, reader.GetString(1), reader.GetString(2), reader.GetInt32(3) != 0));
        return developers;
    }

    public async Task CreateAsync(ProjectDeveloper developer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(developer);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO project_developers
                (id, project_id, initials_key, initials, display_name, is_retired)
            SELECT $id, $projectId, $key, $initials, $displayName, $retired
            FROM projects WHERE id = $projectId AND lifecycle_state = 'Active';
            """;
        AddParameters(command, developer);
        try
        {
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("The Project is unavailable.");
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("Those initials are already used by an available developer in this Project.", exception);
        }
    }

    public async Task UpdateAsync(ProjectDeveloper developer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(developer);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE project_developers
            SET initials_key = $key, initials = $initials,
                display_name = $displayName, is_retired = $retired
            WHERE id = $id AND project_id = $projectId
              AND EXISTS (SELECT 1 FROM projects
                          WHERE id = $projectId AND lifecycle_state = 'Active');
            """;
        AddParameters(command, developer);
        try
        {
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("The developer or Project is unavailable.");
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("Those initials are already used by an available developer in this Project.", exception);
        }
    }

    private static void AddParameters(SqliteCommand command, ProjectDeveloper developer)
    {
        command.Parameters.AddWithValue("$id", developer.Id.Value.ToString("D"));
        command.Parameters.AddWithValue("$projectId", SqlitePersistenceValues.Format(developer.ProjectId));
        command.Parameters.AddWithValue("$key", developer.Initials.ToUpperInvariant());
        command.Parameters.AddWithValue("$initials", developer.Initials);
        command.Parameters.AddWithValue("$displayName", developer.DisplayName);
        command.Parameters.AddWithValue("$retired", developer.IsRetired ? 1 : 0);
    }
}

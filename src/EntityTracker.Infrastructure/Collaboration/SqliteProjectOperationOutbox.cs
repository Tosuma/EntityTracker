using System.Text.Json;
using System.Text.Json.Serialization;

using EntityTracker.Application.Collaboration;
using EntityTracker.Application.History;
using EntityTracker.Application.Persistence;
using EntityTracker.Domain;
using EntityTracker.Infrastructure.Persistence;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Collaboration;

internal sealed record PendingProjectMutation(long Sequence, ProjectMutation Mutation);

internal sealed class SqliteProjectOperationOutbox(SqliteDatabase database)
{
    private const int PayloadVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public async Task EnqueueAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProjectMutation mutation,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO project_operation_outbox
            (project_id, operation_id, mutation_type, mutation_json, occurred_at_utc)
            VALUES ($projectId, $operationId, $mutationType, $mutationJson, $occurredAtUtc);
            """;
        command.Parameters.AddWithValue("$projectId", SqlitePersistenceValues.Format(mutation.ProjectId));
        command.Parameters.AddWithValue("$operationId", SqlitePersistenceValues.Format(mutation.OperationId));
        command.Parameters.AddWithValue("$mutationType", TypeName(mutation));
        command.Parameters.AddWithValue("$mutationJson", Serialize(mutation));
        command.Parameters.AddWithValue("$occurredAtUtc", SqlitePersistenceValues.FormatTimestamp(mutation.OccurredAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PendingProjectMutation>> GetPendingAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT sequence, mutation_type, mutation_json
            FROM project_operation_outbox
            WHERE project_id = $projectId AND committed_git_object_id IS NULL
            ORDER BY sequence;
            """;
        command.Parameters.AddWithValue("$projectId", SqlitePersistenceValues.Format(projectId));
        List<PendingProjectMutation> values = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(new PendingProjectMutation(
                reader.GetInt64(0),
                Deserialize(reader.GetString(1), reader.GetString(2))));
        }
        return values;
    }

    public async Task<int> CountPendingAsync(
        ProjectId projectId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT count(*)
            FROM project_operation_outbox
            WHERE project_id = $projectId AND committed_git_object_id IS NULL;
            """;
        command.Parameters.AddWithValue("$projectId", SqlitePersistenceValues.Format(projectId));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task MarkCommittedAsync(
        long sequence,
        string commitId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE project_operation_outbox
            SET committed_git_object_id = $commitId,
                committed_at_utc = $committedAtUtc
            WHERE sequence = $sequence AND committed_git_object_id IS NULL;
            """;
        command.Parameters.AddWithValue("$sequence", sequence);
        command.Parameters.AddWithValue("$commitId", commitId);
        command.Parameters.AddWithValue("$committedAtUtc", SqlitePersistenceValues.FormatTimestamp(
            database.TimeProvider.GetUtcNow().ToUniversalTime()));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("The pending Project operation could not be acknowledged.");
    }

    private static string Serialize(ProjectMutation mutation) => JsonSerializer.Serialize(
        new MutationEnvelope(PayloadVersion, JsonSerializer.SerializeToElement(mutation, mutation.GetType(), JsonOptions)),
        JsonOptions);

    private static ProjectMutation Deserialize(string mutationType, string json)
    {
        MutationEnvelope envelope = JsonSerializer.Deserialize<MutationEnvelope>(json, JsonOptions)
            ?? throw new InvalidDataException("A pending Project operation is empty.");
        if (envelope.Version != PayloadVersion)
            throw new InvalidDataException($"Pending Project operation version {envelope.Version} is not supported.");
        Type type = mutationType switch
        {
            nameof(RenameProjectMutation) => typeof(RenameProjectMutation),
            nameof(SetProjectLifecycleMutation) => typeof(SetProjectLifecycleMutation),
            nameof(PurgeProjectMutation) => typeof(PurgeProjectMutation),
            nameof(CreateTrackerMutation) => typeof(CreateTrackerMutation),
            nameof(RenameTrackerMutation) => typeof(RenameTrackerMutation),
            nameof(SetTrackerLifecycleMutation) => typeof(SetTrackerLifecycleMutation),
            nameof(PurgeTrackerMutation) => typeof(PurgeTrackerMutation),
            nameof(ChangeTrackedStateMutation) => typeof(ChangeTrackedStateMutation),
            _ => throw new InvalidDataException($"Pending Project operation type '{mutationType}' is not supported.")
        };
        return envelope.Payload.Deserialize(type, JsonOptions) as ProjectMutation
            ?? throw new InvalidDataException("A pending Project operation payload is invalid.");
    }

    private static string TypeName(ProjectMutation mutation) => mutation switch
    {
        RenameProjectMutation => nameof(RenameProjectMutation),
        SetProjectLifecycleMutation => nameof(SetProjectLifecycleMutation),
        PurgeProjectMutation => nameof(PurgeProjectMutation),
        CreateTrackerMutation => nameof(CreateTrackerMutation),
        RenameTrackerMutation => nameof(RenameTrackerMutation),
        SetTrackerLifecycleMutation => nameof(SetTrackerLifecycleMutation),
        PurgeTrackerMutation => nameof(PurgeTrackerMutation),
        ChangeTrackedStateMutation => nameof(ChangeTrackedStateMutation),
        _ => throw new InvalidOperationException("The Project mutation cannot be queued for Git synchronization.")
    };

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        options.Converters.Add(new TrackedStateChangeSetConverter());
        return options;
    }

    private sealed record MutationEnvelope(int Version, JsonElement Payload);

    private sealed class TrackedStateChangeSetConverter : JsonConverter<TrackedStateChangeSet>
    {
        public override TrackedStateChangeSet Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            ChangeSetDocument value = JsonSerializer.Deserialize<ChangeSetDocument>(ref reader, options)
                ?? throw new JsonException("A tracked-state change set is empty.");
            return new TrackedStateChangeSet(
                value.EntitiesToAdd,
                value.EntitiesToUpdate,
                value.EntityIdsToArchive,
                value.ReconciledOwnerIds,
                value.ResolvedDependencies,
                value.UnresolvedDependencies,
                value.ReconciledOverrideOwnerIds,
                value.ManualDependencyOverrides,
                value.EntitiesWithProgressToUpdate,
                value.EntityIdsToRestore,
                value.ProgressSnapshotAfterChanges,
                value.EntitiesWithRequestedPriorityToUpdate,
                value.EntitiesWithResponsibleDeveloperToUpdate,
                value.EntitiesWithGroupNameToUpdate,
                value.OperationId);
        }

        public override void Write(
            Utf8JsonWriter writer,
            TrackedStateChangeSet value,
            JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, new ChangeSetDocument(
                value.EntitiesToAdd.ToArray(),
                value.EntitiesToUpdate.ToArray(),
                value.EntityIdsToArchive.ToArray(),
                value.ReconciledOwnerIds.ToArray(),
                value.ResolvedDependencies.ToArray(),
                value.UnresolvedDependencies.ToArray(),
                value.ReconciledOverrideOwnerIds.ToArray(),
                value.ManualDependencyOverrides.ToArray(),
                value.EntitiesWithProgressToUpdate.ToArray(),
                value.EntityIdsToRestore.ToArray(),
                value.ProgressSnapshotAfterChanges,
                value.EntitiesWithRequestedPriorityToUpdate.ToArray(),
                value.EntitiesWithResponsibleDeveloperToUpdate.ToArray(),
                value.EntitiesWithGroupNameToUpdate.ToArray(),
                value.OperationId), options);
    }

    private sealed record ChangeSetDocument(
        TrackedEntity[] EntitiesToAdd,
        TrackedEntity[] EntitiesToUpdate,
        EntityId[] EntityIdsToArchive,
        EntityId[] ReconciledOwnerIds,
        PersistedDependency[] ResolvedDependencies,
        PersistedUnresolvedDependency[] UnresolvedDependencies,
        EntityId[] ReconciledOverrideOwnerIds,
        ManualDependencyOverride[] ManualDependencyOverrides,
        TrackedEntity[] EntitiesWithProgressToUpdate,
        EntityId[] EntityIdsToRestore,
        ProgressSnapshotState? ProgressSnapshotAfterChanges,
        TrackedEntity[] EntitiesWithRequestedPriorityToUpdate,
        TrackedEntity[] EntitiesWithResponsibleDeveloperToUpdate,
        TrackedEntity[] EntitiesWithGroupNameToUpdate,
        OperationId OperationId);
}

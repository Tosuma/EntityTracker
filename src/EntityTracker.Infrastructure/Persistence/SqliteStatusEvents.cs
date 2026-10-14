using EntityTracker.Domain;

using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

/// <summary>
/// Appends to an entity's status history. The history is the record of an entity's development
/// status: its newest event is the current status (view entity_current_status).
/// </summary>
internal static class SqliteStatusEvents
{
    /// <summary>
    /// Appends an event that sets the entity's status. A Transition follows the entity's newest event
    /// and is only added when the status changes; returns whether an event was added.
    /// </summary>
    public static async Task<bool> AppendAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EntityId entityId,
        DevelopmentStatus status,
        StatusHistoryEntryKind kind,
        string timestamp,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = kind == StatusHistoryEntryKind.Transition
            ? """
              INSERT INTO entity_status_history
              (event_id, entity_id, sequence, previous_event_id, new_status, entry_kind, occurred_at_utc)
              SELECT $eventId, $entityId, (SELECT COALESCE(MAX(sequence), 0) + 1 FROM entity_status_history),
                     latest.event_id, $status, 'Transition', $timestamp
              FROM (SELECT event_id, new_status FROM entity_status_history
                    WHERE entity_id = $entityId
                    ORDER BY occurred_at_utc DESC, sequence DESC LIMIT 1) latest
              WHERE latest.new_status <> $status;
              """
            : """
              INSERT INTO entity_status_history
              (event_id, entity_id, sequence, previous_event_id, new_status, entry_kind, occurred_at_utc)
              VALUES ($eventId, $entityId, (SELECT COALESCE(MAX(sequence), 0) + 1 FROM entity_status_history),
                      NULL, $status, $kind, $timestamp);
              """;
        command.Parameters.AddWithValue("$eventId", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$entityId", SqlitePersistenceValues.Format(entityId));
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$kind", kind.ToString());
        command.Parameters.AddWithValue("$timestamp", timestamp);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}

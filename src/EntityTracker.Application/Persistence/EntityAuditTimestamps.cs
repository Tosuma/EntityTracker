using EntityTracker.Domain;

namespace EntityTracker.Application.Persistence;

public sealed record EntityAuditTimestamps(
    EntityId EntityId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset SchemaUpdatedAtUtc,
    DateTimeOffset ProgressUpdatedAtUtc);

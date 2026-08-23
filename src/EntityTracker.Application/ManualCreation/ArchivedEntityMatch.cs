using EntityTracker.Domain;

namespace EntityTracker.Application.ManualCreation;

public sealed record ArchivedEntityMatch(EntityId EntityId, string SourceName);

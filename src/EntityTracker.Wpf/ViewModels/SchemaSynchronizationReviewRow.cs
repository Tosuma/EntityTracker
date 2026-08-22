using EntityTracker.Domain;

namespace EntityTracker.Wpf.ViewModels;

public sealed record SchemaSynchronizationReviewRow(
    EntityId EntityId,
    string SourceName,
    string Details);

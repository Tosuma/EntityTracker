using EntityTracker.Application.Dependencies;
using EntityTracker.Domain;

namespace EntityTracker.Application.Workflow;

public sealed record DependencyBlocker(
    string SourceName,
    DependencyBlockerKind Kind,
    EntityId? DependencyEntityId = null,
    DevelopmentStatus? DevelopmentStatus = null);

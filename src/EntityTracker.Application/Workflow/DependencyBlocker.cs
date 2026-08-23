using EntityTracker.Application.Importing;
using EntityTracker.Domain;

namespace EntityTracker.Application.Workflow;

public sealed record DependencyBlocker(
    string SourceName,
    DependencyBlockerKind Kind,
    ImportedDependencyKind DependencyKind,
    EntityId? DependencyEntityId = null,
    DevelopmentStatus? DevelopmentStatus = null);

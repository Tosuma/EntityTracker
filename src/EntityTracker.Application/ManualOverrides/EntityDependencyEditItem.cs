using EntityTracker.Application.Importing;
using EntityTracker.Domain;

namespace EntityTracker.Application.ManualOverrides;

public sealed record EntityDependencyEditItem(
    string DependencySourceName,
    EntitySourceKey DependencySourceKey,
    DependencyEditOrigin Origin,
    ImportedDependencyKind? ImportedKind,
    bool IsResolved,
    EntityId? ResolvedEntityId);

using EntityTracker.Application.Dependencies;
using EntityTracker.Domain;

namespace EntityTracker.Application.ManualOverrides;

/// <param name="DependencySourceName">The name of the entity depended on.</param>
/// <param name="Target">What the edit asks for; pass it back to keep this dependency.</param>
public sealed record EntityDependencyEditItem(
    string DependencySourceName,
    EntitySourceKey DependencySourceKey,
    bool IsResolved,
    EntityId? ResolvedEntityId,
    DependencyTarget Target);

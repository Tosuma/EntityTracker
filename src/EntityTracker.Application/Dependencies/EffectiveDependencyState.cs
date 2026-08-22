using EntityTracker.Application.Persistence;

namespace EntityTracker.Application.Dependencies;

public sealed class EffectiveDependencyState
{
    internal EffectiveDependencyState(
        IEnumerable<PersistedDependency> resolvedDependencies,
        IEnumerable<PersistedUnresolvedDependency> unresolvedDependencies)
    {
        ResolvedDependencies = resolvedDependencies.ToArray();
        UnresolvedDependencies = unresolvedDependencies.ToArray();
    }

    public IReadOnlyList<PersistedDependency> ResolvedDependencies { get; }

    public IReadOnlyList<PersistedUnresolvedDependency> UnresolvedDependencies { get; }
}

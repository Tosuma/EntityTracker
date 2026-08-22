using EntityTracker.Domain;

namespace EntityTracker.Application.Ranking;

public sealed class UnrankedEntity
{
    internal UnrankedEntity(
        EntityId entityId,
        DependencyResolutionState state,
        IEnumerable<string> missingDependencyNames)
    {
        if (state is not DependencyResolutionState.Unresolved
            and not DependencyResolutionState.Blocked)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                "An unranked entity must be unresolved or blocked.");
        }

        string[] missingNameArray = missingDependencyNames.ToArray();
        if (missingNameArray.Length == 0)
        {
            throw new ArgumentException(
                "An unranked entity requires at least one missing dependency name.",
                nameof(missingDependencyNames));
        }

        EntityId = entityId;
        State = state;
        MissingDependencyNames = Array.AsReadOnly(missingNameArray);
    }

    public EntityId EntityId { get; }

    public DependencyResolutionState State { get; }

    /// <summary>
    /// Gets the missing source names directly or transitively blocking this entity.
    /// </summary>
    public IReadOnlyList<string> MissingDependencyNames { get; }
}

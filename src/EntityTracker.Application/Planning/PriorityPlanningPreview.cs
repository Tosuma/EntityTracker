namespace EntityTracker.Application.Planning;

public sealed class PriorityPlanningPreview
{
    internal PriorityPlanningPreview(
        IEnumerable<PriorityPlanningItem> entities,
        IEnumerable<string> unresolvedDependencyNames)
    {
        Entities = entities.ToArray();
        UnresolvedDependencyNames = unresolvedDependencyNames.ToArray();
    }

    public IReadOnlyList<PriorityPlanningItem> Entities { get; }

    public IReadOnlyList<string> UnresolvedDependencyNames { get; }
}

namespace EntityTracker.Application.Persistence;

/// <summary>
/// Payload for a future conflict-review surface. Milestone 12 defines the seam but does not
/// introduce a SharePoint write path or conflict UI.
/// </summary>
public sealed class CollaborativeConflictSet
{
    public CollaborativeConflictSet(IEnumerable<CollaborativeConflict> conflicts)
    {
        ArgumentNullException.ThrowIfNull(conflicts);

        Conflicts = conflicts.ToArray();
        if (Conflicts.Count == 0)
        {
            throw new ArgumentException(
                "A collaborative conflict set must contain at least one conflict.",
                nameof(conflicts));
        }
    }

    public IReadOnlyList<CollaborativeConflict> Conflicts { get; }
}

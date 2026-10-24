namespace EntityTracker.Domain;

public enum DevelopmentStatus
{
    NotStarted,
    InProgress,
    ReworkNeeded,
    DevelopmentCompleted,
    Reconciled,
    Blocked,
    Reworking,
    /// <summary>
    /// Out of scope in this Tracker: the entity stays visible, blocks nothing that depends on it and is left
    /// out of progress. The same entity can be relevant in a sibling Tracker.
    /// </summary>
    NotRelevant
}

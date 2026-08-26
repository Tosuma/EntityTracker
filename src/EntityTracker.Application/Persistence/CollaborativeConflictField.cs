namespace EntityTracker.Application.Persistence;

public enum CollaborativeConflictField
{
    SourceName = 0,
    DevelopmentStatus = 1,
    Notes = 2,
    LifecycleState = 3,
    Provenance = 4,
    ImportedDependency = 5,
    ManualDependencyOverride = 6,
    Identity = 7,
    RequestedPriority = 8
}

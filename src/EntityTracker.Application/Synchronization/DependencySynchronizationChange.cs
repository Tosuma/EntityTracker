using EntityTracker.Application.Importing;

namespace EntityTracker.Application.Synchronization;

public enum DependencySynchronizationChangeKind
{
    Added,
    Removed,
    KindChanged,
    MetadataChanged,
    Resolved,
    BecameUnresolved
}

public sealed record DependencySynchronizationChange(
    string DependencySourceName,
    DependencySynchronizationChangeKind ChangeKind,
    ImportedDependencyKind? PreviousKind,
    ImportedDependencyKind? NewKind);

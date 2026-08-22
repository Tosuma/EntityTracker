namespace EntityTracker.Domain;

/// <summary>
/// Describes how a durable manual correction changes an imported dependency fact.
/// </summary>
public enum ManualDependencyOverrideAction
{
    Add,
    Suppress
}

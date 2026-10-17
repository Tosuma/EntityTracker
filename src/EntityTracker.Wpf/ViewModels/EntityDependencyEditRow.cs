namespace EntityTracker.Wpf.ViewModels;

/// <summary>A dependency in the entity editor; an unresolved one shows a warning icon before its label.</summary>
public sealed record EntityDependencyEditRow(string SourceName, string Resolution, bool IsUnresolved = false);

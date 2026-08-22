using EntityTracker.Application.ManualCreation;

namespace EntityTracker.Wpf.ViewModels;

public sealed record ManualDependencyRow(
    ManualDependencySelection Selection,
    string SourceName,
    string ResolutionLabel,
    bool IsUnresolved);

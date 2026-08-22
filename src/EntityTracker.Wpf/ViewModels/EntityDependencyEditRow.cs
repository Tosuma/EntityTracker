using EntityTracker.Application.ManualOverrides;

namespace EntityTracker.Wpf.ViewModels;

public sealed record EntityDependencyEditRow(
    string SourceName,
    string Origin,
    string Resolution,
    DependencyEditOrigin OriginKind)
{
    public bool CanSuppress => OriginKind == DependencyEditOrigin.Imported;

    public bool CanRemoveManual => OriginKind is
        DependencyEditOrigin.Manual or DependencyEditOrigin.ImportedAndManual;

    public bool CanRestore => OriginKind is
        DependencyEditOrigin.SuppressedImported or DependencyEditOrigin.DormantSuppression;
}

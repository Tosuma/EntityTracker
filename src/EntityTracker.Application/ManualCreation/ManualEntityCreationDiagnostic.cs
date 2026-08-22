namespace EntityTracker.Application.ManualCreation;

public enum ManualEntityCreationDiagnosticSeverity
{
    Warning,
    Error
}

public enum ManualEntityCreationDiagnosticCode
{
    MissingEntityName,
    UnsupportedEntityName,
    DuplicateEntity,
    InvalidDependency,
    SelfDependency,
    DuplicateDependency,
    ArchivedDependency,
    MissingSelectedEntity,
    UnresolvedDependency,
    CycleDetected
}

public sealed record ManualEntityCreationDiagnostic(
    ManualEntityCreationDiagnosticCode Code,
    string Message,
    ManualEntityCreationDiagnosticSeverity Severity =
        ManualEntityCreationDiagnosticSeverity.Error);

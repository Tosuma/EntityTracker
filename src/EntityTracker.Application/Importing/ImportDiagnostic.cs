namespace EntityTracker.Application.Importing;

public enum ImportDiagnosticSeverity
{
    Warning,
    Error
}

public enum ImportDiagnosticCode
{
    FileAccessError,
    InvalidHeader,
    MalformedRow,
    MissingValue,
    InvalidCount,
    CountMismatch,
    NoEntities,
    DuplicateEntity,
    DuplicateDependency,
    ConflictingDependencyKind,
    UnknownDependency,
    SelfDependency,
    UnsupportedEntityName
}

public sealed record ImportDiagnostic(
    ImportDiagnosticCode Code,
    string Message,
    int? RowNumber = null,
    string? ColumnName = null,
    ImportDiagnosticSeverity Severity = ImportDiagnosticSeverity.Error);

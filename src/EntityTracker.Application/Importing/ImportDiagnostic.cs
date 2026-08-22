namespace EntityTracker.Application.Importing;

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
    string? ColumnName = null);

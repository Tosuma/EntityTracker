namespace EntityTracker.Application.Importing;

public sealed class SchemaImportResult
{
    private SchemaImportResult(
        SchemaImportCandidate? candidate,
        IReadOnlyList<ImportDiagnostic> diagnostics)
    {
        Candidate = candidate;
        Diagnostics = diagnostics;
    }

    public bool IsSuccess => Candidate is not null;

    public SchemaImportCandidate? Candidate { get; }

    public IReadOnlyList<ImportDiagnostic> Diagnostics { get; }

    public static SchemaImportResult Success(SchemaImportCandidate candidate)
    {
        return Success(candidate, []);
    }

    public static SchemaImportResult Success(
        SchemaImportCandidate candidate,
        IEnumerable<ImportDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(diagnostics);

        ImportDiagnostic[] diagnosticArray = diagnostics.ToArray();
        ValidateDiagnostics(diagnosticArray, nameof(diagnostics));

        if (diagnosticArray.Any(static diagnostic =>
                diagnostic.Severity == ImportDiagnosticSeverity.Error))
        {
            throw new ArgumentException(
                "A successful import result cannot contain error diagnostics.",
                nameof(diagnostics));
        }

        return new SchemaImportResult(
            candidate,
            Array.AsReadOnly(diagnosticArray));
    }

    public static SchemaImportResult Failure(IEnumerable<ImportDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        ImportDiagnostic[] diagnosticArray = diagnostics.ToArray();
        ValidateDiagnostics(diagnosticArray, nameof(diagnostics));

        if (diagnosticArray.Length == 0)
        {
            throw new ArgumentException(
                "A failed import result requires at least one diagnostic.",
                nameof(diagnostics));
        }

        if (!diagnosticArray.Any(static diagnostic =>
                diagnostic.Severity == ImportDiagnosticSeverity.Error))
        {
            throw new ArgumentException(
                "A failed import result requires at least one error diagnostic.",
                nameof(diagnostics));
        }

        return new SchemaImportResult(
            null,
            Array.AsReadOnly(diagnosticArray));
    }

    private static void ValidateDiagnostics(
        IReadOnlyList<ImportDiagnostic> diagnostics,
        string parameterName)
    {
        if (diagnostics.Any(static diagnostic => diagnostic is null))
        {
            throw new ArgumentException(
                "An import result cannot contain a null diagnostic.",
                parameterName);
        }

        if (diagnostics.Any(static diagnostic => !Enum.IsDefined(diagnostic.Severity)))
        {
            throw new ArgumentException(
                "An import result cannot contain an undefined diagnostic severity.",
                parameterName);
        }
    }
}

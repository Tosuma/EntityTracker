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
        ArgumentNullException.ThrowIfNull(candidate);

        return new SchemaImportResult(candidate, Array.Empty<ImportDiagnostic>());
    }

    public static SchemaImportResult Failure(IEnumerable<ImportDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        ImportDiagnostic[] diagnosticArray = diagnostics.ToArray();
        if (diagnosticArray.Length == 0)
        {
            throw new ArgumentException("A failed import result requires at least one diagnostic.", nameof(diagnostics));
        }

        if (diagnosticArray.Any(static diagnostic => diagnostic is null))
        {
            throw new ArgumentException("A failed import result cannot contain a null diagnostic.", nameof(diagnostics));
        }

        return new SchemaImportResult(null, Array.AsReadOnly(diagnosticArray));
    }
}

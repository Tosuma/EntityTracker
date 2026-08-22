using EntityTracker.Application.Importing;

namespace EntityTracker.Application.Tests.Importing;

public sealed class SchemaImportResultTests
{
    [Fact]
    public void Success_ExposesCandidateWithoutDiagnostics()
    {
        SchemaImportCandidate candidate = new([], []);

        SchemaImportResult result = SchemaImportResult.Success(candidate);

        Assert.True(result.IsSuccess);
        Assert.Same(candidate, result.Candidate);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Failure_ExposesDiagnosticsWithoutCandidate()
    {
        ImportDiagnostic diagnostic = new(
            ImportDiagnosticCode.NoEntities,
            "The CSV does not declare any entities.");

        SchemaImportResult result = SchemaImportResult.Failure([diagnostic]);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Candidate);
        Assert.Equal([diagnostic], result.Diagnostics);
    }

    [Fact]
    public void Failure_RejectsEmptyDiagnostics()
    {
        Assert.Throws<ArgumentException>(() => SchemaImportResult.Failure([]));
    }

    [Fact]
    public void Success_AllowsWarningDiagnosticsWithCandidate()
    {
        SchemaImportCandidate candidate = new([], []);
        ImportDiagnostic warning = new(
            ImportDiagnosticCode.UnknownDependency,
            "Missing dependency.",
            Severity: ImportDiagnosticSeverity.Warning);

        SchemaImportResult result = SchemaImportResult.Success(candidate, [warning]);

        Assert.True(result.IsSuccess);
        Assert.Same(candidate, result.Candidate);
        Assert.Equal([warning], result.Diagnostics);
    }

    [Fact]
    public void Success_RejectsErrorDiagnostics()
    {
        ImportDiagnostic error = new(
            ImportDiagnosticCode.MalformedRow,
            "Malformed row.");

        Assert.Throws<ArgumentException>(() =>
            SchemaImportResult.Success(new SchemaImportCandidate([], []), [error]));
    }

    [Fact]
    public void Failure_RejectsWarningsWithoutAnError()
    {
        ImportDiagnostic warning = new(
            ImportDiagnosticCode.UnknownDependency,
            "Missing dependency.",
            Severity: ImportDiagnosticSeverity.Warning);

        Assert.Throws<ArgumentException>(() =>
            SchemaImportResult.Failure([warning]));
    }
}

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
}

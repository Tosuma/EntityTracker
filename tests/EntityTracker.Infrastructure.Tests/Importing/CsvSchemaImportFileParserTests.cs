using EntityTracker.Application.Importing;
using EntityTracker.Infrastructure.Importing;

namespace EntityTracker.Infrastructure.Tests.Importing;

public sealed class CsvSchemaImportFileParserTests
{
    private const string ValidCsv = """
        table_name;mandatory_dependencies;mandatory_dependency_count;optional_dependencies;optional_dependency_count;total_dependency_count
        Parent;;0;;0;0
        Child;Parent;1;;0;1
        """;

    [Fact]
    public async Task ParseAsync_ReadableFile_UsesCsvParser()
    {
        using TemporaryTextFile file = new(ValidCsv);
        CsvSchemaImportFileParser parser = new(new CsvSchemaImportParser());

        SchemaImportResult result = await parser.ParseAsync(file.FilePath);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["Child", "Parent"],
            result.Candidate!.Entities.Select(static entity => entity.SourceName));
        Assert.Single(result.Candidate.Dependencies);
    }

    [Fact]
    public async Task ParseAsync_MissingFile_ReturnsFileAccessDiagnostic()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            "EntityTracker.Tests",
            Guid.NewGuid().ToString("N"),
            "missing.csv");
        CsvSchemaImportFileParser parser = new(new CsvSchemaImportParser());

        SchemaImportResult result = await parser.ParseAsync(missingPath);

        Assert.False(result.IsSuccess);
        ImportDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ImportDiagnosticCode.FileAccessError, diagnostic.Code);
        Assert.Contains("could not be read", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ParseAsync_DirectoryPath_ReturnsFileAccessDiagnostic()
    {
        using TemporaryDirectory directory = new();
        CsvSchemaImportFileParser parser = new(new CsvSchemaImportParser());

        SchemaImportResult result = await parser.ParseAsync(directory.DirectoryPath);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            ImportDiagnosticCode.FileAccessError,
            Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task ParseAsync_CancelledOperation_PropagatesCancellation()
    {
        using CancellationTokenSource cancellationTokenSource = new();
        cancellationTokenSource.Cancel();
        CsvSchemaImportFileParser parser = new(new CsvSchemaImportParser());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            parser.ParseAsync("unused.csv", cancellationTokenSource.Token));
    }

    [Fact]
    public async Task ParseAsync_UnexpectedParserFailure_PropagatesException()
    {
        using TemporaryTextFile file = new(ValidCsv);
        CsvSchemaImportFileParser parser = new(new ThrowingParser());

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => parser.ParseAsync(file.FilePath));

        Assert.Equal("Parser failure.", exception.Message);
    }

    private sealed class ThrowingParser : ISchemaImportParser
    {
        public SchemaImportResult Parse(TextReader reader)
        {
            throw new InvalidOperationException("Parser failure.");
        }
    }

    private sealed class TemporaryTextFile : IDisposable
    {
        private readonly TemporaryDirectory _directory = new();

        public TemporaryTextFile(string contents)
        {
            FilePath = Path.Combine(_directory.DirectoryPath, "schema.csv");
            File.WriteAllText(FilePath, contents);
        }

        public string FilePath { get; }

        public void Dispose()
        {
            _directory.Dispose();
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                "EntityTracker.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}

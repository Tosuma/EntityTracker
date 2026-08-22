using EntityTracker.Application.Importing;
using EntityTracker.Application.Ranking;

namespace EntityTracker.Application.Tests.Importing;

public sealed class SchemaImportPreviewServiceTests
{
    [Fact]
    public async Task PreviewAsync_ValidCandidate_ReturnsRankedRowsAndKindCounts()
    {
        SchemaImportCandidate candidate = Candidate(
            ["Screen", "Foundation", "Service"],
            [
                ("Service", "Foundation", ImportedDependencyKind.Optional),
                ("Screen", "Service", ImportedDependencyKind.Mandatory)
            ]);
        StubFileParser parser = new(SchemaImportResult.Success(candidate));
        SchemaImportPreviewService service = new(parser, new DependencyRanker());

        SchemaImportPreviewResult result = await service.PreviewAsync("schema.csv");

        Assert.True(result.IsSuccess);
        Assert.Equal("schema.csv", parser.ReceivedPath);
        Assert.Equal(
            ["Foundation", "Service", "Screen"],
            result.Items.Select(static item => item.SourceName));
        Assert.Equal([1, 2, 3], result.Items.Select(static item => item.Rank));
        Assert.Equal([0, 0, 1], result.Items.Select(static item => item.MandatoryDependencyCount));
        Assert.Equal([0, 1, 0], result.Items.Select(static item => item.OptionalDependencyCount));
        Assert.Equal([0, 1, 1], result.Items.Select(static item => item.DependencyCount));
    }

    [Fact]
    public async Task PreviewAsync_ImportFailure_ReturnsImportDiagnosticsAndNoRows()
    {
        ImportDiagnostic diagnostic = new(
            ImportDiagnosticCode.InvalidHeader,
            "Required header missing.",
            1,
            "table_name");
        SchemaImportPreviewService service = new(
            new StubFileParser(SchemaImportResult.Failure([diagnostic])),
            new DependencyRanker());

        SchemaImportPreviewResult result = await service.PreviewAsync("invalid.csv");

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Items);
        Assert.Equal([diagnostic], result.ImportDiagnostics);
        Assert.Empty(result.RankingDiagnostics);
    }

    [Fact]
    public async Task PreviewAsync_Cycle_ReturnsRankingDiagnosticAndNoPartialRows()
    {
        SchemaImportCandidate candidate = Candidate(
            ["Alpha", "Beta"],
            [
                ("Alpha", "Beta", ImportedDependencyKind.Mandatory),
                ("Beta", "Alpha", ImportedDependencyKind.Optional)
            ]);
        SchemaImportPreviewService service = new(
            new StubFileParser(SchemaImportResult.Success(candidate)),
            new DependencyRanker());

        SchemaImportPreviewResult result = await service.PreviewAsync("cycle.csv");

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Items);
        Assert.Empty(result.ImportDiagnostics);
        Assert.Contains(result.RankingDiagnostics, static diagnostic =>
            diagnostic.Code == DependencyRankingDiagnosticCode.CycleDetected);
    }

    [Fact]
    public async Task PreviewAsync_RepeatedPreview_ReturnsSameVisibleOrdering()
    {
        SchemaImportCandidate candidate = Candidate(["Zulu", "Alpha"], []);
        SchemaImportPreviewService service = new(
            new StubFileParser(SchemaImportResult.Success(candidate)),
            new DependencyRanker());

        SchemaImportPreviewResult first = await service.PreviewAsync("schema.csv");
        SchemaImportPreviewResult second = await service.PreviewAsync("schema.csv");

        Assert.Equal(
            first.Items.Select(static item => item.SourceName),
            second.Items.Select(static item => item.SourceName));
        Assert.Equal(["Alpha", "Zulu"], first.Items.Select(static item => item.SourceName));
    }

    private static SchemaImportCandidate Candidate(
        IEnumerable<string> names,
        IEnumerable<(string Dependent, string Dependency, ImportedDependencyKind Kind)> dependencies)
    {
        ImportedEntity[] entities = names
            .Select(static name => new ImportedEntity(EntitySourceKey.From(name), name))
            .ToArray();
        ImportedDependency[] importedDependencies = dependencies
            .Select(static dependency => new ImportedDependency(
                EntitySourceKey.From(dependency.Dependent),
                EntitySourceKey.From(dependency.Dependency),
                dependency.Kind))
            .ToArray();
        return new SchemaImportCandidate(entities, importedDependencies);
    }

    private sealed class StubFileParser(SchemaImportResult result) : ISchemaImportFileParser
    {
        public string? ReceivedPath { get; private set; }

        public Task<SchemaImportResult> ParseAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            ReceivedPath = filePath;
            return Task.FromResult(result);
        }
    }
}

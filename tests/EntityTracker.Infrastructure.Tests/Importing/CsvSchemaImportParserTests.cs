using EntityTracker.Application.Importing;
using EntityTracker.Infrastructure.Importing;

namespace EntityTracker.Infrastructure.Tests.Importing;

public sealed class CsvSchemaImportParserTests
{
    private const string Header = "table_name;mandatory_dependencies;mandatory_dependency_count;optional_dependencies;optional_dependency_count;total_dependency_count";

    private readonly CsvSchemaImportParser _parser = new();

    [Fact]
    public void Parse_CanonicalFixtureProducesExpectedEntitiesAndDependencies()
    {
        SchemaImportResult result = ParseFixture("valid", "extracted_dependencies.csv");

        SchemaImportCandidate candidate = AssertSuccess(result);
        Assert.Equal(
            ["FACILITY", "LEGAL_ENTITY", "PROPERTY", "SPACE", "STRUCTURE", "UNIT"],
            candidate.Entities.Select(static entity => entity.SourceKey.Value));
        Assert.Equal(
            [
                "FACILITY->PROPERTY:Mandatory",
                "PROPERTY->LEGAL_ENTITY:Mandatory",
                "SPACE->FACILITY:Mandatory",
                "SPACE->STRUCTURE:Mandatory",
                "STRUCTURE->PROPERTY:Mandatory",
                "UNIT->STRUCTURE:Mandatory"
            ],
            candidate.Dependencies.Select(FormatDependency));
    }

    [Fact]
    public void Parse_PreservesOptionalKindAndQuotedSemicolon()
    {
        SchemaImportResult result = ParseFixture("valid", "optional_and_quoted.csv");

        SchemaImportCandidate candidate = AssertSuccess(result);
        ImportedEntity parent = Assert.Single(candidate.Entities, static entity =>
            entity.SourceKey == EntitySourceKey.From("sales; parent"));
        ImportedDependency dependency = Assert.Single(candidate.Dependencies);

        Assert.Equal("sales; parent", parent.SourceName);
        Assert.Equal(ImportedDependencyKind.Optional, dependency.Kind);
        Assert.Equal(EntitySourceKey.From("child"), dependency.DependentSourceKey);
        Assert.Equal(EntitySourceKey.From("sales; parent"), dependency.DependencySourceKey);
    }

    [Fact]
    public void Parse_IdenticalInputProducesEquivalentOrderedCandidate()
    {
        string csv = ReadFixture("valid", "extracted_dependencies.csv");

        SchemaImportCandidate first = AssertSuccess(Parse(csv));
        SchemaImportCandidate second = AssertSuccess(Parse(csv));

        Assert.Equal(first.Entities, second.Entities);
        Assert.Equal(first.Dependencies, second.Dependencies);
    }

    [Fact]
    public void Parse_MatchesNamesCaseInsensitivelyAndPreservesTrimmedDeclaration()
    {
        string csv = $"""
            {Header}
             Parent ;;0;;0;0
            child; parent ;1;;0;1
            """;

        SchemaImportCandidate candidate = AssertSuccess(Parse(csv));
        ImportedEntity parent = Assert.Single(candidate.Entities, static entity =>
            entity.SourceKey == EntitySourceKey.From("PARENT"));
        ImportedDependency dependency = Assert.Single(candidate.Dependencies);

        Assert.Equal("Parent", parent.SourceName);
        Assert.Equal(EntitySourceKey.From("PARENT"), dependency.DependencySourceKey);
    }

    [Fact]
    public void Parse_AcceptsRequiredHeadersInAnotherOrder()
    {
        string csv = """
            total_dependency_count;table_name;optional_dependency_count;mandatory_dependencies;mandatory_dependency_count;optional_dependencies
            0;parent;0;;0;
            """;

        SchemaImportCandidate candidate = AssertSuccess(Parse(csv));

        Assert.Equal("PARENT", Assert.Single(candidate.Entities).SourceKey.Value);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Parse_AcceptsSupportedLineEndingsAndBlankLines(string newLine)
    {
        string csv = $"{Header}{newLine}{newLine}parent;;0;;0;0{newLine}";

        SchemaImportCandidate candidate = AssertSuccess(Parse(csv));

        Assert.Equal("PARENT", Assert.Single(candidate.Entities).SourceKey.Value);
    }

    [Fact]
    public void Parse_LeavesCallerOwnedReaderOpen()
    {
        using StringReader reader = new($"{Header}\nparent;;0;;0;0");

        SchemaImportResult result = _parser.Parse(reader);

        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, reader.ReadToEnd());
    }

    [Theory]
    [InlineData("table_name;mandatory_dependencies;mandatory_dependency_count;optional_dependencies;optional_dependency_count")]
    [InlineData("table_name;mandatory_dependencies;mandatory_dependency_count;optional_dependencies;optional_dependency_count;total_dependency_count;extra")]
    [InlineData("table_name;table_name;mandatory_dependency_count;optional_dependencies;optional_dependency_count;total_dependency_count")]
    public void Parse_RejectsInvalidHeaders(string header)
    {
        SchemaImportResult result = Parse(header);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Candidate);
        Assert.Contains(result.Diagnostics, static diagnostic =>
            diagnostic.Code == ImportDiagnosticCode.InvalidHeader);
    }

    [Fact]
    public void Parse_RejectsHeaderOnlyInput()
    {
        SchemaImportResult result = Parse(Header);

        AssertFailure(result, ImportDiagnosticCode.NoEntities);
    }

    [Fact]
    public void Parse_RejectsMalformedCsv()
    {
        SchemaImportResult result = ParseFixture("invalid", "malformed.csv");

        AssertFailure(result, ImportDiagnosticCode.MalformedRow);
    }

    [Fact]
    public void Parse_RejectsWrongFieldCount()
    {
        SchemaImportResult result = Parse($"{Header}\nparent;;0;;0");

        ImportDiagnostic diagnostic = AssertFailure(result, ImportDiagnosticCode.MalformedRow);
        Assert.Equal(2, diagnostic.RowNumber);
    }

    [Fact]
    public void Parse_RejectsMissingTableNameAndEmptyDependencyItem()
    {
        string csv = $"""
            {Header}
            ;;0;;0;0
            child;parent, ;2;;0;2
            parent;;0;;0;0
            """;

        SchemaImportResult result = Parse(csv);

        AssertFailure(result, ImportDiagnosticCode.MissingValue, "table_name");
        AssertFailure(result, ImportDiagnosticCode.MissingValue, "mandatory_dependencies");
    }

    [Theory]
    [InlineData("child;parent;invalid;;0;1", ImportDiagnosticCode.InvalidCount, "mandatory_dependency_count")]
    [InlineData("child;parent;-1;;0;-1", ImportDiagnosticCode.InvalidCount, "mandatory_dependency_count")]
    [InlineData("child;parent;2;;0;2", ImportDiagnosticCode.CountMismatch, "mandatory_dependency_count")]
    [InlineData("child;parent;1;;0;2", ImportDiagnosticCode.CountMismatch, "total_dependency_count")]
    public void Parse_RejectsInvalidOrMismatchedCounts(
        string childRow,
        ImportDiagnosticCode expectedCode,
        string expectedColumn)
    {
        string csv = $"{Header}\nparent;;0;;0;0\n{childRow}";

        SchemaImportResult result = Parse(csv);

        AssertFailure(result, expectedCode, expectedColumn);
    }

    [Fact]
    public void Parse_RetainsUnknownDependencyAsWarningWithLocation()
    {
        SchemaImportResult result = Parse($"{Header}\nchild;missing;1;;0;1");

        Assert.True(result.IsSuccess);
        SchemaImportCandidate candidate = Assert.IsType<SchemaImportCandidate>(result.Candidate);
        Assert.Empty(candidate.Dependencies);
        UnresolvedImportedDependency unresolved = Assert.Single(candidate.UnresolvedDependencies);
        Assert.Equal(EntitySourceKey.From("child"), unresolved.DependentSourceKey);
        Assert.Equal(EntitySourceKey.From("missing"), unresolved.DependencySourceKey);
        Assert.Equal("missing", unresolved.DependencySourceName);
        Assert.Equal(ImportedDependencyKind.Mandatory, unresolved.Kind);

        ImportDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ImportDiagnosticCode.UnknownDependency, diagnostic.Code);
        Assert.Equal(ImportDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("mandatory_dependencies", diagnostic.ColumnName);
        Assert.Equal(2, diagnostic.RowNumber);
        Assert.Contains("missing", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RetainsMultipleUnknownDependencies()
    {
        SchemaImportResult result = Parse(
            $"{Header}\nchild;MissingX, MissingY;2;;0;2");

        Assert.True(result.IsSuccess);
        SchemaImportCandidate candidate = Assert.IsType<SchemaImportCandidate>(result.Candidate);
        Assert.Equal(
            ["MissingX", "MissingY"],
            candidate.UnresolvedDependencies.Select(
                static dependency => dependency.DependencySourceName));
        Assert.Equal(2, result.Diagnostics.Count);
        Assert.All(result.Diagnostics, static diagnostic =>
            Assert.Equal(ImportDiagnosticSeverity.Warning, diagnostic.Severity));
    }

    [Fact]
    public void Parse_RetainsMixedResolvedAndUnresolvedDependencies()
    {
        SchemaImportResult result = Parse(
            $"{Header}\nknown;;0;;0;0\nchild;known, MissingX;2;;0;2");

        Assert.True(result.IsSuccess);
        SchemaImportCandidate candidate = Assert.IsType<SchemaImportCandidate>(result.Candidate);
        Assert.Single(candidate.Dependencies);
        Assert.Equal(
            "MissingX",
            Assert.Single(candidate.UnresolvedDependencies).DependencySourceName);
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void Parse_RejectsSelfDependency()
    {
        SchemaImportResult result = Parse($"{Header}\nchild;CHILD;1;;0;1");

        AssertFailure(result, ImportDiagnosticCode.SelfDependency, "mandatory_dependencies");
    }

    [Fact]
    public void Parse_RejectsCaseInsensitiveDuplicateEntity()
    {
        string csv = $"{Header}\nParent;;0;;0;0\n parent ;;0;;0;0";

        SchemaImportResult result = Parse(csv);

        AssertFailure(result, ImportDiagnosticCode.DuplicateEntity, "table_name");
    }

    [Fact]
    public void Parse_RejectsDuplicateDependency()
    {
        string csv = $"{Header}\nparent;;0;;0;0\nchild;parent, PARENT;2;;0;2";

        SchemaImportResult result = Parse(csv);

        AssertFailure(result, ImportDiagnosticCode.DuplicateDependency, "mandatory_dependencies");
    }

    [Fact]
    public void Parse_RejectsConflictingDependencyKinds()
    {
        string csv = $"{Header}\nparent;;0;;0;0\nchild;parent;1;PARENT;1;2";

        SchemaImportResult result = Parse(csv);

        AssertFailure(result, ImportDiagnosticCode.ConflictingDependencyKind, "optional_dependencies");
    }

    [Fact]
    public void Parse_RejectsTableNameContainingComma()
    {
        SchemaImportResult result = Parse($"{Header}\n\"sales,archive\";;0;;0;0");

        AssertFailure(result, ImportDiagnosticCode.UnsupportedEntityName, "table_name");
    }

    private SchemaImportResult Parse(string contents)
    {
        using StringReader reader = new(contents);
        return _parser.Parse(reader);
    }

    private SchemaImportResult ParseFixture(string category, string fileName)
    {
        return Parse(ReadFixture(category, fileName));
    }

    private static string ReadFixture(string category, string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", category, fileName);
        return File.ReadAllText(path);
    }

    private static SchemaImportCandidate AssertSuccess(SchemaImportResult result)
    {
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Diagnostics);
        return Assert.IsType<SchemaImportCandidate>(result.Candidate);
    }

    private static ImportDiagnostic AssertFailure(
        SchemaImportResult result,
        ImportDiagnosticCode code,
        string? columnName = null)
    {
        Assert.False(result.IsSuccess);
        Assert.Null(result.Candidate);

        return Assert.Single(result.Diagnostics, diagnostic =>
            diagnostic.Code == code
            && (columnName is null || diagnostic.ColumnName == columnName));
    }

    private static string FormatDependency(ImportedDependency dependency)
    {
        return $"{dependency.DependentSourceKey.Value}->{dependency.DependencySourceKey.Value}:{dependency.Kind}";
    }
}

using System.Globalization;

using CsvHelper;
using CsvHelper.Configuration;

using EntityTracker.Application.Importing;

namespace EntityTracker.Infrastructure.Importing;

public sealed class CsvSchemaImportParser : ISchemaImportParser
{
    private const string TableNameHeader = "table_name";
    private const string MandatoryDependenciesHeader = "mandatory_dependencies";
    private const string MandatoryDependencyCountHeader = "mandatory_dependency_count";
    private const string OptionalDependenciesHeader = "optional_dependencies";
    private const string OptionalDependencyCountHeader = "optional_dependency_count";
    private const string TotalDependencyCountHeader = "total_dependency_count";

    private static readonly string[] RequiredHeaders =
    [
        TableNameHeader,
        MandatoryDependenciesHeader,
        MandatoryDependencyCountHeader,
        OptionalDependenciesHeader,
        OptionalDependencyCountHeader,
        TotalDependencyCountHeader
    ];

    public SchemaImportResult Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        CsvConfiguration configuration = new(CultureInfo.InvariantCulture)
        {
            Delimiter = ";",
            HasHeaderRecord = true,
            IgnoreBlankLines = true
        };

        using CsvReader csv = new(reader, configuration, leaveOpen: true);
        List<ImportDiagnostic> diagnostics = [];

        try
        {
            if (!csv.Read())
            {
                return Failure([new ImportDiagnostic(
                    ImportDiagnosticCode.InvalidHeader,
                    "The CSV is empty and does not contain the required header row.",
                    1)]);
            }

            csv.ReadHeader();
            string[] headers = csv.HeaderRecord ?? [];
            Dictionary<string, int>? headerIndexes = ValidateHeaders(headers, diagnostics);
            if (headerIndexes is null)
            {
                return Failure(diagnostics);
            }

            Dictionary<EntitySourceKey, ParsedEntityRow> entityRows = [];
            List<ParsedDependency> parsedDependencies = [];

            while (csv.Read())
            {
                int rowNumber = csv.Parser.RawRow;

                if (csv.Parser.Count != headers.Length)
                {
                    diagnostics.Add(new ImportDiagnostic(
                        ImportDiagnosticCode.MalformedRow,
                        $"Expected {headers.Length} fields but found {csv.Parser.Count}.",
                        rowNumber));
                    continue;
                }

                ParseRow(
                    csv,
                    headerIndexes,
                    rowNumber,
                    entityRows,
                    parsedDependencies,
                    diagnostics);
            }

            if (entityRows.Count == 0)
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticCode.NoEntities,
                    "The CSV does not declare any entities."));
            }

            Dictionary<DependencyKey, ParsedDependency> uniqueDependencies = [];
            ValidateDependencies(entityRows, parsedDependencies, uniqueDependencies, diagnostics);

            if (diagnostics.Count > 0)
            {
                return Failure(diagnostics);
            }

            ImportedEntity[] entities = entityRows.Values
                .Select(static row => new ImportedEntity(row.SourceKey, row.SourceName))
                .OrderBy(static entity => entity.SourceKey.Value, StringComparer.Ordinal)
                .ToArray();

            ImportedDependency[] dependencies = uniqueDependencies.Values
                .Select(static dependency => new ImportedDependency(
                    dependency.DependentSourceKey,
                    dependency.DependencySourceKey,
                    dependency.Kind))
                .OrderBy(static dependency => dependency.DependentSourceKey.Value, StringComparer.Ordinal)
                .ThenBy(static dependency => dependency.Kind)
                .ThenBy(static dependency => dependency.DependencySourceKey.Value, StringComparer.Ordinal)
                .ToArray();

            return SchemaImportResult.Success(new SchemaImportCandidate(entities, dependencies));
        }
        catch (CsvHelperException)
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticCode.MalformedRow,
                "The CSV contains malformed quoting or field data.",
                csv.Parser.RawRow > 0 ? csv.Parser.RawRow : null));

            return Failure(diagnostics);
        }
    }

    private static Dictionary<string, int>? ValidateHeaders(
        IReadOnlyList<string> headers,
        ICollection<ImportDiagnostic> diagnostics)
    {
        Dictionary<string, int> headerIndexes = new(StringComparer.Ordinal);

        for (int index = 0; index < headers.Count; index++)
        {
            string header = headers[index];
            if (!headerIndexes.TryAdd(header, index))
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticCode.InvalidHeader,
                    $"The header '{header}' is declared more than once.",
                    1,
                    header));
            }
        }

        foreach (string requiredHeader in RequiredHeaders)
        {
            if (!headerIndexes.ContainsKey(requiredHeader))
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticCode.InvalidHeader,
                    $"The required header '{requiredHeader}' is missing.",
                    1,
                    requiredHeader));
            }
        }

        HashSet<string> requiredHeaderSet = new(RequiredHeaders, StringComparer.Ordinal);
        foreach (string header in headers)
        {
            if (!requiredHeaderSet.Contains(header))
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticCode.InvalidHeader,
                    $"The header '{header}' is not part of the supported CSV contract.",
                    1,
                    header));
            }
        }

        return diagnostics.Count == 0 ? headerIndexes : null;
    }

    private static void ParseRow(
        CsvReader csv,
        IReadOnlyDictionary<string, int> headerIndexes,
        int rowNumber,
        IDictionary<EntitySourceKey, ParsedEntityRow> entityRows,
        ICollection<ParsedDependency> parsedDependencies,
        ICollection<ImportDiagnostic> diagnostics)
    {
        string tableNameValue = GetField(csv, headerIndexes, TableNameHeader);
        EntitySourceKey? tableKey = ParseTableName(tableNameValue, rowNumber, diagnostics);

        ParsedDependencyList mandatoryDependencies = ParseDependencyList(
            GetField(csv, headerIndexes, MandatoryDependenciesHeader),
            MandatoryDependenciesHeader,
            rowNumber,
            diagnostics);
        ParsedDependencyList optionalDependencies = ParseDependencyList(
            GetField(csv, headerIndexes, OptionalDependenciesHeader),
            OptionalDependenciesHeader,
            rowNumber,
            diagnostics);

        int? mandatoryCount = ParseCount(
            GetField(csv, headerIndexes, MandatoryDependencyCountHeader),
            MandatoryDependencyCountHeader,
            rowNumber,
            diagnostics);
        int? optionalCount = ParseCount(
            GetField(csv, headerIndexes, OptionalDependencyCountHeader),
            OptionalDependencyCountHeader,
            rowNumber,
            diagnostics);
        int? totalCount = ParseCount(
            GetField(csv, headerIndexes, TotalDependencyCountHeader),
            TotalDependencyCountHeader,
            rowNumber,
            diagnostics);

        ValidateCount(
            mandatoryCount,
            mandatoryDependencies,
            MandatoryDependencyCountHeader,
            rowNumber,
            diagnostics);
        ValidateCount(
            optionalCount,
            optionalDependencies,
            OptionalDependencyCountHeader,
            rowNumber,
            diagnostics);

        if (mandatoryCount is not null
            && optionalCount is not null
            && totalCount is not null
            && totalCount != mandatoryCount + optionalCount)
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticCode.CountMismatch,
                $"The total dependency count {totalCount} does not equal mandatory plus optional counts ({mandatoryCount + optionalCount}).",
                rowNumber,
                TotalDependencyCountHeader));
        }

        if (tableKey is null)
        {
            return;
        }

        string sourceName = tableNameValue.Trim();
        if (!entityRows.TryAdd(tableKey, new ParsedEntityRow(tableKey, sourceName)))
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticCode.DuplicateEntity,
                $"The entity '{sourceName}' is declared more than once.",
                rowNumber,
                TableNameHeader));
        }

        AddDependencies(
            tableKey,
            mandatoryDependencies.SourceKeys,
            ImportedDependencyKind.Mandatory,
            rowNumber,
            MandatoryDependenciesHeader,
            parsedDependencies);
        AddDependencies(
            tableKey,
            optionalDependencies.SourceKeys,
            ImportedDependencyKind.Optional,
            rowNumber,
            OptionalDependenciesHeader,
            parsedDependencies);
    }

    private static string GetField(
        CsvReader csv,
        IReadOnlyDictionary<string, int> headerIndexes,
        string header)
    {
        return csv.GetField(headerIndexes[header]) ?? string.Empty;
    }

    private static EntitySourceKey? ParseTableName(
        string value,
        int rowNumber,
        ICollection<ImportDiagnostic> diagnostics)
    {
        string sourceName = value.Trim();
        if (sourceName.Length == 0)
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticCode.MissingValue,
                "A table name is required.",
                rowNumber,
                TableNameHeader));
            return null;
        }

        if (sourceName.Contains(',', StringComparison.Ordinal))
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticCode.UnsupportedEntityName,
                $"The table name '{sourceName}' contains a comma, which is reserved as the dependency-list separator.",
                rowNumber,
                TableNameHeader));
            return null;
        }

        return EntitySourceKey.From(sourceName);
    }

    private static ParsedDependencyList ParseDependencyList(
        string value,
        string columnName,
        int rowNumber,
        ICollection<ImportDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new ParsedDependencyList([], true);
        }

        List<EntitySourceKey> sourceKeys = [];
        bool isValid = true;

        foreach (string part in value.Split(',', StringSplitOptions.None))
        {
            string dependencyName = part.Trim();
            if (dependencyName.Length == 0)
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticCode.MissingValue,
                    "A dependency list contains an empty name.",
                    rowNumber,
                    columnName));
                isValid = false;
                continue;
            }

            sourceKeys.Add(EntitySourceKey.From(dependencyName));
        }

        return new ParsedDependencyList(sourceKeys, isValid);
    }

    private static int? ParseCount(
        string value,
        string columnName,
        int rowNumber,
        ICollection<ImportDiagnostic> diagnostics)
    {
        string trimmedValue = value.Trim();
        if (trimmedValue.Length == 0)
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticCode.MissingValue,
                "A dependency count is required.",
                rowNumber,
                columnName));
            return null;
        }

        if (!int.TryParse(
                trimmedValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int count)
            || count < 0)
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticCode.InvalidCount,
                $"The value '{trimmedValue}' is not a non-negative integer.",
                rowNumber,
                columnName));
            return null;
        }

        return count;
    }

    private static void ValidateCount(
        int? declaredCount,
        ParsedDependencyList dependencies,
        string columnName,
        int rowNumber,
        ICollection<ImportDiagnostic> diagnostics)
    {
        if (declaredCount is not null
            && dependencies.IsValid
            && declaredCount != dependencies.SourceKeys.Count)
        {
            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticCode.CountMismatch,
                $"The declared count {declaredCount} does not match the {dependencies.SourceKeys.Count} parsed dependencies.",
                rowNumber,
                columnName));
        }
    }

    private static void AddDependencies(
        EntitySourceKey dependentSourceKey,
        IEnumerable<EntitySourceKey> dependencySourceKeys,
        ImportedDependencyKind kind,
        int rowNumber,
        string columnName,
        ICollection<ParsedDependency> parsedDependencies)
    {
        foreach (EntitySourceKey dependencySourceKey in dependencySourceKeys)
        {
            parsedDependencies.Add(new ParsedDependency(
                dependentSourceKey,
                dependencySourceKey,
                kind,
                rowNumber,
                columnName));
        }
    }

    private static void ValidateDependencies(
        IReadOnlyDictionary<EntitySourceKey, ParsedEntityRow> entityRows,
        IEnumerable<ParsedDependency> parsedDependencies,
        IDictionary<DependencyKey, ParsedDependency> uniqueDependencies,
        ICollection<ImportDiagnostic> diagnostics)
    {
        foreach (ParsedDependency dependency in parsedDependencies)
        {
            if (dependency.DependentSourceKey == dependency.DependencySourceKey)
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticCode.SelfDependency,
                    $"The entity '{dependency.DependentSourceKey}' cannot depend on itself.",
                    dependency.RowNumber,
                    dependency.ColumnName));
            }

            if (!entityRows.ContainsKey(dependency.DependencySourceKey))
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticCode.UnknownDependency,
                    $"The dependency '{dependency.DependencySourceKey}' has no table declaration row.",
                    dependency.RowNumber,
                    dependency.ColumnName));
            }

            DependencyKey key = new(
                dependency.DependentSourceKey,
                dependency.DependencySourceKey);

            if (!uniqueDependencies.TryAdd(key, dependency))
            {
                ParsedDependency existingDependency = uniqueDependencies[key];
                ImportDiagnosticCode code = existingDependency.Kind == dependency.Kind
                    ? ImportDiagnosticCode.DuplicateDependency
                    : ImportDiagnosticCode.ConflictingDependencyKind;
                string message = existingDependency.Kind == dependency.Kind
                    ? $"The dependency '{dependency.DependencySourceKey}' is repeated for '{dependency.DependentSourceKey}'."
                    : $"The dependency '{dependency.DependencySourceKey}' is both mandatory and optional for '{dependency.DependentSourceKey}'.";

                diagnostics.Add(new ImportDiagnostic(
                    code,
                    message,
                    dependency.RowNumber,
                    dependency.ColumnName));
            }
        }
    }

    private static SchemaImportResult Failure(IEnumerable<ImportDiagnostic> diagnostics)
    {
        return SchemaImportResult.Failure(diagnostics
            .OrderBy(static diagnostic => diagnostic.RowNumber ?? 0)
            .ThenBy(static diagnostic => diagnostic.ColumnName ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Code));
    }

    private sealed record ParsedEntityRow(EntitySourceKey SourceKey, string SourceName);

    private sealed record ParsedDependencyList(IReadOnlyList<EntitySourceKey> SourceKeys, bool IsValid);

    private sealed record ParsedDependency(
        EntitySourceKey DependentSourceKey,
        EntitySourceKey DependencySourceKey,
        ImportedDependencyKind Kind,
        int RowNumber,
        string ColumnName);

    private sealed record DependencyKey(
        EntitySourceKey DependentSourceKey,
        EntitySourceKey DependencySourceKey);
}

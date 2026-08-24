namespace EntityTracker.Infrastructure.Importing;

public static class SchemaCsvContract
{
    public const string Version = "1";
    public const char Delimiter = ';';
    public const string TableNameHeader = "table_name";
    public const string MandatoryDependenciesHeader = "mandatory_dependencies";
    public const string MandatoryDependencyCountHeader = "mandatory_dependency_count";
    public const string OptionalDependenciesHeader = "optional_dependencies";
    public const string OptionalDependencyCountHeader = "optional_dependency_count";
    public const string TotalDependencyCountHeader = "total_dependency_count";

    public static IReadOnlyList<string> Headers { get; } = Array.AsReadOnly(
    [
        TableNameHeader,
        MandatoryDependenciesHeader,
        MandatoryDependencyCountHeader,
        OptionalDependenciesHeader,
        OptionalDependencyCountHeader,
        TotalDependencyCountHeader
    ]);

    public static string HeaderRow { get; } = string.Join(Delimiter, Headers);
}

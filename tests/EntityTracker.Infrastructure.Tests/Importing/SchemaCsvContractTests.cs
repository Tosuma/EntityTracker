using EntityTracker.Infrastructure.Importing;

namespace EntityTracker.Infrastructure.Tests.Importing;

public sealed class SchemaCsvContractTests
{
    [Fact]
    public void VersionOneContractMatchesCanonicalParserHeader()
    {
        Assert.Equal("1", SchemaCsvContract.Version);
        Assert.Equal(';', SchemaCsvContract.Delimiter);
        Assert.Equal(
            "table_name;mandatory_dependencies;mandatory_dependency_count;" +
            "optional_dependencies;optional_dependency_count;total_dependency_count",
            SchemaCsvContract.HeaderRow);
    }

    [Fact]
    public void PostgreSqlQueryTargetsConfigurableSingleSchemaAndExactContractColumns()
    {
        string sql = PostgreSqlSchemaExtractionQuery.Sql;

        Assert.Equal("PostgreSQL", PostgreSqlSchemaExtractionQuery.Dialect);
        Assert.Contains("SELECT 'public'::name AS target_schema", sql);
        Assert.Contains("relation.relkind IN ('r', 'p')", sql);
        Assert.Contains("constraint_row.conrelid <> constraint_row.confrelid", sql);
        Assert.Contains("bool_and(source_column.attnotnull)", sql);
        Assert.Contains("bool_or(foreign_key.is_mandatory)", sql);
        foreach (string header in SchemaCsvContract.Headers)
        {
            Assert.Contains($"AS {header}", sql);
        }
    }
}

namespace EntityTracker.Infrastructure.Importing;

public static class PostgreSqlSchemaExtractionQuery
{
    public const string Dialect = "PostgreSQL";
    public const string DefaultSchema = "public";

    public const string Sql = """
        -- EntityTracker schema CSV contract v1.
        -- Change target_schema once if the tracked tables are not in public.
        WITH settings AS (
            SELECT 'public'::name AS target_schema
        ),
        tracked_tables AS (
            SELECT relation.oid, relation.relname AS table_name
            FROM pg_catalog.pg_class AS relation
            INNER JOIN pg_catalog.pg_namespace AS namespace
                ON namespace.oid = relation.relnamespace
            CROSS JOIN settings
            WHERE namespace.nspname = settings.target_schema
              AND relation.relkind IN ('r', 'p')
        ),
        foreign_keys AS (
            SELECT
                constraint_row.conrelid AS dependent_oid,
                constraint_row.confrelid AS dependency_oid,
                bool_and(source_column.attnotnull) AS is_mandatory
            FROM pg_catalog.pg_constraint AS constraint_row
            CROSS JOIN LATERAL unnest(constraint_row.conkey) AS key_column(attnum)
            INNER JOIN pg_catalog.pg_attribute AS source_column
                ON source_column.attrelid = constraint_row.conrelid
               AND source_column.attnum = key_column.attnum
            WHERE constraint_row.contype = 'f'
              AND constraint_row.conrelid <> constraint_row.confrelid
            GROUP BY constraint_row.oid, constraint_row.conrelid, constraint_row.confrelid
        ),
        table_dependencies AS (
            SELECT
                foreign_key.dependent_oid,
                dependency.relname AS dependency_name,
                bool_or(foreign_key.is_mandatory) AS is_mandatory
            FROM foreign_keys AS foreign_key
            INNER JOIN pg_catalog.pg_class AS dependency
                ON dependency.oid = foreign_key.dependency_oid
            GROUP BY foreign_key.dependent_oid, dependency.relname
        )
        SELECT
            tracked_table.table_name AS table_name,
            coalesce(
                string_agg(
                    table_dependency.dependency_name,
                    ', ' ORDER BY table_dependency.dependency_name)
                    FILTER (WHERE table_dependency.is_mandatory),
                '') AS mandatory_dependencies,
            count(table_dependency.dependency_name)
                FILTER (WHERE table_dependency.is_mandatory)::integer
                AS mandatory_dependency_count,
            coalesce(
                string_agg(
                    table_dependency.dependency_name,
                    ', ' ORDER BY table_dependency.dependency_name)
                    FILTER (WHERE NOT table_dependency.is_mandatory),
                '') AS optional_dependencies,
            count(table_dependency.dependency_name)
                FILTER (WHERE NOT table_dependency.is_mandatory)::integer
                AS optional_dependency_count,
            count(table_dependency.dependency_name)::integer AS total_dependency_count
        FROM tracked_tables AS tracked_table
        LEFT JOIN table_dependencies AS table_dependency
            ON table_dependency.dependent_oid = tracked_table.oid
        GROUP BY tracked_table.table_name
        ORDER BY tracked_table.table_name;
        """;
}

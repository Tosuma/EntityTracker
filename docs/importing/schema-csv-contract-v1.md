# EntityTracker schema CSV contract v1

EntityTracker imports UTF-8 CSV with a semicolon (`;`) delimiter, a header row, and standard double-quote escaping. Columns are case-sensitive and may be reordered, but exactly these six columns must be present:

| Column | Meaning |
| --- | --- |
| `table_name` | Unqualified table name declared by this row. |
| `mandatory_dependencies` | Alphabetical comma-separated dependency names whose foreign-key columns are all non-nullable, or empty. |
| `mandatory_dependency_count` | Number of mandatory dependency names. |
| `optional_dependencies` | Alphabetical comma-separated dependency names with at least one nullable foreign-key column, or empty. |
| `optional_dependency_count` | Number of optional dependency names. |
| `total_dependency_count` | Mandatory plus optional dependency count. |

Every table, including a table with no dependencies, has one row. Names are trimmed and matched case-insensitively by EntityTracker. Commas are not supported inside names.

## PostgreSQL query

The canonical query is embedded in the application and available from **Help & SQL**. It reads PostgreSQL system catalogs but never changes the database.

- Change the `target_schema` value near the top of the query when the tracked schema is not `public`.
- Run one schema at a time. The v1 contract uses unqualified table names and cannot distinguish identical names in different schemas.
- Self-referencing foreign keys are excluded because they describe internal table structure rather than an implementation dependency between two entities.
- Multiple foreign keys to the same table are collapsed. If any is mandatory, the table dependency is mandatory.
- A referenced table outside the selected schema is emitted as a dependency but has no declaration row, so EntityTracker preserves it as unresolved.

Export the result grid as UTF-8 CSV with column headers and `;` as the delimiter. If the tracker also contains entities from other schemas, use a **Partial** import so absent entities are not archived.

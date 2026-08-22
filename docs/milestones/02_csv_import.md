# Milestone 2 — CSV Parsing and Validation

## Goal
Convert the database-export CSV into a validated in-memory import candidate without changing persistent state.

## Tasks
- Define and document the exact CSV column contract.
- Parse table/entity names and dependency relationships.
- Normalize whitespace/casing by an explicit policy while preserving display names.
- Handle duplicate relationship rows.
- Return structured diagnostics for malformed rows, missing values, unknown references, and self-dependencies.
- Add representative CSV fixtures and unit tests.
- Keep parsing independent of WPF and persistence.

## Acceptance criteria
- Valid input produces deterministic entities and edges.
- Invalid input gives actionable diagnostics.
- Re-importing identical CSV produces equivalent output.
- No access to the real database is required for tests.

## CSV contract

The import file is UTF-8 text with a semicolon (`;`) field delimiter and standard double-quote CSV escaping. Blank physical lines are ignored. The header names are case-sensitive, may appear in any order, and must contain exactly these columns:

| Column | Required value |
| --- | --- |
| `table_name` | The table/entity declared by the row. |
| `mandatory_dependencies` | Comma-separated mandatory dependency names, or empty. |
| `mandatory_dependency_count` | Non-negative integer matching the mandatory list. |
| `optional_dependencies` | Comma-separated optional dependency names, or empty. |
| `optional_dependency_count` | Non-negative integer matching the optional list. |
| `total_dependency_count` | Sum of the mandatory and optional counts. |

Each entity is declared by exactly one row, including entities with no dependencies. Every dependency name must have a corresponding `table_name` row elsewhere in the file.

### Name policy

- Trim surrounding whitespace from table names and individual dependency names.
- Preserve the trimmed spelling of `table_name` for display.
- Match names case-insensitively using an invariant normalized source key.
- Preserve internal whitespace.
- Commas are not supported inside entity names because comma is the dependency-list separator.

### Validation policy

- Reject empty files, header-only files, missing/duplicate/unexpected headers, malformed rows, blank names, empty dependency-list entries, invalid counts, and count mismatches.
- Reject duplicate entity rows and duplicate dependency relationships rather than silently deduplicating them.
- Reject a relationship listed as both mandatory and optional.
- Reject unknown dependencies and self-dependencies.
- Preserve mandatory and optional dependency kinds in the import candidate.
- Return no partial candidate when any diagnostic is present.

The canonical example is `tests/EntityTracker.Infrastructure.Tests/Fixtures/valid/extracted_dependencies.csv`.

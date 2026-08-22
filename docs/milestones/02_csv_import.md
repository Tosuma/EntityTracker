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

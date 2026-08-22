# Milestone 4 — SQLite Persistence and Stable Tracking

## Goal
Persist development information independently of rank and row position.

## Tasks
- Introduce persistence interfaces in the Application layer now that they are needed.
- Implement them with SQLite in Infrastructure.
- Persist stable entity identity, source name, status, notes, schema metadata, dependencies, and timestamps.
- Ensure imported metadata updates never overwrite manual progress fields.
- Treat rank as disposable derived data, not authoritative persisted state.
- Add a small migration/schema-version strategy.
- Add integration tests using temporary SQLite databases.

## Acceptance criteria
- Restarting preserves progress and notes.
- Rank changes cannot move progress to another entity.
- Existing entities can be updated without recreation.
- Domain/Application do not depend on SQLite types.

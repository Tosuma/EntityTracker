# Milestone 11 — SQL Query Utility and Usability Polish

## Goal
Make schema extraction/import convenient while keeping the SQL query completely independent of CSV import.

## Tasks
- Add a dedicated `Show SQL Query`/`Get SQL Query` button.
- Display the canonical query with an easy `Copy` action.
- Do not make viewing/copying the SQL a prerequisite for importing.
- Version/document the query output contract alongside the CSV parser.
- Improve file-picker defaults, error messages, confirmations, empty states, and recent-import information.
- Add concise in-app help.
- Publish/package the WPF application for simple Windows launching.

## Acceptance criteria
- A user with a CSV imports immediately without touching the SQL button.
- A user who lacks the query can retrieve it independently.
- Normal operation requires no command line.

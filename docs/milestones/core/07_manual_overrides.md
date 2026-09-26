# Milestone 7 — Manual Dependency Overrides and Entity Editing

## Goal
Support imperfect foreign-key metadata without losing corrections on later imports.

## Tasks
- Keep imported dependencies distinct from manual additions/removals/overrides.
- Define an effective graph from imported facts plus overrides.
- Add an entity-details editor.
- Permit corrections both during import review and later.
- Revalidate and rerank after dependency edits.
- Warn immediately when an edit introduces a cycle.

## Acceptance criteria
- A manually added missing dependency survives later imports.
- Incorrect imported relationships can be overridden without destroying the raw imported fact.
- Effective graph construction is deterministic and tested.

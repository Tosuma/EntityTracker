# GS-01 — Portable Project Snapshot

**Status: planned.** Implement this milestone separately after approving its implementation plan. This document does not authorize GS-02 or Git operations.

## Goal and user-facing outcome

Make a complete Project portable as a validated, deterministic, versioned snapshot. A Project exported from one SQLite catalog can reconstruct the same Project, Trackers, entities, dependencies, planning fields, recycled items, import metadata, and full history in an empty catalog without involving Git.

## Starting point and boundaries

The completed [product](../product/00_README.md) and [UX](../ux/00_README.md) milestones provide Project/Tracker identity and SQLite persistence. The [GS shared contract](00_README.md#version-1-repository-format) defines the JSON layout. SQLite remains the sole runtime store. Do not add repository linking, a Sync control, Git execution, remote settings, or a scheduler in GS-01.

## Required implementation

- Add stable IDs to status-history events and progress snapshots and causal predecessor IDs to status-history entries. Deterministically migrate existing records while preserving content, chronology, and audit information. Repeated migration must keep the same identities.
- Treat cross-Project Tracker copy origins as portable informational references so importing a Project does not require the source Project to exist locally.
- Define `ProjectSnapshot` and its application-layer validation model. Preserve stable IDs, UTC timestamps, audit fields, current state, lifecycle state, imported and unresolved dependencies, manual overrides, relationships, planning metadata, complete histories, schema import summaries, and copy provenance. Include a format version; reject malformed and newer versions before applying anything.
- Serialize the [version 1 layout](00_README.md#version-1-repository-format) to canonical UTF-8 JSON with stable property and collection order and no volatile export timestamp. The same logical state must produce the same bytes and snapshot hash.
- Add `IProjectSnapshotStore` for consistent Project reads and atomic full-Project application. Add per-Project revisions covering every synchronized table and lifecycle operation, including Project and Tracker deletion or restoration. Apply with an expected-revision check and rollback on any failure.
- Enable SQLite WAL and bounded busy handling. Hold a read transaction only while materializing the snapshot model; perform JSON serialization after the transaction ends. Keep existing operations responsive.

## Interfaces and compatibility

Domain owns stable history identity and causal linkage. Application owns snapshot, validation, revision, and store interfaces. Infrastructure owns SQLite migration and JSON encoding. Preserve existing data through migration; do not infer missing history or invent status transitions. The snapshot format is versioned so later formats can be rejected safely.

## Tests and acceptance

- Migrate a populated legacy catalog twice and verify deterministic history IDs, original order, contents, and causal links.
- Round-trip a complete Project into an empty catalog and compare all IDs, relationships, lifecycle states, import metadata, planning fields, and history. Include recycled records, unresolved and cross-Tracker references, and a copy origin whose source Project is absent.
- Verify canonical byte equality for logically equal snapshots and stable hash across repeated exports.
- Reject malformed JSON, duplicate IDs, invalid references, unsupported newer versions, and revision mismatch without changing SQLite. Verify transactional rollback on an injected apply failure.
- Build the complete solution and run its regression suite. No Git process is needed for GS-01 acceptance.

## Agent planning prompt

```text
Plan GS-01 only. Read the GS category contract, current Domain/Application history models, SQLite schema and migrations, Project/Tracker lifecycle stores, and existing tests. Produce a repository-specific migration, snapshot, validation, revision, and verification plan. Do not edit files or plan GS-02 implementation.
```

## Agent implementation prompt

```text
Implement GS-01 according to its approved plan and this milestone. Preserve legacy data, add deterministic portable snapshots and atomic revision-checked SQLite application, then build and test the complete solution. Do not add Git linking, UI sync controls, or later GS milestones.
```

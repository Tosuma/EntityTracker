# Milestone 12 — SharePoint-Ready Boundary and Hardening

## Goal
Prove the architecture can replace or augment SQLite without rewriting core behavior.

## Tasks
- Review persistence interfaces based on actual usage; simplify rather than generalize unnecessarily.
- Ensure SQLite-specific types never escape Infrastructure.
- Define collaborative-backend semantics for identity, concurrency, statuses, notes, archives, and history.
- Once SharePoint access exists, implement a separate SharePoint adapter using the organization-approved Microsoft API/authentication approach.
- Keep SQLite available for local development/testing unless requirements change.
- Add logging, recovery/backup considerations, migration tests, and architecture documentation.
- Document replacement seams for WPF → another UI and SQLite → SharePoint.

## Acceptance criteria
- Core ranking/import/workflow tests run without WPF or SQLite.
- Storage implementation is selected at composition/configuration.
- Replacing WPF does not require rewriting Domain/Application/Reporting.
- SharePoint concerns remain isolated to Infrastructure.

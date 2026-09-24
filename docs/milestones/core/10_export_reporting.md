# Milestone 10 — Chart Export and Manager Reporting

## Goal
Make visualizations reusable in project status reports.

## Tasks
- Add `Save as PNG` beside charts.
- Add `Copy to clipboard` if practical.
- Render at sufficient resolution for documents/presentations.
- Investigate SVG/vector export only if cleanly supported; do not delay PNG for it.
- Add a compact manager summary of key counts and dates.
- Keep report/export logic in the Reporting project.
- Consider PDF/Word generation only after chart export is stable.

## Acceptance criteria
- Manager can export charts without taking screenshots.
- Labels remain readable in a normal report.
- Export logic is independent of graph/business logic.

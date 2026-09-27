# Milestone 9 — Progress History and Charts

## Goal
Provide useful project-management visualizations backed by real history.

## Tasks
- Persist status-transition history with timestamps.
- Build reporting/query models independent of WPF controls.
- Integrate a WPF-compatible .NET chart library, for example LiveCharts2 after checking current compatibility.
- Start with useful charts:
  - entities by status;
  - cumulative completions over time;
  - ready versus blocked over time;
  - weekly completion throughput.
- Avoid charts that do not support a management decision.

## Acceptance criteria
- Historical charts come from stored events/history, not guesses from current state.
- Reopening reproduces the same history.
- Reporting calculations are testable without rendering WPF.

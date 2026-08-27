# EntityTracker UX milestone roadmap

The `UX-` category modernizes EntityTracker around Microsoft's Fluent design language and improves
its information architecture for Team Leads and Project Managers. It is separate from the numbered
core roadmap, corrective milestones, Product Feedback milestones, and CI milestones.

Read [`../design/FLUENT_UI_DIRECTION.md`](../design/FLUENT_UI_DIRECTION.md) before planning or
implementing any UX milestone.

## Design baseline

- WPF and .NET 10 remain the presentation technology.
- Use the built-in WPF Fluent theme and ordinary WPF resources.
- Preserve accepted Domain/Application behavior and project boundaries.
- Keep the interface usable, buildable, and visually coherent after every milestone.
- Treat accessibility and keyboard behavior as part of every milestone.
- Do not implement a third-party Fluent framework, WinUI/Avalonia rewrite, or speculative design
  system.

## Roadmap

1. [UX-01 — Projects and Trackers Foundation](ux_01_projects_and_trackers_foundation.md)
2. [UX-02 — Fluent Foundation and Theme](ux_02_fluent_foundation.md)
3. [UX-03 — Application Shell and Portfolio](ux_03_application_shell_and_portfolio.md)
4. [UX-04 — Overview and Entity Details](ux_04_overview_and_entity_details.md)
5. [UX-05 — Schema Synchronization Experience](ux_05_schema_synchronization_experience.md)
6. [UX-06 — Entity Creation and Editing](ux_06_entity_creation_and_editing.md)
7. [UX-07 — Reporting and Tracker Comparison](ux_07_reporting_and_tracker_comparison.md)
8. [UX-08 — Accessibility and Consistency](ux_08_accessibility_and_consistency.md)

## Classification

| Milestone | Primary concern |
| --- | --- |
| UX-01 | Cross-layer product prerequisite |
| UX-02 | Visual foundation |
| UX-03 | Information architecture and tracker-management workflow |
| UX-04 | Overview interaction and information hierarchy |
| UX-05 | Synchronization workflow |
| UX-06 | Creation/editing workflow |
| UX-07 | Reporting, dashboards, and comparison |
| UX-08 | Polish and accessibility audit |

UX-01 is a deliberate exception to the presentation focus of the category. The selected UX requires
first-class Projects and Trackers in one catalog, while the accepted application currently assumes
one global tracker. Keeping that prerequisite explicit prevents tracker scope or portfolio
calculations from leaking into WPF.

## Execution order and dependencies

- Implement UX-01 before exposing portfolio navigation or tracker comparison.
- UX-02 establishes one coherent Fluent baseline before page layouts are redesigned.
- Implement UX-03 before the page-specific milestones.
- UX-04 through UX-06 preserve the accepted PF-01 through PF-05 behaviors.
- UX-07 depends on tracker-scoped history from UX-01 and dashboards from UX-03.
- UX-08 is the final audit, but earlier milestones must satisfy their own accessibility criteria.

The recommended execution order is UX-01 through UX-08. Do not start a later milestone merely
because a related control or view is already open for modification.

## Storage direction

SQLite remains the local catalog and working cache. The planned SharePoint integration is retired.
A future, separately planned capability may synchronize a Project to a Git repository and folder
through an explicit user action. The UX roadmap reserves an information-architecture location for
that capability but does not implement or expose it.

## Verification shared by every milestone

- Build the complete solution.
- Run the complete regression suite.
- Test presentation state in ViewModels where practical.
- Keep Domain/Application logic independent of WPF.
- Verify keyboard and focus behavior manually.
- Verify affected screens at minimum size and common desktop size.
- Update deterministic README screenshot automation when routes or visible screens change.
- Do not accept a visual half-migration or duplicate navigation/workflow pattern.


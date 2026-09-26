# UX-02 — Fluent Foundation and Theme

## Context

UX-01 provides tracker-safe application state. The WPF application currently uses a large custom
resource dictionary with broad control templates and fixed light-theme brushes. These styles can
mask the built-in Fluent behavior available in .NET 10.

## Goal

Establish a coherent built-in Fluent control baseline, semantic design resources, and persistent
Light/Dark/System theme behavior across every existing screen.

## User-facing outcome

The entire application uses a consistent Fluent visual foundation and follows the selected Windows
appearance without changing navigation or workflow behavior.

## Design decisions

- Use the built-in .NET 10 WPF Fluent theme.
- Default appearance is `System`; users may choose Light, Dark, or System.
- Preserve EntityTracker's brand palette and semantic status meanings.
- Use ordinary WPF resource dictionaries rather than a custom design-system framework.
- Exported chart images keep a stable report palette independent of the application theme.

## Required implementation

- Enable the supported .NET 10 WPF application/window theme mechanism.
- Add a strongly typed presentation setting for Light, Dark, and System and persist it in local
  settings with backward-compatible migration.
- Provide a small Appearance control in an existing utility/settings location until UX-03 installs
  the final shell.
- Reorganize WPF resources into clear theme-aware semantic concerns: palette/surfaces, typography
  and spacing, and small component variants.
- Use dynamic, theme-aware semantic resources for page, card, text, border, selection, focus,
  overlay, status, missing/removal, and unresolved-warning presentation.
- Remove broad custom templates when built-in Fluent controls already supply the required states.
- Retain keyed Primary, Secondary, and Danger command variants only where they express semantic
  command importance.
- Standardize normal control heights, table row density, headings, card borders/radii, tooltips,
  and focus treatment across all existing pages.
- Update `docs/design/DESIGN_GUIDE.md` to align the accepted brand palette with the Fluent baseline,
  dark-theme use, and `FLUENT_UI_DIRECTION.md`.
- Keep the Reporting palette separate from WPF and update it only where accessible semantic
  consistency requires it.

## Interaction behavior

- Changing theme updates all open application-owned surfaces immediately.
- System mode follows the current Windows theme through the built-in behavior.
- Theme choice survives restart and is local to the installation, not Project or Tracker data.
- Keyboard focus remains clearly visible in every theme.
- Native file pickers and Windows chrome may retain system-owned presentation.

## Architectural constraints

- Do not introduce a third-party Fluent/controls package.
- Do not recreate WinUI NavigationView or other controls in this milestone.
- Do not change application workflows, navigation destinations, filtering, or business behavior.
- Keep theme and WPF resources out of Domain, Application, Infrastructure persistence, and
  Reporting calculations.
- Do not split or redesign pages planned for UX-03 onward.

## Tests / verification

Automated checks should cover:

- settings migration and Light/Dark/System round trips;
- invalid appearance-setting fallback to System;
- presentation resource dictionaries loading without missing keys;
- XAML checks preventing raw feature-level color values and unsupported third-party theme packages;
- existing ViewModel and application regression suites.

Manual checks must cover every current page and modal in Light and Dark, switching to System,
keyboard focus, hover/pressed/disabled states, status contrast, warning/error distinction, charts,
minimum-window layout, and ordinary DPI scaling.

## Acceptance criteria

- [ ] The application uses the built-in .NET 10 WPF Fluent theme.
- [ ] Light, Dark, and System modes work and persist.
- [ ] System is the default for new and migrated settings.
- [ ] Existing screens share one coherent Fluent baseline.
- [ ] Feature XAML uses semantic resources rather than new raw colors.
- [ ] Coral and unresolved-warning semantics remain distinguishable and accessible.
- [ ] Focus, disabled, hover, pressed, and selected states remain visible.
- [ ] PNG/report output does not vary unexpectedly with live application theme.
- [ ] No workflow or business behavior changes.
- [ ] No third-party Fluent framework is introduced.
- [ ] The complete solution builds and all tests pass.

## Out of scope

- left navigation and portfolio shell;
- page information-architecture changes;
- overview, import, editor, or report redesign;
- custom animation/elevation framework;
- Git synchronization.

## Agent planning prompt

```text
Plan UX-02 — Fluent Foundation and Theme.

Read the architecture document, both design guides, `ux/README.md`, UX-01, and this milestone.
Inspect App.xaml, the current theme dictionary, MainWindow XAML, settings persistence, charts, and
presentation tests. Produce a repository-specific plan for UX-02 only. Verify the exact built-in
.NET 10 WPF Fluent APIs available locally. Do not modify the repository and do not plan the new
shell or later page redesigns.
```

## Agent implementation prompt

```text
Implement UX-02 according to the architecture, Fluent design direction, this milestone, and the
approved UX-02 plan. Use the built-in .NET 10 WPF Fluent theme and small semantic WPF resources.
Implement Light/Dark/System persistence, update relevant tests and design documentation, verify all
current screens in both themes, build the solution, and run all tests. Do not begin UX-03 or change
accepted workflow behavior.
```


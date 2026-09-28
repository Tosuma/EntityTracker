# UX-08 — Accessibility and Consistency

## Context

UX-01 through UX-07 establish the product hierarchy, Fluent foundation, shell, primary workflows,
and reporting. Accessibility has been required throughout; this milestone performs the final
cross-application audit and resolves inconsistencies without adding new product capability.

## Goal

Deliver a cohesive, keyboard-accessible, scalable Fluent application whose terminology, feedback,
and interaction states are consistent across every destination and modal.

## User-facing outcome

EntityTracker behaves predictably across workflows, themes, window sizes, keyboards, and DPI
settings, with clear errors, focus, labels, empty states, and help.

## Design decisions

- Accessibility is a release criterion, not optional polish.
- Status, warning, and destructive meaning must never rely on color alone.
- Standard WPF accessibility behavior is preferred over custom interaction code.
- This milestone fixes inconsistency; it does not reopen accepted product semantics.

## Required implementation

### Accessibility audit

- Verify complete keyboard navigation and sensible tab order on every destination, flyout, dialog,
  details pane, table, chart command, and confirmation.
- Ensure visible focus in Light and Dark and predictable initial/restored focus for dialogs/flyouts.
- Add useful `AutomationProperties.Name`, help text, and labels for icon-only commands, selectors,
  badges, charts, table actions, and issue indicators.
- Verify contrast for normal text, secondary text, selection, disabled state, focus, status badges,
  errors, Coral danger states, and yellow unresolved warnings.
- Verify all status/issue information has textual labels.
- Verify common Windows scaling and screen-reader-friendly ordering.

### Consistency audit

- Standardize page titles, supporting descriptions, toolbar hierarchy, command labels, capitalization,
  spacing, card treatment, dialog actions, and destructive confirmation language.
- Use stable manager terminology: Project, Tracker, Development status, Work status, Blockers,
  Reports, Complete import, Partial import, Recycle, Restore, and Permanently delete.
- Standardize busy, success, warning, validation, error, empty, filtered-empty, and no-history states.
- Keep notification surfaces compact and scrollable when messages are long.
- Verify Escape/back/close behavior follows the same nearest-surface rule throughout.
- Finish Help & SQL and Appearance settings presentation and concise in-app help for portfolio,
  tracker context, blockers, imports, reports, and destructive lifecycle actions.
- Remove unused SharePoint presentation/configuration remnants that no longer serve accepted
  functionality. Preserve safe settings migration for existing files.
- Ensure no visible Git setup/Sync placeholder remains before its future product milestone.

### Verification assets

- Update deterministic screenshot generation for the final navigation and key states.
- Refresh affected README screenshots in the established image set.
- Keep screenshot seeding isolated and never modify the user's application database.
- Update design/architecture documentation where the implemented UX structure differs from the
  pre-UX description.

## Interaction behavior

- Keyboard users can reach and operate every user-facing action.
- Escape closes the nearest transient surface without accidentally discarding committed or dirty
  work.
- Destructive default focus remains on the safe/cancel action.
- Errors preserve user input where retry is possible.
- Empty and disabled states explain the next available action.
- Theme, DPI, resizing, and long content do not hide required actions.

## Architectural constraints

- Do not move behavior into code-behind solely to fix presentation.
- Do not add a custom accessibility framework or custom control when a standard WPF control works.
- Do not change business terminology definitions, status transitions, import semantics, ranking,
  readiness, or persistence.
- Do not implement Git synchronization or another remote provider.
- Avoid stylistic refactors unrelated to observable consistency/accessibility issues.

## Tests / verification

Automated checks should cover navigation/focus state where practical, AutomationProperties presence
for defined icon-only actions, settings migration, semantic resource use, final navigation routes,
XAML regressions, screenshot generator routes, and the complete existing suite.

Manual acceptance matrix:

- keyboard-only pass of every workflow;
- Light, Dark, and System;
- minimum and wide window sizes;
- 100%, 150%, and 200% Windows scaling where available;
- empty, populated, filtered-empty, error, warning, busy, and destructive states;
- 125+ entity data and multi-Tracker comparison;
- screen-reader/automation label spot checks;
- README screenshot generation and visual review.

## Acceptance criteria

- [ ] Every workflow is keyboard operable with visible, predictable focus.
- [ ] Icon-only controls have useful accessible names and tooltips.
- [ ] Status and issue meaning never relies on color alone.
- [ ] Light/Dark/System contrast and interaction states are readable.
- [ ] Layout remains usable at supported window sizes and common DPI scaling.
- [ ] Page hierarchy, terminology, commands, notifications, and empty/error states are consistent.
- [ ] Help and Appearance presentation match the final shell.
- [ ] Obsolete SharePoint UI/configuration is safely retired.
- [ ] No non-functional Git UI is exposed.
- [ ] Deterministic screenshots and README images reflect the final interface.
- [ ] No business behavior or project dependency direction regresses.
- [ ] The complete solution builds and all tests pass.

## Out of scope

- new product workflows or status definitions;
- custom screen-reader integration beyond standard WPF automation;
- user-customizable layouts or design tokens;
- localization;
- Git synchronization;
- broad backend refactoring.

## Agent planning prompt

```text
Plan UX-08 — Accessibility and Consistency.

Read the architecture, design direction, UX-01 through UX-07, the implemented WPF Views/resources,
all presentation tests, screenshot tooling, README, and help/design documentation. Produce a
repository-specific final audit plan for UX-08 only. Identify observable inconsistencies and
accessibility gaps; do not invent new functionality or refactor for taste. Do not modify the
repository or plan Git synchronization.
```

## Agent implementation prompt

```text
Implement UX-08 according to this milestone and the approved audit plan. Fix verified accessibility
and consistency issues only, update help/design docs and deterministic screenshots, build the full
solution, and run every test. Do not add features, change accepted business semantics, or implement
Git synchronization.
```


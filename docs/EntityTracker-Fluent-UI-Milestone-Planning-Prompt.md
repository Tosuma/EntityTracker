# Prompt — Create a New Fluent UI/UX Milestone Category for EntityTracker

## Purpose

Use this prompt with a coding/planning agent to create a **new category of milestones** for redesigning the EntityTracker user interface around Microsoft's Fluent design language.

This is **not** a replacement for the existing implementation milestones or Product Feedback milestones.

The agent must treat this as a separate milestone category focused on UI architecture, information architecture, interaction design, visual consistency, Fluent design adoption, usability for Team Leads / Project Managers, and WPF presentation-layer modernization.

The agent must **not** immediately implement the redesign. Its first task is to inspect the repository, ask design questions, and then propose a milestone roadmap.

---

# Prompt to give the agent

```text
We are introducing a new milestone category for EntityTracker.

This category is separate from:
- the existing core implementation milestones;
- corrective/intermediate milestones such as 5.1 or 6.1;
- Product Feedback milestones such as PF-05.

Create a new category specifically for UI/UX modernization.

Use a clear category prefix that distinguishes these milestones from all existing milestone types.

For example:
UX-01
UX-02
UX-03

or:
UI-01
UI-02
UI-03

Do not choose the final prefix silently if there is a meaningful naming choice. Ask me if needed.

The goal of this new category is to redesign the current WPF user interface around Microsoft's Fluent design language while preserving the existing domain/application architecture and all accepted functionality.

Before creating the milestones, inspect the existing repository and documentation.

Read at minimum:
- docs/architecture/ARCHITECTURE.md
- docs/milestones/README.md
- all completed/accepted milestones relevant to the current UI
- any Product Feedback milestones that affect the overview, filtering, editing, importing, reporting, or navigation
- the current WPF project structure
- existing ViewModels, Views, dialogs, styles, resource dictionaries, and navigation patterns

Do not assume the original milestone documentation reflects every implementation decision that now exists in the repository. The repository as implemented is part of the design context.

# Design direction

The intended design direction is:

Microsoft Fluent / Fluent 2 design language
        ↓
.NET 10 WPF built-in Fluent theme
        ↓
EntityTracker UI

The application should use Microsoft's Fluent design language as the primary design reference rather than inventing a custom visual language.

The purpose is not merely to make controls look modern.

The redesign should consider:
- information hierarchy;
- navigation;
- spacing;
- typography;
- visual density;
- grouping;
- semantic colors;
- state indication;
- command placement;
- filtering UX;
- dialogs versus pages;
- overview/dashboard structure;
- consistency across workflows;
- accessibility;
- manager-oriented usability.

The technical baseline should prefer the built-in .NET 10 WPF Fluent theme where practical.

Do not replace WPF simply to obtain Fluent styling.

Do not propose a WinUI or Avalonia rewrite unless the existing WPF implementation presents a concrete blocker that cannot reasonably be solved in WPF.

Do not introduce a third-party Fluent component framework as the architectural foundation unless there is a concrete requirement that the built-in WPF Fluent support cannot satisfy.

If a third-party component is later needed for an isolated capability, treat that as a specific design/technical decision rather than the default.

# Important architectural rules

The redesign must respect the existing architecture.

In particular:
- WPF remains the presentation layer.
- Business logic must stay outside WPF.
- Domain/Application behavior must not move into Views or code-behind.
- Existing ViewModels should remain focused on presentation/orchestration.
- Ranking, readiness, synchronization, persistence, dependency logic, and validation remain outside the UI.
- Do not duplicate business rules merely to simplify presentation.
- Existing functionality must continue working unless a UI milestone explicitly changes its interaction design.
- UI modernization must not silently rewrite backend architecture.
- Do not create a generic design-system framework beyond what the project actually needs.

The goal is a better interface, not an architectural reset.

# Fluent design considerations

Use Fluent as a design system rather than merely a theme.

The milestone plan should consider introducing or standardizing:
- application-level Fluent theme usage;
- consistent design resources / design tokens;
- spacing scale;
- typography scale;
- semantic colors;
- corner radius usage;
- borders/strokes;
- elevation where appropriate;
- focus states;
- hover/pressed/disabled states;
- light/dark/system theme behavior if appropriate;
- reusable presentation styles;
- consistent control sizing;
- accessible contrast and keyboard interaction.

Avoid arbitrary values scattered throughout XAML where a shared semantic resource would be better.

Do not over-engineer a large custom design-token framework if ordinary WPF resources are sufficient.

# Information architecture considerations

Do not assume the current screen layout should simply be restyled.

Before defining milestones, evaluate the application's information architecture.

Consider where the following functionality should live:
- Overview
- Ready
- Blocked
- Archived
- Reports / charts
- Import CSV
- Add Entity
- Show SQL Query
- Entity details
- Editing
- Filtering
- Search
- Status summaries
- unresolved dependencies
- synchronization review
- manual entity creation
- dependency inspection
- manager-focused reporting actions

Determine whether each belongs as:
- persistent navigation;
- primary page content;
- contextual command;
- toolbar action;
- details pane;
- modal dialog;
- flyout;
- secondary page.

Do not place every action in the main navigation simply because it exists.

Do not hide frequently used commands behind unnecessary dialogs.

# Manager / Team Lead usability

The primary UI should be usable by a Team Lead or Project Manager who is not expected to understand the internal architecture.

The overview should make high-value information easy to scan.

Examples include:
- current status distribution;
- implementation readiness;
- blockers;
- unresolved dependencies;
- meaningful ranking;
- changed/new entities after synchronization;
- progress information;
- filtering/search;
- report-oriented outputs.

Use plain, stable terminology.

Avoid exposing internal class names, database implementation details, or technical concepts unless useful to the user.

The UI should emphasize:
"What needs attention?"
"What can be implemented now?"
"What is blocked?"
"What changed?"
"How far along are we?"

rather than:
"How is the software internally modeled?"

# Existing filtering behavior

Existing Product Feedback milestones are part of the accepted interaction behavior.

For example, if multi-status filtering has already been accepted:
- preserve its semantics;
- improve its presentation if appropriate;
- do not regress from multi-select back to single-select;
- keep Ready, Blocked, and Archived behavior consistent with the accepted product rules.

UI milestones may change how controls are presented, but should not silently change accepted filtering semantics.

# Before creating the milestone roadmap: ask design questions

Do not immediately generate the final UI milestone files.

First inspect the repository.

Then ask me a focused set of design questions.

Ask only questions that materially affect:
- information architecture;
- navigation;
- visual density;
- workflow;
- manager usability;
- difficult-to-reverse UI decisions.

Do not ask questions about trivial implementation details that can be handled idiomatically.

Group related questions together.

Prefer a small number of meaningful questions over a large questionnaire.

# Topics you should actively clarify

Ask me about these areas if the repository does not already make the answer clear.

## 1. Navigation model

Examples:
- Should the app use persistent left-side navigation?
- Should Overview, Ready, Blocked, Archived, and Reports be separate navigation destinations?
- Should Ready/Blocked be filters within Overview instead?
- Should import/add/query actions be navigation items or commands?

Do not decide this solely from the current UI if there is room for a better information architecture.

## 2. Overview density

Clarify whether the desired style is closer to:
- dense productivity software;
- spacious dashboard;
- a hybrid.

EntityTracker manages potentially many entities, so density matters.

Ask whether the user prefers a data-dense Microsoft 365/admin-style interface or a more spacious modern app layout.

## 3. Primary overview content

Ask which information should be immediately visible in the main entity table.

Possible fields include:
- Rank
- Entity name
- Development status
- Ready / Blocked / Unresolved
- Dependency count
- Missing dependencies
- Progress
- Assignment / responsible developer if implemented
- Last change
- downstream impact

Do not assume all fields belong in the default table.

## 4. Details interaction

Clarify whether selecting an entity should:
- open a right-side details pane;
- open a dedicated details page;
- open a dialog;
- expand the row.

This will influence several later workflows.

## 5. Editing interaction

Clarify whether editing should happen:
- inline;
- in a details pane;
- in a modal dialog;
- on a dedicated page.

Consider that Milestone 7 and later functionality may already provide editing behavior.

## 6. Import workflow

Clarify how prominent Import CSV should be.

Consider:
- toolbar command;
- primary button;
- separate import page;
- menu action.

The synchronization review itself may deserve a focused page/dialog distinct from the entity overview.

## 7. Add Entity workflow

Clarify whether Add Entity should be:
- a primary command near Import;
- a contextual command;
- a separate page;
- a modal dialog.

Do not redesign the underlying manual creation semantics.

## 8. Theme behavior

Ask whether the application should:
- follow Windows system theme;
- provide Light/Dark/System selection;
- remain light-only initially.

Do not implement theme configuration unless it belongs in an approved UI milestone.

## 9. Status color usage

Clarify how strongly status should be color-coded.

Prefer color as a supporting signal, not the only signal.

Ask whether badges / pills / icons / text labels are preferred.

Accessibility must be considered.

## 10. Reporting/dashboard

Clarify whether reports/charts should eventually be:
- a separate Reports destination;
- part of Overview;
- a dashboard home page.

The design should make later chart/report milestones fit naturally.

# After questions are answered

After I answer the design questions:
1. Summarize the design decisions.
2. Identify any conflicts with the current WPF implementation.
3. Propose the new UI/UX milestone category.
4. Create a milestone roadmap in small, independently reviewable increments.
5. Do not implement anything yet.

# Milestone construction rules

The new UI/UX milestones should be iterative.

Do not create one milestone called:
"Redesign the entire UI with Fluent."

Each milestone should leave the application:
- buildable;
- runnable;
- visually coherent;
- functionally usable.

Prefer milestones that can be manually reviewed.

For example, the roadmap may include stages such as:
- Fluent foundation / theme / resource cleanup;
- application shell and navigation;
- overview information hierarchy;
- filtering/search redesign;
- entity details presentation;
- import/review workflow redesign;
- add/edit entity workflow redesign;
- status/readiness visualization;
- reporting/dashboard integration;
- accessibility and final consistency pass.

These are examples only.

Do not copy them mechanically if the repository suggests a better decomposition.

# Avoid visual half-migrations

When planning milestones, avoid leaving the app in a state where:
- half the controls use old visual language;
- half use new styles;
- navigation is duplicated;
- two competing interaction patterns coexist without reason.

If necessary, group tightly coupled presentation changes into one milestone to preserve coherence.

At the same time, do not create an excessively large milestone.

Balance incremental delivery against visual consistency.

# New milestone category requirement

This is important:

These milestones are a **new category**.

Do not number them as continuations of the core implementation roadmap.

Do not use:
Milestone 13
Milestone 14
Milestone 15

unless I explicitly request that.

Do not use the Product Feedback `PF-` prefix.

Use a separate category/prefix dedicated to UI/UX redesign.

Before finalizing, either:
- ask me which prefix I want; or
- propose one clear prefix and ask me to approve it.

Examples:
UX-01
UX-02
UX-03

or:
UI-01
UI-02
UI-03

The category should remain usable for future UI/UX milestones beyond this initial Fluent redesign.

# Milestone file format

Each milestone should be produced as its own Markdown file.

Use a consistent filename convention such as:

UX_01_fluent_foundation.md
UX_02_application_shell.md
UX_03_overview_redesign.md

or equivalent based on the approved prefix.

Each milestone file should include:

## Title

## Context

What accepted application state this milestone builds on.

## Goal

A concise outcome.

## User-facing outcome

What the Team Lead / Project Manager should notice after completion.

## Design decisions

Relevant Fluent / information architecture decisions already agreed upon.

## Required implementation

Concrete work included in the milestone.

## Interaction behavior

Explicit expected behavior for controls and workflows.

## Architectural constraints

What must remain outside WPF and what existing behavior must be preserved.

## Tests / verification

Include both:
- automated checks where meaningful;
- manual UI verification steps.

## Acceptance criteria

Use Markdown checkboxes.

## Out of scope

Explicitly prevent scope creep.

## Agent planning prompt

Provide a Plan-mode prompt for that specific milestone.

## Agent implementation prompt

Provide an implementation prompt for that specific milestone.

# Testing expectations

UI milestones must not rely only on visual inspection.

Where practical, test:
- ViewModel behavior;
- filtering state;
- navigation state;
- command enablement;
- presentation transformations;
- existing business behavior regression.

Do not move business logic into ViewModels merely to make it easier to test.

Manual verification should cover:
- visual hierarchy;
- keyboard navigation;
- resizing;
- high entity counts;
- empty states;
- error states;
- light/dark theme if supported;
- status readability;
- unresolved dependency readability.

# Accessibility

The milestone roadmap must explicitly consider accessibility.

At minimum:
- keyboard navigation;
- focus visibility;
- color contrast;
- status not communicated through color alone;
- useful labels/tooltips where icon-only actions exist;
- sensible tab order;
- readable text at normal Windows scaling;
- reasonable behavior under DPI scaling.

Do not defer all accessibility to one final cleanup milestone if earlier UI decisions would make it difficult.

# WPF-specific guidance

The project is currently WPF on .NET 10.

Prefer:
- built-in WPF Fluent theming;
- WPF resource dictionaries;
- standard WPF controls where they satisfy the interaction need;
- existing MVVM structure;
- existing dependency injection/composition patterns.

Do not introduce a large third-party UI framework unless a milestone identifies a concrete missing capability and the tradeoff is explicitly reviewed.

Keep any custom styles small and semantic.

Do not recreate WinUI controls from scratch merely for visual imitation.

# Preserve existing behavior

The redesign must preserve accepted functional behavior unless I explicitly approve a change.

Examples include:
- ranking semantics;
- unresolved dependency handling;
- Complete vs Partial synchronization;
- actionable diff review;
- manual entity creation semantics;
- status filtering semantics;
- Ready / Blocked / Archived behavior;
- persistence behavior;
- status definitions;
- readiness rules.

If the agent believes a better UI requires changing product behavior:
1. stop;
2. explain the proposed behavior change;
3. explain why it improves the workflow;
4. ask for approval.

Do not silently reinterpret backend behavior as a UI redesign.

# Final output after design questions are answered

After the design discussion is complete, provide:
1. A short UI Design Direction document.
2. The chosen new milestone category/prefix.
3. A roadmap/README for the UI/UX milestone category.
4. One Markdown file per proposed UI/UX milestone.
5. Recommended execution order.
6. Any dependencies between UI/UX milestones and existing core/Product Feedback milestones.
7. Any Product Feedback items that should be completed before a particular UI milestone.
8. A note identifying which milestones are primarily:
   - visual foundation;
   - information architecture;
   - interaction/workflow;
   - polish/accessibility.

Do not implement the milestones as part of this planning task.

The purpose of this task is to produce a thoughtful, reviewable UI/UX roadmap that can later be executed milestone by milestone.
```

---

# Suggested use

Give the entire prompt above to your agent in planning mode.

The intended workflow is:

```text
Agent inspects repository
        ↓
Agent asks UI/design questions
        ↓
You answer
        ↓
Agent summarizes decisions
        ↓
Agent proposes the new UI/UX milestone category
        ↓
You review the roadmap
        ↓
Agent writes individual milestone Markdown files
        ↓
Implementation begins later, one UI/UX milestone at a time
```

Do not ask the agent to both design the entire roadmap and immediately implement the first UI milestone in the same session.

Treat the UI/UX roadmap as a design artifact that should be reviewed before implementation.

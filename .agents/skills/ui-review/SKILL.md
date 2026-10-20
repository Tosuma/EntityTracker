---
name: ui-review
description: Review a C# WPF desktop application using Microsoft's built-in Fluent theme and Fluent System Icons. Audit persistent navigation, menus, every discovered view, context-sensitive commands, UX flows, consistency, and accessibility. Deliver a prioritized report with practical alternatives under docs/review/. Assessment only: while reviewing, it changes nothing but the new report. Implementing its recommendations is a separate request (deliver-change).
---

# WPF + Fluent UI review (report only)

## Purpose
Review the current WPF desktop user interface as a product and design system. Evaluate the **entire persistent navigation + content-area experience**, including the information architecture, the purpose and placement of each menu item, and the structure of each discovered screen. Identify strengths, usability issues, inconsistencies, accessibility gaps, and ways to make workflows and styling simpler and more standardized. Produce actionable recommendations and trade-offs **without implementing them**.

**Review what exists.** Do not hard-code a specific button, workflow, presumed defect, or preferred redesign into the audit. Problems and solutions must emerge from observed behavior and evidence. A context-dependent command in persistent navigation is *not automatically wrong*; evaluate whether users can predict its scope and effect.

## Non-negotiable boundaries
These boundaries hold **while performing a review**. They are not general repository rules: when the
user later asks to implement findings, that is a separate request handled by the `deliver-change` skill.
Write the review in English.

1. **Do not modify** application code, XAML, styles, resource dictionaries, images/icons, configuration, dependencies, tests, or documentation other than the new report.
2. **Allowed writes only:** create `docs/review/` if missing, then write **one new `.md` review file** into it. Do not modify or overwrite existing reports or other files.
3. Do not run formatters, package installers, migrations, builds, or tests that change the working tree. Read-only commands and searches are acceptable. Inspect existing screenshots or a running UI only when already available or explicitly permitted; do not assume screenshots were captured.
4. Never confuse source-code inference with visual or interaction testing. Never claim WCAG compliance or accessibility test success without actual evidence.
5. Recommend modifications in the report only. Do not implement fixes during this skill, even if they look trivial.

## Stage 1 — Discover the actual desktop stack and scope
- Identify solution/project structure (`.sln`, `.slnx`, `.csproj`), WPF windows/pages/user controls, view models, and navigation pattern.
- The app is described as having a persistent side area and a page-dependent main work area. Confirm this from `MainWindow`, shell/container XAML, navigation services, view-models and commands; discover actual elements rather than assuming names or controls.
- Build an **inventory of all discovered top-level destinations, secondary navigation items, shell actions, and content views**. Inspect how menu item labels/icons map to actual commands or destinations, including runtime/context-dependent behavior when it can be determined from source.
- Confirm the configured **Microsoft built-in WPF Fluent theme** (for example `ThemeMode` or `PresentationFramework.Fluent` when present) and how **Microsoft Fluent System Icons** are represented and bound. Do not substitute third-party WPF UI assumptions, React/WinUI APIs, or incorrect icon packages.
- Locate `App.xaml`, merged `ResourceDictionary` files, themes, brushes, typography, styles, control templates, shared components, icon wrappers, and relevant design documentation.
- Determine primary workflows, audience, frequently used screens, and error/empty/loading states from available evidence. Where unknown, flag assumptions.
- Discover any existing screenshot-capture utility or configuration **by reading its source/settings only**. Identify its output location when possible, but do not run it or assume a specific filename or directory.
- Review every discoverable screen in scope, rather than only a representative subset. Start with the application shell, enumerate screens, review each screen, then compare them cross-screen for consistency. Include dialogs and non-page interaction surfaces when discoverable. If the app is too large to cover exhaustively, explicitly list what was and was not reviewed and why.

## Stage 1A — Find and map existing UI screenshots (required)
- Search for existing screenshots **throughout the repository**, prioritizing likely folders such as `images/`, `Images/`, `screenshots/`, `Screenshots/`, `docs/images/`, and capture-tool output directories. These are **hints, not required paths**; the actual folder name and nesting are unknown. Look for relevant image formats (`.png`, `.jpg`, `.jpeg`, `.webp`, and `.bmp` where supported). Exclude build caches, generated dependencies, and irrelevant assets from review scope.
- Distinguish **UI screenshots** from icons, logos, illustrations, design resources, and test fixtures. Use capture-tool source/configuration, filenames, folder grouping, and actual image contents when available to determine which images represent the application UI.
- Read relevant existing images with image-capable inspection where available; merely discovering a file path does **not** count as visually reviewing its contents. When image inspection is not possible, record the limitation and avoid claiming to have visually verified it.
- Create an internal mapping of screenshot -> window/view/dialog/state -> related XAML, command and view-model files. Reconcile screenshots with the source implementation to understand what users see **and** what controls actually do. If a screenshot cannot be confidently mapped, label it as uncertain rather than guessing.
- Prioritize coverage of the persistent sidebar/app shell, each discoverable content page, dialogs, open menus, context-dependent commands, and relevant interaction states. Use the screenshot collection systematically, not just the first image found. Prefer the most relevant/current captures if duplicate or historical captures exist; note evidence of staleness when identifiable. If freshness is unknown, say so.
- **Never** run the screenshot utility or generate, rename, alter, remove, or overwrite images as part of the review. The only permitted output is the one new Markdown report in `docs/review/`.

## Stage 2 — Collect verifiable evidence
- Read relevant XAML and view-model/code-behind only to understand UI behavior and interaction; avoid a general-purpose code architecture review.
- Use the **discovered existing screenshot set**, if any, as visual evidence. Cross-check visible layout, labeling, grouping, navigation state, affordances, and command placement against the XAML and C# implementation. If no screenshot can actually be inspected, explicitly label the output as a **source-based review**.
- Reference screenshots by repository-relative path (for example, `images/<capture>.png` **only when that path actually exists**) and state which view or condition each image supports. Report screenshot coverage, missing views/states, potential staleness, and uncertain mappings.
- For every important finding, record a screen/control and a repository-relative file path; include line numbers when reliable. Explain whether the finding is:
  - **Verified visually** (observed in a screenshot or actual UI);
  - **Verified in source** (e.g., a conflicting style or missing property is directly present in XAML); or
  - **Hypothesis / needs testing** (e.g., an inferred layout failure at 150% scaling).
- Do not invent UI appearance, interactions, measurements, or testing results.

## Stage 2A — Audit the persistent navigation and application shell (required)
Examine the persistent side area and its relationship to the currently displayed content, rather than reviewing it only as a styled list of buttons.

1. **Information architecture:** Identify the meaning of every navigation destination or grouping. Are the labels understandable, mutually distinguishable, and organized around user tasks? Is the menu ordering appropriate to frequency, importance, and workflow? Are there unnecessary or missing destinations? Can users tell where they are and return to previous work?
2. **Navigation versus commands:** Determine which items change location, which execute actions, and which open menus or dialogs. Is that distinction predictable from their location, appearance, and wording? Does persistent app-level chrome contain actions whose effect depends on the selected screen or selection state? If so, is their scope obvious at the moment of use?
3. **Semantic fit:** For **each** actionable element, compare its visible text, icon, tooltip, enabled/disabled state, and placement with the actual implementation and user-facing effect. Ask whether a new user would predict what happens before clicking it. Validate whether Fluent System Icons express the intended meaning, including whether labels are needed.
4. **Discoverability and feedback:** Are current location, selected states, relevant context, unavailable actions, unsaved changes, and the result of commands communicated clearly? Consider how context changes and how users realize that an action will affect a different object or screen.
5. **Placement alternatives:** Where confusion or friction is supported by evidence, compare at least two plausible patterns (for example, persistent shell action, contextual page toolbar, local in-content action, contextual menu, or clearer naming/labeling). Explain benefits and trade-offs, including consistency, visibility, speed, scalability, and development effort. **Do not prescribe relocating actions by default.**
6. **Cross-screen behavior:** Compare sidebar interactions across all discovered pages, particularly controls that retain their position but have context-dependent effects. Record whether behavior, iconography, labels, affordances, focus and keyboard access are consistent.

Produce a **navigation/command inventory** in the report: each discovered item, its inferred or observed purpose, scope (app-wide, destination, page-specific, selected-object-specific, or unknown), source evidence, and usability assessment. If any purpose cannot be determined from source, label it unknown rather than inventing behavior.

## Stage 2B — Review every content view in its actual shell context (required)
For each discovered view or screen, record its purpose and primary tasks, identify its main sections and action hierarchy, evaluate the clarity of layout and user flow, and check how the persistent navigation influences the screen. Examine the appropriateness of control placement, labels, icon meanings, empty/loading/error states, and transitions to/from other screens. Compare repeated concepts across views for standardization opportunities. Identify strengths as well as weaknesses; avoid adding a finding merely to populate every view.

Do not confuse discoverable source structure with rendered appearance: any claims about visual balance, actual clipping, rendered density, or interaction outcomes must be verified using a screenshot or running UI or explicitly marked as hypotheses.

## Stage 3 — Evaluate using WPF-appropriate criteria

### A. Windows desktop interaction and usability
- Navigation clarity, discoverability, information architecture, the distinction between global navigation and contextual commands, sensible command placement, menu/context menu behavior, and consistent dialogs.
- Efficiency for expert users: keyboard shortcuts, accelerators, tab order, focus visibility/return, default/cancel actions, predictable Enter/Escape behavior, and reduced mouse travel.
- Selection versus activation, confirmation for destructive actions, undo/recovery, validation, progress/status feedback, empty and error states.
- Clear labeling and helpful microcopy, consistent terminology, accurate control labels/icons relative to their behavior, and efficient workflows without unnecessary dialogs or steps.

### B. Fluent visual consistency
- Visual hierarchy, content density, grouping, spacing, alignment, padding/margins, typography scale, readability, and coherent primary/secondary actions.
- Consistent Fluent-inspired colors, elevation/surfaces, corner radii, state treatments, and theme semantics **as supported by this project's actual WPF library**.
- Repeated hard-coded XAML values versus shared styles/resources. Suggest design tokens/resource dictionary consolidation only where it reduces inconsistency without undue complexity.
- Hover, pressed, selected, disabled, loading, error, and focus states where supported by the evidence.
- Do not demand web-only Fluent components or prescribe WinUI/React control APIs to a WPF app.

### C. Icons and visual language
- Identify the icon source(s) used; check whether there are mixed icon families or competing metaphors.
- Consistency of regular/filled variants, visual weight, size, baseline alignment, spacing with text, semantic clarity, and selected-state behavior.
- Check icon-only commands for accessible names/tooltips and discoverability; suggest accompanying labels where meaning is unclear.
- Prefer icons already available in the application's chosen Fluent icon library. Do not recommend replacing its entire icon set without a clear justification.

### D. WPF-specific responsiveness and accessibility
- `Grid` / `DockPanel` / `StackPanel` / `ScrollViewer` sizing behavior; avoid problematic fixed dimensions, clipping, overflow, and dense layouts where evidence supports the concern.
- Window resize behavior, minimum dimensions, high-DPI / per-monitor scaling, Windows text scaling, theme variants, and High Contrast considerations.
- Keyboard focus and `KeyboardNavigation`, `AccessText`/access keys, labels, tab sequence, and logical navigation.
- UI Automation (`AutomationProperties.Name`, labeled-by relationships, control accessibility and automation peers where relevant), tooltips, and screen-reader semantics.
- Contrast and text legibility; use WCAG 2.2 AA as a **reference** where relevant, not an unverified compliance badge.
- Lists, tables, tree views, and complex controls: selection affordances, column behavior, empty states, and virtualization/performance issues **only if grounded in the UI evidence**.

### E. Simplification and standardization
- Find repeated UI patterns that would benefit from shared WPF `Style`, `ControlTemplate`, `DataTemplate`, resources, or a reusable `UserControl`.
- Spot inconsistent naming, status indicators, actions, button hierarchies, icon usage, dialog structures, and spacing conventions across views.
- Recommend a common Fluent-aligned pattern and explain migration cost and any situations where intentional differences should remain.
- Prefer a small number of high-leverage improvements over speculative redesigns or arbitrary “make it modern” advice.

## Stage 3F — Compare architectural design alternatives
- Evaluate the **existing** persistent-sidebar + work-area structure on its merits: whether it suits the app’s workflows, which parts are effective, and which could be simplified, regrouped, clarified, or moved to a more context-appropriate surface.
- Where useful, compare alternative arrangements without assuming a full redesign is desirable. Explain which changes require no new controls and which require more involved design work.
- Identify relationships among issues: for example, ambiguous navigation categories, repeated commands, inconsistent naming, and unclear screen-specific ownership may share a root cause.
- Use Microsoft Fluent and Windows navigation/command guidelines as **design references**, while ensuring suggestions are implementable in WPF with the actual controls used by the app; never assume WinUI NavigationView or CommandBar is present.

## Stage 4 — Rank findings and explain trade-offs
- Include **what already works** and should be preserved.
- Prioritize each issue:
  - **High:** impedes a core task, creates serious confusion/accessibility barriers, or causes likely user errors.
  - **Medium:** recurring inconsistency/friction or meaningful opportunity to standardize.
  - **Low:** polish or limited-context improvement.
- For each improvement give the concrete observation, why it matters, a practical WPF/Fluent-aligned recommendation, advantages, disadvantages/costs, and level of confidence.
- Give 3–7 top recommendations when justified by the evidence; fewer if the repository provides little evidence. Avoid manufactured problems and unsupported scores.
- Separate **quick wins** from **structural design-system recommendations**; distinguish verified issues from ideas to test.

## Stage 5 — Write the Markdown report (required)
1. From the **repository root**, create `docs/review/` if it does not exist.
2. Name the report `docs/review/YYYY-MM-DD-ui-review.md` using the date of the review. If that file already exists, create `docs/review/YYYY-MM-DD-ui-review-02.md`, then `-03`, etc. **Never overwrite** a previous review.
3. Write a substantive report **in English** using the template below. It must still be useful if screenshot or runtime testing is unavailable, and it must include a navigation item audit and per-view assessment where evidence is available.
4. After writing, verify the report exists and that no other files were modified by this review (compare working-tree state before/after if Git is available). Existing unrelated changes must remain untouched. If the report cannot be written because of permissions, state that limitation and provide the report in the response rather than silently omitting it.
5. In the user-facing reply, give the exact repository-relative Markdown path, summarize the main themes, and confirm no application code was changed.

### Required Markdown report template

```markdown
# WPF / Fluent UI Review

**Date:** YYYY-MM-DD  
**Scope:** [screens / workflows / entire app]  
**Review type:** [visual + source / source-only; use visual only for images actually inspected]  
**Fluent implementation:** [actual identified library and icon source, or unknown]

## Executive summary
[Overall assessment and top opportunities, without invented ratings.]

## Scope, method, and limitations
- **Reviewed:** [views, controls, styles, actually inspected screenshots with repository-relative paths]
- **Screenshot discovery:** [actual screenshot folders found, screenshot generator output folder if identified, screenshot-to-view coverage and capture freshness/unknowns]
- **Not verified:** [runtime behaviors, resolutions, accessibility testing, etc.]
- **Assumptions:** [clearly stated]

## What is working well
- [Specific strength + supporting evidence; include shell/navigation strengths]

## Application structure and navigation model
- **App shell:** [persistent UI elements and their relation to content]
- **Navigation model:** [how screens change and state/context is tracked]
- **Assessment:** [whether the overall structure supports core workflows and why]

### Navigation and command inventory
| Item / control | Location | Actual destination or behavior | Scope / context | Label / icon fit | Evidence & confidence | UX assessment |
|----------------|----------|--------------------------------|-----------------|------------------|-----------------------|---------------|
| ... | ... | ... | App-wide / page / selection / unknown | ... | `path/to/File.xaml` etc. | ... |

[Explain grouping, ordering, command versus navigation separation, clarity of actions, and alternatives worth exploring. Do not assume any particular item is a problem.]

## Screenshot inventory and coverage
| Screenshot (repository-relative path) | Mapped view / state | Source cross-check | Evidence quality / caveats |
|---------------------------------------|---------------------|--------------------|----------------------------|
| ... | ... | ... | Visually inspected / Not inspectable / Mapping uncertain / Possibly outdated |

[Include relevant inspected captures and explicitly identify important views or states for which no suitable screenshot was found. Do not list non-screenshot assets as UI evidence.]

## Screen-by-screen review
### [Screen / view name]
- **Purpose and main tasks:** ...
- **Layout and information hierarchy:** ...
- **Primary and secondary actions:** ...
- **Relationship to persistent navigation:** ...
- **What works:** ...
- **Potential improvements / supporting evidence:** ...
- **Screenshots inspected:** [repository-relative paths / none]
- **Confidence and limitations:** ...

[Repeat for each discovered screen. Explicitly list unreviewed screens and why, if any.]

## Cross-screen workflows and consistency
[Compare how navigation, commands, icons, labeling, status/feedback, and repeated components behave across views.]

## Priority overview
| ID | Priority | Area | Finding | Effort | Confidence |
|----|----------|------|---------|--------|------------|
| UI-001 | High / Medium / Low | ... | ... | Small / Medium / Large / Unknown | Verified visually / Verified in source / Needs testing |

## Findings and recommendations
### UI-001 — [Concise issue name]
- **Priority:** ...
- **Where / evidence:** [screen, `path/to/File.xaml:line`, screenshot if any]
- **Observation:** ...
- **User impact:** ...
- **Recommendation:** [concrete WPF / Fluent-aligned design change; do not implement]
- **Benefits:** ...
- **Trade-offs / costs:** ...
- **Estimated effort:** Small / Medium / Large / Unknown
- **Confidence:** Verified visually / Verified in source / Needs testing

## Navigation and command-placement alternatives
[For substantiated shell/menu/command problems, offer plausible options, expected effects, and trade-offs; do not prescribe a preselected redesign.]

## Standardization opportunities
[Shared styles, resources, icon patterns, control conventions, navigation and command-placement conventions; avoid duplication with findings.]

## Recommended sequence
1. **Quick wins:** ...
2. **Structural changes:** ...
3. **Validate with screenshots or users:** ...

## Items to validate
[Unknowns / recommended accessibility, DPI, keyboard, or usability tests.]

## Review boundaries
This report contains recommendations only. No application code, XAML, assets, dependencies, or settings were changed as part of this review.
```

If the evidence is too thin for some sections, write `Not assessed` and explain why rather than inventing findings.

## Example request
`$ui-review Review the entire WPF desktop app, including its persistent sidebar, meaning and behavior of each menu item, navigation versus contextual commands, and every discovered content view. Evaluate user workflows, built-in Fluent consistency, Fluent System Icons, accessibility, and cross-screen standardization. Identify strengths and concrete improvements with alternatives and trade-offs. Do not implement changes. Save the English report to docs/review/.`

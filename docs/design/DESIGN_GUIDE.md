# EntityTracker design guide

This guide defines the visual language for EntityTracker's WPF interface and generated reports.
The application uses the built-in .NET 10 WPF Fluent theme as its control baseline. Use semantic
resources instead of choosing colors directly in individual screens or controls.

## Brand palette

| Name | Hex | Intended use |
| --- | --- | --- |
| Dark Green | `#141E1E` | Brand dark surface and report text |
| Green 100% | `#123836` | Primary actions, dark surfaces, reconciled work |
| Green 90% | `#2A4C4A` | Primary-action hover state |
| Green 80% | `#41605E` | Strong borders, in-progress and ready states |
| Green 60% | `#718886` | Development-completed state and supporting graphics |
| Green 40% | `#A0AFAF` | Not-started state and neutral graphics |
| Green 30% | `#B8C3C3` | Report support color |
| Green 20% | `#D0D7D7` | Report chart grids |
| Green 10% | `#E7EBEB` | Text on dark green and report backgrounds |
| White | `#FFFFFF` | Text on dark green and report/chart canvas |
| Coral | `#FF6359` | Rework, blocked/error states, destructive actions |

The application icon is existing artwork and is not recolored by this guide.

Synchronization review uses one additional amber warning palette for retained, non-fatal
unresolved dependencies. Its centralized header and border color is `#D9922E`; its surface is a
translucent tint over the current theme surface and its text follows the active system text color.
These values are not status colors and must not replace Coral for errors, removal, destructive
actions, or blocked states.

## Contrast and text

Normal text must meet a contrast ratio of at least 4.5:1. Application text, secondary text, cards,
page surfaces, borders, selection, and focus derive from the active Fluent/System resources so they
remain legible in Light, Dark, and System modes. The fixed brand combinations below remain the
supported defaults:

- Dark Green text on White, Green 10%, Green 20%, Green 30%, Green 40%, Green 60%, or Coral.
- White or Green 10% text on Green 80%, Green 90%, or Green 100%.

Do not use White text on Green 40% or Green 60%, or Green 10% text on Green 60%; those
combinations do not provide enough contrast for normal text. Prefer Dark Green text on those
lighter backgrounds. Use White text on the primary Green 100% background.

Secondary application text uses the active system secondary/disabled text color. Do not
communicate a status by color alone: pair color with a name, icon, count, or other textual
explanation.

## Appearance modes

- `System` is the default and follows the Windows light/dark setting through WPF's built-in theme.
- `Light` and `Dark` pin the application to that appearance.
- The local choice is stored in `settings.json`; it is not Project or Tracker data.
- Theme changes apply to open application-owned surfaces immediately. Native Windows dialogs and
  chrome may continue to use system-owned presentation.
- The report palette and PNG output never derive colors from the live application theme.

## Semantic colors

Status colors are deliberately shared by the overview, progress dashboard, and PNG exports:

| Meaning | Color | Notes |
| --- | --- | --- |
| Not started | Green 40% | Neutral status; use Dark Green text over the color |
| In progress | Green 80% | Active work; use White text over the color |
| Rework needed | Coral | Attention state; use Dark Green text over the color |
| Development completed | Green 60% | Complete but not reconciled; use Dark Green text over the color |
| Reconciled | Green 100% | Fully implemented/reconciled; use White text over the color |
| Ready | Green 80% | Dependency-ready work |
| Blocked/error/destructive | Coral | Always add a label, icon, or explanatory message |
| Unresolved import warning | Warning palette | Retained non-fatal references in synchronization review only |

Coral is the primary accent color. Reserve it for conditions or actions that deserve attention
rather than using it as decoration. The synchronization-review warning palette is the sole
exception and distinguishes retained, non-fatal unresolved references from removal/error states.
Destructive actions require confirmation where the workflow already calls for it.

## Components and interaction states

- Pages, cards, controls, hover, selection, borders, focus, disabled states, and tooltips use the
  built-in Fluent behavior plus theme-aware semantic Fluent resources.
- Primary buttons use Green 100% with White text. Secondary buttons use the active card/text
  resources. Destructive buttons use Coral with Dark Green text.
- Normal controls have a 32-pixel baseline height, data rows are 36 pixels, and table headers are
  40 pixels. Layout spacing follows a 4/8/12/16/24 scale; normal cards use a 6-pixel radius.
- Keyboard focus must remain visibly distinct in every appearance. Popups and editor overlays move
  focus into their first useful field and restore it to the invoker when closed.
- Informational overlays use a translucent Dark Green scrim.
- Missing/removal and error states use Coral with Dark Green explanatory text. Synchronization
  review uses a pale Coral fill for possibly removed entities and the dedicated yellow warning
  palette for retained unresolved dependencies.
- Charts use Dark Green for axes and labels, Green 20% for grid lines, the status mapping above for
  categories, Green 80% for positive/ready trends, and Coral for negative/blocked trends.

## Implementation rules

WPF resources are composed by
[`src/EntityTracker.Wpf/Themes/EntityTrackerTheme.xaml`](../../src/EntityTracker.Wpf/Themes/EntityTrackerTheme.xaml)
from palette/surface, typography/spacing, and component dictionaries. The component layer adjusts
density and semantic command importance without replacing built-in Fluent control templates.
Feature XAML should reference semantic brush keys such as `Brush.Text.Primary`,
`Brush.Surface.Card`, `Brush.Status.ReworkNeeded`, or the shared button styles. Do not add raw hex
values to feature XAML.

Reporting is intentionally independent of WPF. Its matching SkiaSharp values live in
[`src/EntityTracker.Reporting/ProgressChartPalette.cs`](../../src/EntityTracker.Reporting/ProgressChartPalette.cs).
This small duplication preserves project boundaries. Any shared brand or semantic status change
must update the WPF theme, reporting palette, this guide, and the reporting palette tests together.
Review-only warning colors do not belong in Reporting.

Live charts sit on an explicit light report canvas inside theme-aware application cards so their
fixed Reporting labels remain readable in Dark mode. Native Windows chrome and operating-system
dialogs may retain system colors. New application-owned screens, overlays, charts, and exported
visual reports must follow this guide.

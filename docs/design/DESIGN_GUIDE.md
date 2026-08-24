# EntityTracker design guide

This guide defines the visual language for EntityTracker's WPF interface and generated reports.
Use semantic resources instead of choosing colors directly in individual screens or controls.

## Brand palette

| Name | Hex | Intended use |
| --- | --- | --- |
| Dark Green | `#141E1E` | Primary text on light surfaces |
| Green 100% | `#123836` | Primary actions, dark surfaces, reconciled work |
| Green 90% | `#2A4C4A` | Primary-action hover state |
| Green 80% | `#41605E` | Strong borders, in-progress and ready states |
| Green 60% | `#718886` | Development-completed state and supporting graphics |
| Green 40% | `#A0AFAF` | Not-started state and neutral graphics |
| Green 30% | `#B8C3C3` | Medium borders |
| Green 20% | `#D0D7D7` | Subtle borders, hover/selection surfaces, chart grids |
| Green 10% | `#E7EBEB` | Page and subtle backgrounds; text on dark green |
| White | `#FFFFFF` | Cards and text on dark green |
| Coral | `#FF6359` | Rework, blocked/error states, destructive actions |

The application icon is existing artwork and is not recolored by this guide.

Synchronization review uses one additional warning palette for retained, non-fatal unresolved
dependencies. Its centralized values are Warning Header `#9A5B1B`, Warning Surface `#FFF8DC`,
Warning Border `#E5D28B`, and Warning Text `#6E5A18`. These values are not status colors and must
not replace Coral for errors, removal, destructive actions, or blocked states.

## Contrast and text

Normal text must meet a contrast ratio of at least 4.5:1. The combinations below satisfy that
requirement and are the supported defaults:

- Dark Green text on White, Green 10%, Green 20%, Green 30%, Green 40%, Green 60%, or Coral.
- White or Green 10% text on Green 80%, Green 90%, or Green 100%.

Do not use White text on Green 40% or Green 60%, or Green 10% text on Green 60%; those
combinations do not provide enough contrast for normal text. Prefer Dark Green text on those
lighter backgrounds. Use White text on the primary Green 100% background.

Secondary text is Dark Green at reduced opacity and should only appear on White or Green 10%
surfaces. Do not communicate a status by color alone: pair color with a name, icon, count, or other
textual explanation.

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

- Pages use Green 10%; cards, tables, dialogs, and form fields use White.
- Primary buttons use Green 100% with White text. Secondary buttons use White with Dark Green
  text. Destructive buttons use Coral with Dark Green text.
- Hover and selected surfaces use Green 20%. Keyboard focus uses a visible Green 100% border.
- Disabled controls retain their semantic color but use reduced opacity and a non-interactive
  cursor.
- Table headings use Green 10%, table separators use Green 20%, and selected rows use Green 20%.
- Informational overlays use a translucent Dark Green scrim. Tooltips use Green 100% with White
  text.
- Missing/removal and error states use Coral with Dark Green explanatory text. Synchronization
  review uses a pale Coral fill for possibly removed entities and the dedicated yellow warning
  palette for retained unresolved dependencies.
- Charts use Dark Green for axes and labels, Green 20% for grid lines, the status mapping above for
  categories, Green 80% for positive/ready trends, and Coral for negative/blocked trends.

## Implementation rules

WPF colors and styles are centralized in
[`src/EntityTracker.Wpf/Themes/EntityTrackerTheme.xaml`](../../src/EntityTracker.Wpf/Themes/EntityTrackerTheme.xaml).
Feature XAML should reference semantic brush keys such as `Brush.Text.Primary`,
`Brush.Surface.Card`, `Brush.Status.ReworkNeeded`, or the shared button styles. Do not add raw hex
values to feature XAML.

Reporting is intentionally independent of WPF. Its matching SkiaSharp values live in
[`src/EntityTracker.Reporting/ProgressChartPalette.cs`](../../src/EntityTracker.Reporting/ProgressChartPalette.cs).
This small duplication preserves project boundaries. Any shared brand or semantic status change
must update the WPF theme, reporting palette, this guide, and the reporting palette tests together.
Review-only warning colors do not belong in Reporting.

Native Windows chrome and operating-system dialogs may retain system colors. New application-owned
screens, overlays, charts, and exported visual reports must follow this guide.

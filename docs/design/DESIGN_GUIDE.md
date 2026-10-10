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
| Green 80% | `#41605E` | Strong borders, ready state and positive trends |
| Green 60% | `#718886` | Supporting graphics |
| Green 40% | `#A0AFAF` | Not-started state and neutral graphics |
| Green 30% | `#B8C3C3` | Report support color |
| Green 20% | `#D0D7D7` | Report chart grids |
| Green 10% | `#E7EBEB` | Text on dark green and report backgrounds |
| White | `#FFFFFF` | Text on dark green and report/chart canvas |
| Coral | `#FF6359` | Rework needed, error states, destructive actions |

### Status extension

The brand palette has too few distinct hues for every status, so a small set of muted colors
extends it. They are used only for the statuses below, never as decoration.

| Name | Hex | Status | Text over it |
| --- | --- | --- | --- |
| Slate blue | `#3D6A8A` | In progress | White (5.8:1) |
| Lavender | `#B58BD0` | Reworking | Dark Green (6.1:1) |
| Brick red | `#9E2B25` | Blocked | White (7.4:1) |
| Soft green | `#A8D5A2` | Development completed / Completed | Dark Green (10.3:1) |
| Amber | `#D9922E` | Waiting on dependencies | Dark Green (6.6:1) |

A new status color must meet 4.5:1 contrast with its text and stay clearly distinguishable from the
other statuses it appears beside (development statuses together, work statuses together), also
under simulated deuteranopia and protanopia. The set above keeps every pair at least ΔE 17 apart
in all three. Define it once in `EntityTrackerPalette.xaml` and mirror it in Reporting.

The application icon is existing artwork and is not recolored by this guide.

Synchronization review uses one additional amber warning palette for retained, non-fatal
unresolved dependencies. Its centralized header and border color is `#D9922E`; its surface is a
translucent tint over the current theme surface and its text follows the active system text color.
The same amber marks the Waiting on dependencies status. It must not replace Coral for errors,
removal, or destructive actions.

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
| Not started | Green 40% | Not begun; Dark Green text |
| In progress | Slate blue | Being worked on; White text |
| Rework needed | Coral | Needs attention; Dark Green text |
| Reworking | Lavender | Being reworked; Dark Green text |
| Blocked | Brick red | Stuck; White text |
| Development completed / Completed | Soft green | Done, not yet reconciled; Dark Green text |
| Reconciled | Green 100% | Fully implemented/reconciled; White text |
| Ready | Green 80% | Dependency-ready work; White text |
| Waiting on dependencies | Amber | Waiting for other entities; Dark Green text |
| Error/destructive | Coral | Always add a label, icon, or explanatory message |
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
- Informational overlays use a translucent Dark Green scrim. Focused catalog-management dialogs
  use the stronger scrim semantic so background content cannot compete with the active form.
- Missing/removal and error states use Coral with Dark Green explanatory text. Synchronization
  review uses a pale Coral fill for possibly removed entities and the dedicated yellow warning
  palette for retained unresolved dependencies.
- Charts use Dark Green for axes and labels, Green 20% for grid lines, the status mapping above for
  categories, Green 80% for positive/ready trends, Amber for waiting-on-dependencies trends, and
  Coral for negative trends.

## Scrolling

Scrollable content must move by pixels, including lists of suggestions. WPF list controls can
default to logical item scrolling, which jumps by whole rows. Choose the setting for the control:

| Content | WPF setting |
| --- | --- |
| Short `ListBox`, including a suggestion popup | Set `ScrollViewer.CanContentScroll="False"` on the list. |
| Page content or `ItemsControl` inside a `ScrollViewer` | Set `CanContentScroll="False"` on the viewer. |
| Large virtualized `DataGrid` or list | Keep `ScrollViewer.CanContentScroll="True"` and set `VirtualizingPanel.ScrollUnit="Pixel"` with virtualization and recycling enabled. The shared DataGrid style already sets the pixel scroll unit. |

The dependency dropdown can browse every eligible entity. Its list uses virtualization with
pixel scrolling and a maximum height of 240 pixels (about eight rows), rather than limiting
the number of available results.

Give an independently scrolling list a finite height or `MaxHeight` so its own viewer can scroll.
Avoid placing it inside an unbounded `StackPanel` or another `ScrollViewer` that takes over its
scrolling. Use `VerticalScrollBarVisibility="Auto"` unless the layout calls for a persistent bar.

`MainWindow` routes mouse-wheel input through `MouseWheelScrollRouter` so nested viewers use
the Windows wheel setting and keep one scroll owner during a gesture. Do not add a local wheel
handler to compensate for item scrolling; set the scroll unit correctly. WPF `Popup` content is
hosted in a separate window, so the main-window router does not receive its wheel events. Give
scrollable popup content an explicit pixel-scrolling setting and verify it with the mouse wheel.

When adding or changing a scrollable area, check that a wheel step can stop partway through a
row, that the intended viewer owns the scroll, and that scrolling remains usable at its top and
bottom boundaries. Check with enough content to overflow the viewport.

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

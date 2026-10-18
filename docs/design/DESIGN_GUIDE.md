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
- The app's progress charts follow the theme: axes, grid, legends and tooltips use `Chart` colours
  in Light and `DarkChart` colours in Dark, on the card surface. Saved and copied chart images and the
  exported report never derive colors from the live application theme; they are always light.

## Semantic colors

Status colors are shared by the overview, dashboards, dependency graph, progress charts, PNG exports
and the exported report. Every status has its own hue. All are mid-tones (relative luminance about
0.245): Dark Green text reads on each (about 4.7:1), and each stays visible as a graph node or chart
slice on light and dark pages (3:1 or more). One palette therefore serves both themes and the exports.

| Meaning | Color | Graph mark |
| --- | --- | --- |
| Not started / Ready | Status grey `#778C8A` | none |
| In progress | Status blue `#4C8CCB` | play triangle |
| Rework needed | Status orange `#E65C20` | exclamation bar |
| Reworking | Status violet `#9A77CC` | two triangles |
| Blocked | Status red `#E25A66` | bar |
| Development completed / Completed | Status teal `#209888` | check |
| Reconciled | Status green `#4D993E` | two checks |
| Waiting on dependencies | Status amber `#B77B14` | (work status only) |
| Unresolved reference warning | Warning palette | Dependencies on names no entity has yet, in the Overview, entity details and the dependency graph |

A status is never told by colour alone: badges carry their name, and the dependency graph draws the
mark above inside each node large enough to read, and in its legend. Error text uses the theme's
critical colour (`Brush.Critical`), not a status colour, since status colours are fills.

### Colour scheme

Every colour in the app, its charts and the exported Project report is defined in one file:
[`src/EntityTracker.Theme/ColorScheme.cs`](../../src/EntityTracker.Theme/ColorScheme.cs). Nothing
else defines a colour; a test fails if a hex value, `rgb()`/`rgba()` or a colour built from numbers
appears anywhere else in `src`.

- `ColorScheme.Base` names each colour once (the brand palette above plus a few supporting tints).
- The other sections assign a base colour to one part of the application:
  - `Status` and `WorkStatus`: a fill and a text colour per development and work status.
  - `ReportWorkStatus`: the exported report's work status badges (the same colours as the app).
  - `App`: everything else in the app (accent, surfaces, overlays, notifications, review, graph).
  - `Chart`: the progress charts saved as images, in the exported report, and in the app's light theme.
  - `DarkChart`: the text, grid and tooltip of the app's progress charts in the dark theme.
  - `Report`: the exported report's page (text, borders, highlights).
- To recolour one thing, change its assignment, for example
  `Status.InProgress = new(Base.Coral, Base.DarkGreen)`. To change a colour everywhere it is used,
  change its `Base` value. Rebuild; the app, the charts and the report all follow.
- Each status's text colour must keep 4.5:1 contrast with its fill, and each status fill 3:1 with the
  light and dark pages and cards; tests check both, and that no two statuses share a fill.

The app's text, page, card, border and focus colours are not in the scheme: they follow the Windows
light or dark theme through Fluent resources.

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
  categories, Green 80% for positive/ready trends, and Coral for negative/blocked trends.

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

The scheme reaches each part through one adapter:

- WPF: [`Themes/ColorSchemeResources.cs`](../../src/EntityTracker.Wpf/Themes/ColorSchemeResources.cs)
  turns every entry into a `Color` and a `SolidColorBrush` (keys such as `Brush.Brand.Green80`,
  `Brush.Status.InProgress` and `.Text`, `Brush.WorkStatus.Ready`, `Brush.Accent`). It is merged
  first in `EntityTrackerTheme.xaml`. `EntityTrackerPalette.xaml` keeps only the theme-following
  Fluent brushes.
- Charts: [`ProgressChartPalette.cs`](../../src/EntityTracker.Reporting/ProgressChartPalette.cs) reads
  the scheme as SkiaSharp colours.
- Exported report: `ProjectReportHtmlWriter` fills in report.css's colour variables (the base colours
  as `--green-100` etc. and `ColorScheme.Report` as `--ink` etc.) and writes the status and chart
  colours report.js needs next to the report's data.

`EntityTracker.Theme` has no dependencies, so Reporting can use it while staying independent of WPF.

Live charts sit on an explicit light report canvas inside theme-aware application cards so their
fixed Reporting labels remain readable in Dark mode. Native Windows chrome and operating-system
dialogs may retain system colors. New application-owned screens, overlays, charts, and exported
visual reports must follow this guide.

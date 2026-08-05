# Horizontal Mini Graph — Design

**Date:** 2026-08-05
**Status:** Approved (brainstorming) — pending implementation plan

## Summary

Add a **horizontal orientation** to the existing mini graph widget: the same four sections laid
out left-to-right in a short, wide strip sized to sit on the Windows taskbar.

This is an orientation of the widget described in
[2026-07-31-floating-mini-graph-design.md](2026-07-31-floating-mini-graph-design.md), not a second
widget and not a taskbar integration. One window, one tray entry, one set of section toggles.
`MiniGraphState` gains an `Orientation` property beside `Opacity`, and switching it relayouts the
same window in place.

**Database impact: none.** Everything here is `settings.json` preferences and runtime layout — no
entity, `DbSet`, column or index changes, so no EF Core migration is required.

## Why not a taskbar integration

The request began as "a version of the mini graph that lives in the taskbar, docked, movable within
the taskbar". Windows 11 has no supported mechanism for that. Deskbands — the COM shell extension
that put NetSpeedMonitor and similar tools inside the taskbar — were deprecated in Windows 10 1809,
and the Windows 11 taskbar was rewritten as a XAML shell that dropped toolbar hosting entirely.
There is no replacement API.

Three routes were considered:

| Route | Result | Cost | Ongoing risk |
|---|---|---|---|
| Reparent into `Shell_TrayWnd` via `SetParent` | Genuinely inside the taskbar | 4–7 days, plus replacing the WinUI `Window` with a Win32 child hosting a `DesktopWindowXamlSource` | High — cross-process input attachment to explorer.exe, destroyed on every Explorer restart, breaks when Windows rearranges the taskbar tree |
| Topmost overlay positioned over the taskbar | Looks in-taskbar, no Explorer coupling | 2–3 days | Medium — z-order contention with the taskbar, auto-hide and DPI tracking |
| `SHAppBarMessage` docked band | Supported API, reserves its own space | ~1 day | Low, but sits *beside* the taskbar rather than in it |

The overlay route was chosen, and then relaxed: because the strip is simply an always-on-top window,
it is not bound to the taskbar at all. **The design carries no taskbar logic whatsoever** — no
snapping, no auto-hide tracking, no z-order reassertion, no Explorer coupling. The user drags the
strip wherever they want it, including onto the taskbar, and it stays there because it is topmost.

The consequence is honest and must not be papered over: when the user clicks the taskbar, the
taskbar may come over the strip. That was accepted deliberately in exchange for removing the entire
class of fragility above.

## User experience

### Choosing the orientation

A new **Orientation** submenu in the widget's right-click menu, built the same way as the existing
**Opacity** submenu — `RadioMenuFlyoutItem` entries for *Vertical* and *Horizontal* sharing a
`GroupName`. The Settings page gains a matching selector beside the other mini graph preferences, so
the tray, the widget menu and Settings continue to drive one writer.

Switching orientation relayouts the open window in place. It does not close and reopen.

### Placement

Placement is stored **per orientation**. Flipping to horizontal moves the window to wherever the
strip was last left; flipping back returns the vertical widget to its own saved spot. Without this,
switching would drop a 700 × 40 strip at the floating widget's 320 × 220 coordinates and the user
would have to reposition it every time.

### Sizing

- **Width is derived**, never dragged. It is the sum of the visible cells' natural widths plus the
  inter-cell gaps and the strip's padding. Toggling a section on or off resizes the window.
- **Height is dragged** on the top or bottom edge, and persisted like the vertical widget's size.
  Clamped to 40–120 DIP; default 40, which clears a 100%-scaling Windows 11 taskbar (48 px) with
  room either side. (28 DIP was the original target but proved unreachable — Windows enforces a
  minimum tracking size on a window with a resize border, and the measured floor was 39 px.)

### Section order and content

Left to right, matching the vertical widget's top-to-bottom order: **Internet**, **Local**,
**Speed**, **Unknown devices**, then the close glyph.

The traffic cells are `MiniTrafficSection` unchanged in substance — the control is already
chart-behind-floating-text, which is exactly the horizontal treatment required. Each shows its bold
label on the left and `Peak 4.2 MB/s` on the right, with the area chart behind and the existing
scrim gradient keeping the text legible.

The speed-test cell uses a **short form**: `Speed ↓94 ↑12 Mb/s  18 ms`. The vertical widget's full
line also carries jitter and a timestamp, which would make the cell roughly 320 px and let it
dominate the strip. Jitter and time are dropped in horizontal only.

The unknown-devices cell keeps its existing text and its caution colouring when the count is above
zero.

### Two behaviours inherited for free

- `MiniTrafficSection` already hides the chart's gridline values and time markers below 74 px tall,
  so at strip heights the chart cleans itself up with no new code. Above 74 px they return.
- The last remaining section still cannot be switched off — `MiniGraphState.ApplySection` already
  enforces that, and it applies to both orientations.

## Architecture

### Layout mechanism

**One window, one `Grid`, reconfigured in code.** `MiniGraphWindow.ApplyLayout()` already juggles row
heights when sections are toggled; it gains an orientation branch that swaps `RowDefinitions` for
`ColumnDefinitions` and reassigns `Grid.Row` / `Grid.Column` on the four children.

Two alternatives were rejected:

- *Two root panels in the XAML.* A control can only have one parent, so this needs two sets of
  `MiniTrafficSection` — two `TrafficAreaChart` instances each holding a per-frame render hook, for
  no gain.
- *A second `Window` class.* Duplicates the ~400 lines of drag, hover, layered-alpha and placement
  machinery in `MiniGraphWindow`.

### `NetworkMonitor.Models/Widget/MiniGraphOrientation.cs` — new

Two-value enum, `Vertical` and `Horizontal`. Lives in Models so `NetworkMonitor.Core` can reference
it, per the Models ← Core ← Services ← App layering.

### `NetworkMonitor.Core/Widget/HorizontalStripMetrics.cs` — new

Pure derived-width calculation: given which sections are visible and the font scale, return the
strip's natural width. Sits in Core rather than in the window so it is directly testable, per the
project rule that new pure logic needing tests goes in Core.

Cell widths are **measured from the rendered text**, not assumed. The mock's figures (≈170 px per
traffic cell, ≈196 px for the short speed line, ≈146 px for the device count at scale 1.0) are
indicative starting points for the layout, not constants to hard-code.

### `NetworkMonitor.Models/Formatting/MiniGraphFormatter.cs`

Gains a short-form speed-test method beside the existing `SpeedTest`, returning rates and ping only.
The existing method is untouched — the vertical widget keeps the full line.

### `NetworkMonitor.Services/Data/Settings.cs`

Four new keys: `MiniGraphOrientation`, `MiniGraphStripX`, `MiniGraphStripY`, `MiniGraphStripHeight`.
The existing `MiniGraphX/Y/Width/Height` keys keep their current meaning and continue to serve the
vertical orientation. No migration; `settings.json` gains fields with defaults on first read.

### `NetworkMonitor.Services/Platform/MiniGraphState.cs`

Gains an `Orientation` property routed through the existing `Apply` helper, so a change saves
settings and raises `Changed` exactly like `Opacity` does, and a `SaveStripPlacement(x, y, height)`
alongside the existing `SavePlacement`.

### `NetworkMonitor/MiniGraphWindow.xaml.cs`

- `ApplyLayout` gains the orientation branch described above.
- `RestorePlacement` chooses which settings keys to read from, and reuses `GetScaleForPoint` and the
  work-area clamp **verbatim**. That path has already produced two high-DPI defects (`8023ffa`, the
  widget opening at half size; `abea7c8`, the drag diverging) and the strip gets the same treatment
  rather than a fresh implementation.
- `SaveCurrentPlacement` writes to the strip keys when horizontal, and applies the strip's height
  clamp instead of the vertical minimums.
- **Font scale gets its own horizontal formula.** Today `SectionsPanelSizeChanged` scales off
  `min(width / 320, height / 220)`. In horizontal the width grows with every section added, so that
  formula would inflate the text as sections are switched on. Horizontal scales off height alone:
  `clamp(height / 40, 1.0, 2.0)`.
- The close glyph moves from a floating top-right `Button` to its own narrow trailing column when
  horizontal. Left floating it would land on the unknown-devices text, because the 26 px right
  reserve in `MiniTrafficSection`'s header row does not apply to the plain `Border` cells.
- `RootRightTapped` adds the Orientation submenu.

### `NetworkMonitor/Views/Controls/MiniTrafficSection.xaml.cs`

Header margins only. The control's structure — chart, scrim, floating header — is already correct
for horizontal.

### Settings page

An orientation selector beside the existing mini graph preferences, writing through
`MiniGraphState.Orientation` so it stays in step with the widget menu and the tray.

## Error handling and edge cases

- **Width fights the presenter.** `OverlappedPresenter.IsResizable` is all-or-nothing, so width
  cannot be locked while height stays free. The strip remains resizable and `OnAppWindowChanged`
  forces the width back to the derived value while letting the height stand. Dragging a side edge
  therefore snaps back. This is a known rough edge, accepted rather than solved.
- **Orientation change while hidden.** The state change must be applied on next show rather than
  moving a hidden window, so the widget does not reappear somewhere unexpected.
- **A saved strip position on a display that no longer exists.** Originally handled by
  `DisplayArea.GetFromPoint` / `DisplayAreaFallback.None` and the work-area clamp. `None` also
  returns null for a position dragged a few pixels past a screen edge — inside no display at all —
  which sent that case down the never-placed path and dropped the strip in the work area's
  bottom-right corner instead of restoring it. Changed to `DisplayAreaFallback.Nearest`, which
  resolves the nearest display for both cases while the existing clamp still pulls the position
  back on-screen.
- **Height below 34 px.** The label and peak share a baseline row and the chart needs the remainder.
  The peak figure is dropped below 34 px rather than being allowed to collide with the label. The
  34 px threshold is retained in code as a guard, but it is unreachable at the current 40 DIP height
  floor — the strip can never be dragged short enough to trigger it.
- **Alt+F4 on the strip.** Already covered — `OnWindowClosed` tears down and clears
  `MiniGraphState.IsVisible` regardless of orientation.

## Testing

New unit tests in `NetworkMonitor.Tests`:

- `HorizontalStripMetrics` — derived width for each combination of visible sections; the close column
  always contributing; gaps and padding counted once; width scaling with the font scale.
- `MiniGraphFormatter` short speed-test form — a successful result, a never-run result, byte versus
  bit unit mode, and the sub-ten-Mbps decimal rule the existing `Scaled` helper enforces.

Manual verification:

- Switch orientation both ways with the widget open; confirm each form returns to its own saved
  position.
- Drag the strip's height across 74 (chart labels return). The 34 px peak-drop threshold cannot be
  exercised this way — the minimum draggable height is 40 DIP, above the threshold — so it is not
  part of this manual check.
- Toggle each section and confirm the strip resizes rather than stretching its cells.
- Repeat placement and drag checks on a 200% display, given the two prior high-DPI defects.

## Out of scope

- Any form of taskbar docking, snapping, auto-hide tracking or z-order contention.
- Reparenting into `Shell_TrayWnd`.
- `SHAppBarMessage` screen-space reservation.
- Per-section width customisation, or reordering the sections.
- A second widget instance — vertical and horizontal remain mutually exclusive.

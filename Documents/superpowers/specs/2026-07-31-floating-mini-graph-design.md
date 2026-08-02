# Floating Mini Graph — Design

**Date:** 2026-07-31
**Status:** Approved (brainstorming) — pending implementation plan

## Summary

Add a small always-on-top desktop widget that shows live network activity without the main
window being open. The user chooses what it contains: an **Internet** (WAN) chart, a
**Local** (LAN) chart, a **last speed test** strip, and an **unknown devices** strip — any
combination of the four.

The widget is fed entirely from events the app already raises. Its charts never query the
database, so it costs almost nothing to leave running, which is the point: the app is
designed to sit in the tray all day, and this is the glanceable face of it.

## User experience

### Opening and closing

Three entry points, all writing the same setting:

- **Tray menu** — a checkable *Mini graph* item above *Show Umnatha Network Monitor*.
- **Traffic page** — a toggle button in the page toolbar.
- **Settings → Traffic** — a *Show floating mini graph* switch.

The widget is independent of the main window. Hiding the app to the tray leaves it up;
restoring the main window doesn't dismiss it. It closes with the app on exit.

### The window

- **Always on top**, so it isn't buried by whatever you're working in.
- **Frameless** — a resize border with no title bar or caption buttons. Drag it by any
  empty part of its background. A ✕ glyph fades in on hover at the top right.
- **Hidden from the taskbar and Alt-Tab**, so it reads as a desktop widget rather than a
  second application window.
- **Double-click to drill in** — double-clicking the Internet or Local chart shows the main
  window on the matching Traffic tab; the speed strip opens Speed Test; the devices strip
  opens Devices → Unapproved.
- **Adjustable opacity**, 50–100% where 100% is fully opaque, so it can sit over a working
  window without blocking it. The whole window fades together — chart, text and background.
- **Hover to read** — resting the pointer over the widget raises it to fully opaque, and it
  settles back to the chosen opacity when the pointer leaves. The low setting is for
  ignoring it; hovering is the gesture that says *let me read that*.
- **Right-click** opens a flyout with the four checkable section items, an *Opacity*
  submenu, a separator, *Open Network Monitor*, and *Close*.

Closing — by the ✕ glyph or the flyout — clears `ShowMiniGraph`, so the tray item, toolbar
button and Settings switch all follow.

### Layout

Sections stack vertically in a fixed order: Internet chart, Local chart, speed strip,
devices strip. Charts carry no header row; the section name sits small and uppercase in the
chart's top-left corner and the live rate in its top-right, both over a soft scrim that
fades from the window background so the text stays readable when a spike reaches the top of
the trace. The two strips sit together in a slightly darker footer separated by a rule.

**The window keeps whatever size it was dragged to.** The strips are fixed-height; the
charts share all remaining space equally. Switching Local off therefore makes the Internet
chart twice as tall rather than shrinking the window.

If all four sections are off, the window shows *"Right-click to choose what to show"*
rather than collapsing to nothing.

### What the sections show

| Section | Content |
|---|---|
| Internet | Live WAN download/upload area chart, last 5 minutes, plus a smoothed rate (e.g. `↓118 ↑3`) |
| Local | Live LAN download/upload area chart, last 5 minutes, plus a smoothed rate |
| Speed test | Time of the last test with download, upload and ping — `Speed 06:00 ↓512 ↑48 Mb/s · 9 ms`. Reads *"No speed test yet"* on an empty database |
| Unknown devices | Amber `⚠ 2 unknown devices` when any are present, grey `✓ no unknown devices` at zero |

Rate text follows the existing **Speed units** setting, except that *Both* renders as Mb/s
only — there isn't room for two units in this window. When a section's **combined**
throughput is below 0.5 Mb/s the whole rate reads `—`, matching the threshold the existing
live rate badge already uses; the chart still draws.

## Architecture

Respects the project layering **Models ← Core ← Services ← App**. New pure, testable logic
goes in Core.

### `NetworkMonitor.Core/Traffic/LiveRateBuffer.cs`

A fixed-capacity ring of 300 one-second buckets, each holding
`(epochSecond, downloadBytes, uploadBytes)`.

- `Add(DateTime timestampUtc, long download, long upload)` accumulates into that second's
  bucket, zero-filling any seconds skipped since the previous add — an idle gap must read
  as a flat zero line, not as a hole in the trace.
- `Snapshot(DateTime nowUtc)` returns `IReadOnlyList<ChartPoint>` oldest-first, zero-filled
  forward to `nowUtc`.

Pure, no dependencies beyond `NetworkMonitor.Models.Charting`. This is the piece the test
project exercises.

### `NetworkMonitor.Services/Traffic/LiveTrafficFeed.cs`

A singleton `IHostedService`, registered alongside `TrafficTracker` and friends in
`App.xaml.cs`. It runs from startup whether or not the widget is open — which is what lets
the widget open with five minutes already drawn instead of an empty chart.

On start it subscribes to:

- **`TrafficTracker.Flushed`** — WAN entries (excluding `ProcessName == "System"`, matching
  `InternetViewModel.AccumulateRateWindows`) into the WAN buffer; `LocalDeltas` into the LAN
  buffer. Then raises `Updated`.
- **`SpeedTestWorker.SpeedTestCompleted`** — caches `LatestSpeedTest`, raises `Updated`.
- **`ScanWorker.ScanCompleted`** — recounts unapproved devices with the same predicate as
  `UnapprovedDevicesViewModel.cs:91` (`!IsApproved && (IsOnline || LastSeen >= cutoff)`),
  raises `Updated`.

It also holds two `RateWindow` instances (the existing Core type) for the smoothed rate
text, and performs exactly two database reads at startup to seed the last speed test and the
unapproved count. After that the charts never touch the database.

Memory cost is roughly 15 KB held permanently.

Every handler is wrapped and reports through `AppLog.Error`. A fault in the widget's feed
must never take down the flush loop the rest of the app depends on.

### `NetworkMonitor/MiniGraphWindow.xaml`

Sits at the project root beside `MainWindow` and `SplashWindow`, bound to a singleton
`MiniGraphViewModel` in `ViewModels/`.

Window chrome:

- `OverlappedPresenter.SetBorderAndTitleBar(true, false)` — resize border, no caption.
- `IsAlwaysOnTop = true`; minimise and maximise disabled.
- `AppWindow.IsShownInSwitchers = false` for Alt-Tab.
- `WS_EX_TOOLWINDOW` set via P/Invoke to drop the taskbar button. The interop block may
  live in the window's own file per the project convention.
- Dragging via `InputNonClientPointerSource.SetRegionRects(NonClientRegionKind.Caption, …)`
  over the background, recomputed on resize and punched out around the ✕ glyph.
- Theme follows the app, as `MainWindow` does.

The window is created lazily on first show and **hidden rather than closed** thereafter, so
toggling it is instant and its Win2D surfaces aren't rebuilt each time. While hidden, chart
updates are skipped — the cost drops to the ring-buffer writes alone. Lifetime is owned by
`App.xaml.cs` next to the existing `MainWindow` handling.

### `NetworkMonitor/Views/Controls/MiniTrafficSection.xaml`

The label, rate text, scrim and chart for one lens, used once for Internet and once for
Local. The two strips are plain `TextBlock`s inline in the window — they don't earn a
control.

### `TrafficAreaChart` change

One new `Compact` dependency property that collapses the axis-label panel, hover card,
crosshair and input layer — everything that doesn't fit at this size. No other behaviour
changes, so the Traffic page is unaffected.

### Opacity

`MiniGraphOpacity` is a percentage from 50 to 100, applied as `Opacity` on the window's root
element with the window's own background left transparent — so the whole widget, Win2D
canvas included, composites against the desktop at that alpha. This is the preferred route
because it needs no interop and follows the XAML render path the charts already use.

**Verify this early in implementation.** WinUI 3 desktop windows are created with
`WS_EX_NOREDIRECTIONBITMAP` and composited through DirectComposition, and Win2D's
`CanvasControl` renders onto its own composition surface; if root-element opacity doesn't
carry through to the canvas, fall back to `WS_EX_LAYERED` plus
`SetLayeredWindowAttributes(hwnd, 0, alpha, LWA_ALPHA)` on the top-level window, which fades
the composed result regardless of what drew it.

Opacity affects appearance only — the window stays fully interactive at every level. It is
set from a slider in Settings (50–100 in steps of 5) and from an *Opacity* submenu on the
right-click flyout offering 50, 60, 70, 80, 90 and 100% as radio items, matching the
"Settings plus right-click" pattern used for the section toggles.

### Hover to full opacity

`MiniGraphOpacity` is the *resting* opacity. While the pointer is over the window the widget
renders fully opaque, returning to the resting value when the pointer leaves. There is no
setting for this — it is always on, and does nothing at all when the slider is already at
100, so it needs no switch to turn off.

Driven by `PointerEntered` / `PointerExited` on the window's root element, animating the same
`Opacity` property the slider drives (or stepping the layered-window alpha, if the fallback
route above proves necessary).

Timing matters more than it sounds. Dragging the pointer across the screen will sometimes
clip a corner of the widget, and an instant rise makes it flash — which undercuts the whole
reason the opacity is low. So the rise waits for **150 ms of dwell** before starting, and the
fall waits **300 ms** after the pointer leaves; each transition then animates over ~120 ms.
The dwell is not perceived as lag when the user is deliberately moving to look at something,
and it removes the flash entirely. Both timers are cancelled if the pointer reverses before
they elapse.

The animation is presentation only. It never writes `MiniGraphOpacity`, so the resting value
survives a hover and the setting is not churned to disk.

### Entry point wiring

`TrayIconService` gains a checkable *Mini graph* item in its existing
`AppendMenu` / `TrackPopupMenu` block, which means a second `Action` alongside the current
`_onExit` and a flag for the checked state. The Traffic host page adds a toolbar toggle
button, and `SettingsPage` adds the switch plus the four section checkboxes. All three read
and write the same `Settings` fields, so whichever the user touches, the others follow.

### Settings

New fields on `Settings` (`Data/Settings.cs`), persisted to `settings.json` as all others
are:

| Field | Default |
|---|---|
| `ShowMiniGraph` | `false` |
| `MiniGraphShowInternet` | `true` |
| `MiniGraphShowLocal` | `true` |
| `MiniGraphShowSpeedTest` | `true` |
| `MiniGraphShowUnknownDevices` | `true` |
| `MiniGraphX` / `MiniGraphY` | unset — first open places it bottom-right of the primary work area |
| `MiniGraphWidth` / `MiniGraphHeight` | `320` × `230` |
| `MiniGraphOpacity` | `100` (percent, clamped to 50–100 on load) |

All four sections default on, so the first open shows what the widget can do and the user
prunes from there.

## Error handling and edge cases

- **Off-screen placement** — a saved position on a monitor that no longer exists (unplugged,
  resolution change) falls back to the bottom-right of the primary work area, validated with
  `DisplayArea.GetFromPoint`.
- **Minimum size** — clamped in the `AppWindow.Changed` handler, the same place
  `MainWindow` already tracks its own placement. The floor is 240 px wide and tall enough
  for the enabled sections at a 40 px minimum chart height.
- **Empty database** — the speed strip reads *"No speed test yet"*; the devices strip reads
  *"✓ no unknown devices"*.
- **Widget faults are contained** — logged through `AppLog.Error`, never propagated into the
  traffic flush loop or the scan loop.

## Testing

`LiveRateBufferTests` in `NetworkMonitor.Tests` covers:

- accumulation into the correct second's bucket
- zero-fill across an idle gap
- ring wrap at capacity (oldest bucket evicted, ordering preserved)
- snapshot ordering, length, and zero-fill forward to `nowUtc`

`LiveTrafficFeed` is thin event wiring and stays outside unit test coverage; it is verified
manually.

Manual verification:

- Run a large download and confirm the mini chart tracks the Internet tab's chart.
- Copy to the NAS and confirm the Local section moves while Internet stays flat.
- Toggle from all three entry points and confirm they stay in sync.
- Drag, resize, restart the app, confirm placement and size return.
- Switch Local off and confirm the Internet chart fills the freed space with the window
  keeping its size.
- Hide the app to the tray and confirm the widget keeps updating.
- Drop opacity to 50% over a text document and confirm the whole widget fades — including
  the Win2D chart, which is the part at risk — and that it still responds to drag,
  right-click and double-click.
- At 50%, hover the widget and confirm it rises to fully opaque and settles back on exit,
  chart included. Sweep the pointer quickly across a corner and confirm it does *not* flash.
  Set opacity to 100 and confirm hovering changes nothing.
- Check light and dark themes, and 4K at 200% scaling — DPI has bitten this project before.

## Out of scope

- Time range selection — the widget is fixed at 5 minutes, live only. No history mode, no
  hover card, no click-to-inspect.
- Per-app or per-device breakdown; the charts are totals only.
- **Click-through.** Deliberately excluded. A window that passes mouse input through
  receives none itself, which would kill dragging, the ✕ glyph, the right-click flyout and
  double-click-to-drill — four interactions this design depends on — and would need an
  escape hatch outside the window to get back. Hover-to-opaque covers the same "it's in my
  way" complaint at a fraction of the cost. Not revisited unless living with the widget
  shows a need.
- Edge snapping.
- Opacity that changes on its own beyond the hover behaviour above — no fade out when idle,
  no reacting to what's underneath.
- A theme independent of the app's.

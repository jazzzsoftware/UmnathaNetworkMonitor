# Smooth Chart Transitions — Design

**Date:** 2026-06-15
**Status:** Implemented (Win2D). Originally specced on LiveCharts2 — see "Pivot" below.

## Revision history / Pivot

This feature was first designed and built on **LiveCharts2** (see "Superseded: original
LiveCharts2 approach" at the end). That implementation worked and compiled, but on the
device it **pulsed**: every few seconds the whole curve re-animated and the vertical
scale "breathed."

Root cause (confirmed by debugging): LiveCharts2's model is *animate-on-interval-update*.
On every `TrafficTracker.Flushed` the data refreshes, and LiveCharts2 tweens the change —
so the periodic refresh itself becomes a visible, rhythmic motion. Aligning the bucket
grid (see below) removed the *data* churn but not the *engine's* per-update animation, so
the pulse remained.

More importantly, the user's real goal turned out to be **Model B — a continuous,
redraw-free, right-to-left scroll** (Task Manager / heart-monitor style), not Model A's
stepped "sweep on each update." LiveCharts2 cannot do Model B; it animates discrete
updates, full stop.

**Decision:** pivot to **Win2D** (the fallback flagged in the original spike note) and
build a true per-frame scroll. The aligned-bucket work and the extracted rate-formatter
were kept; the LiveCharts2 packages and the `ChartReconcile` helper were removed.

## Problem

The Traffic page chart "jerks" on every refresh. On each `TrafficTracker.Flushed` event
(~every 5 s in the live 5-minute view), `TrafficViewModel.LoadAsync` replaces the whole
`ObservableCollection<ChartPoint>` and the chart rebuilt the entire Bezier area geometry
and swapped `Path.Data` in a single frame — an instant snap with no motion, while the
sliding `cutoff = UtcNow - range` re-laid every point each time.

## Goal & motion model (as built)

**Model B — continuous treadmill scroll.** The curve drifts left smoothly and never
stops; new data slides in at the right; there is no visible "update event," just motion.
This required a per-frame render loop, which is why Win2D (not LiveCharts2) is the engine.

## Engine

- **`Microsoft.Graphics.Win2D` 1.4.0** (targets `net8.0-windows10.0.19041`,
  forward-compatible with our `net10.0-windows`; depends on
  `Microsoft.WindowsAppSDK.WinUI ≥ 1.8`, satisfied by our **2.2.0**).
- Render with **`CanvasControl`** (NOT `CanvasAnimatedControl` — that control has "partial
  or no support" on WinUI 3) driven by a **`CompositionTarget.Rendering`** loop that calls
  `Invalidate()` each vsync (~60 fps). `CanvasControl` draws on the **UI thread**, so all
  state is read/written on one thread — no cross-thread marshalling.

### De-risk gates (both passed)

1. Win2D 1.4.0 restores and builds against WindowsAppSDK 2.2 / .NET 10 / x64.
2. `CanvasControl` renders under WinAppSDK 2.2 (confirmed at runtime by the user).

## Architecture

`TrafficAreaChart`'s public surface is preserved — the `ChartPoints` dependency property
and `BucketSelected` event are unchanged — plus a new `IsLive` dependency property and a
`MarkLiveUpdate()` method (see below). `TrafficViewModel` keeps producing `ChartPoints`.

The root `Grid` is layered (bottom → top):

- **Base:** `CanvasControl` (`IsHitTestVisible=false`), drawn by Win2D.
- **Overlays (unchanged XAML):** dashed crosshair `Line`, custom hover `Border` panel
  (time + Received/Sent in bits/s with colored swatches), dual peak-label `StackPanel`.
- **Top:** a transparent `InputLayer` `Grid` that owns all pointer events.

## Rendering & continuous scroll

Each frame `ChartCanvasDraw`:

- Maps each bucket to its **real timestamp** on the X axis. In **live** mode the right
  edge is "now" (advanced every frame from `DateTime.UtcNow`) and the span is
  `count * bucketSeconds`; in **static** mode (paused / history / selected bucket) the
  right edge is the newest bucket and the span is `(count - 1) * bucketSeconds` (so a
  static chart spans full width and does not scroll).
- **Pins the leading edge to "now"** by appending a synthetic point at the right edge
  carrying the current value, so the line always reaches the right and just stretches as
  time passes — no one-bucket horizontal jump when a flush lands.
- Draws two gradient-filled Bezier areas (received blue `#1976D2`, sent purple `#AB47BC`)
  using `CanvasPathBuilder` cubic beziers (1/3-segment control points) + a separate open
  stroke path, reproducing the original look.

### Value easing (smooth data arrival, not chunks)

New data is quantized to one bucket per flush, so without easing the leading edge pops in
a chunk at a time. Fix: each bucket keeps a **displayed** value that eases toward its
**target** every frame, with an exponential time constant of **2.5 s**
(`EaseTimeConstantSeconds`, frame-rate-independent via `1 - exp(-dt/τ)`).

- Easing is matched **by timestamp** (`MigrateDisplayed`): when the window scroll-shifts,
  each bucket's displayed value follows its timestamp, so stable history never animates —
  only genuinely-new data does. A brand-new bucket starts at its neighbour's displayed
  level so the leading edge "draws in" rather than popping.
- The autoscale max (`_displayMax`) also eases toward the data max, so the vertical scale
  glides instead of snapping.

### Animate only live ticks (snap on context switch)

Easing must apply only to live streaming, not to context changes. `TrafficPage` calls
`AreaChart.MarkLiveUpdate()` only on the `OnTrafficFlushed` path; every other load
(app switch, range change, bucket select, navigation) leaves the flag clear, so
`ApplyPoints` **snaps** both values and scale to the new data. This fixed an off-scale
artifact where switching apps briefly drew the old (large) totals against the new
(small) scale.

`AreaChart.IsLive` is set from `TrafficPage` (`!paused && SelectedBucketStart is null`);
the render loop only invalidates while live and not frozen.

## Hover, crosshair & click-to-select

The `InputLayer` handles pointer events:

- **Freeze-on-hover:** entering the chart freezes "now" so the scrolling target holds
  still and is readable; exiting resumes (catching up to live).
- **Move:** position the crosshair `Line`; map pointer X → nearest bucket by timestamp
  (`NearestIndex`, using the same live/static window math as Draw); fill the hover panel.
- **Press (left):** raise `BucketSelected` with the nearest `ChartPoint`; `TrafficPage`
  drills into that slice and filters the app grid (unchanged downstream).

## Data pipeline: aligned bucket window

`TrafficViewModel.BuildDataAsync` now quantizes the window to a fixed bucket grid via
`TrafficWindow.AlignedCutoffEpoch(nowEpoch, bucketSeconds, totalBuckets)` instead of a
free-floating `UtcNow - range` cutoff. This keeps historical bucket timestamps stable
between flushes, which is what makes the continuous scroll seamless (the timestamp-based
mapping and easing rely on stable history). This was originally out of scope but proved
necessary.

## Testing

**Unit tests (`NetworkMonitor.Tests`, 43 total):**

- `TrafficRateFormatter` (extracted from the chart): b/s → Kb/s → Mb/s → Gb/s and bytes
  thresholds; `BucketSeconds` gap/default handling.
- `TrafficWindow.AlignedCutoffEpoch`: grid alignment, stability between flushes within a
  bucket, one-bucket advance at the boundary, newest-bucket-contains-now, minute-scale
  alignment.

**Manual verification (done with the user):** continuous smooth scroll; smooth (2.5 s)
data arrival rather than chunks; hover freeze + crosshair + panel; click-to-drill; app /
range / bucket switches snap cleanly with no ghosting; paused/history static and
full-width.

## Cost / trade-offs

Win2D draws are GPU-cheap (~60–120 points, two area paths per frame). The real cost is
the **continuous ~60 fps redraw while live**, which keeps the app from fully idling
(minor power/CPU on battery). It idles when navigated away (control unloaded), and when
paused/history/frozen (loop does not invalidate). If needed, the single lever is to
throttle to ~30 fps. One added dependency (Win2D + native Direct2D interop).

---

## Superseded: original LiveCharts2 approach (Model A)

Kept for the record. The first design swapped the rendering engine to **LiveCharts2**
(`LiveChartsCore.SkiaSharpView.WinUI` 2.0.4) and relied on its animation engine for
**Model A — animated transitions** (sweep to the new shape on each update, stationary
between updates). Two `LineSeries<ObservableValue>` with `LinearGradientPaint` fills and
`LineSmoothness` reproduced the look; `OnChartPointsChanged` reconciled values in place
(via a `ChartReconcile.Decide(oldCount, newCount)` helper) so the library could animate;
hover/click used `CartesianChart.ScalePixelsToData`.

Why it was abandoned: see "Pivot" at the top — LiveCharts2 animates every interval update,
which reads as pulsing, and it cannot produce Model B's continuous scroll. The LiveCharts2
and SkiaSharp packages and the `ChartReconcile` helper/tests were removed in the pivot;
the bucket-grid alignment and the `TrafficRateFormatter` extraction were kept.

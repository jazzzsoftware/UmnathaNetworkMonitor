# Traffic Live/History Indicator — Design

Date: 2026-06-14

## Goal

Give the Traffic page app list a clear at-a-glance indicator of whether it is
showing **Live** data (running totals that auto-refresh) or **History** (a
frozen snapshot at a chart point the user clicked), and make returning to Live
obvious. The indicator doubles as the control for switching back to Live.

## Current behaviour

- The app list (`AppGrid` / `TrafficViewModel.Apps`) has two implicit modes
  driven by `TrafficViewModel.SelectedBucketStart`:
  - `null` → **Live**: totals over the selected range; auto-refreshes on
    `TrafficTracker.Flushed` when the range is <= 6h.
  - non-null → **History**: a frozen snapshot of the clicked time bucket.
- `ChartLabel` text already differs per mode ("All Apps — last 5 minutes" vs
  "Apps at 14 Jun 14:03:21"). A `ClearBucketButton` HyperlinkButton
  ("Show full range") returns to Live and is visible only in History.

## Design

A small pill badge in the chart-card header, immediately left of `ChartLabel`.

States (driven by `SelectedBucketStart`):

| State | Condition | Appearance | Interactive |
|---|---|---|---|
| **Live** | no bucket selected | green dot + "Live", green-tinted rounded pill | No |
| **History** | a chart point selected | "History ✕", amber-tinted rounded pill | Yes — click returns to Live |

- Wording: title case "Live" / "History".
- The badge **replaces** the standalone "Show full range" link
  (`ClearBucketButton` removed); its return-to-live function moves to the
  badge's click in History mode.
- Entering History is unchanged: click a point on the chart.
- The existing chart label remains to the right of the badge.

Visuals: explicit hex consistent with the existing legend swatches
(`#1976D2` / `#AB47BC`); green ~ `#2E7D32`, amber ~ `#F57C00` (final
theme-friendly shades chosen during implementation). Pill = a `Border` with
small `CornerRadius` and a tinted background, containing a dot + text at
~11–12px.

## Implementation approach

- `TrafficPage.xaml`: add the badge (a `Border` wrapping a horizontal
  `StackPanel`: dot `Ellipse` + `TextBlock` + a close glyph shown only in
  History) to the left of `ChartLabel`; remove `ClearBucketButton`.
- `TrafficPage.xaml.cs`: in the existing `UpdateChartLabel()`, set the badge's
  text, colour, visibility and interactivity from `ViewModel.SelectedBucketStart`
  (same code-behind pattern already used for the label and clear link). Wire the
  badge's click in History mode to the existing clear-bucket logic
  (reuse `ClearBucketClick`).
- No new bindings or converters, no ViewModel changes, no settings.

## Out of scope

- No change to auto-refresh cadence. Ranges > 6h with no bucket selected remain
  "Live" (they simply do not tick automatically).
- No new persisted settings.

## Files affected

- `NetworkMonitor/Views/TrafficPage.xaml`
- `NetworkMonitor/Views/TrafficPage.xaml.cs`

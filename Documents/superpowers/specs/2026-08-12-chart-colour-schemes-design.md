# Chart Colour Schemes — Design

**Date:** 2026-08-12
**Status:** Approved, ready for planning
**DB impact:** None. This is a `settings.json` preference only — no new table, column or index, and no EF migration.

## Context

The chart palette is four hard-coded hex values with no single source of truth. They are copied across seven files and three unrelated rendering stacks:

| Colour | Role | Declared in |
|---|---|---|
| `#1976D2` | Download | `TrafficAreaChart.xaml.cs:22-24`, `TrafficAreaChart.xaml:96`, `InternetPage.xaml:122,393`, `LocalPage.xaml:140,441,564`, `SpeedTestPage.xaml:102,109,209`, `SpeedTestViewModel.cs:118`, `DigestChartRenderer.cs:29` |
| `#AB47BC` | Upload | the same seven files |
| `#F57C00` | Latency, and the chart selection line | `SpeedTestViewModel.cs:124`, `SpeedTestPage.xaml:292`, `TrafficAreaChart.xaml.cs:28`, `DigestChartRenderer.cs:31` |
| `#2E7D32` | Jitter | `DigestChartRenderer.cs:32` |

The three stacks are Win2D `Color` constants (the live traffic chart), XAML literals (legend swatches, grid cell foregrounds, page chips), and `ChartSeries.ColorHex` strings (the speed trend chart).

Two problems follow. Changing a colour means finding seven places, and the user cannot change it at all.

A third emerged during design. Running the palette through `validate_palette.js` shows the download↔upload pair separates by only **ΔE 6.5** under deuteranopia — inside the 6–8 "floor" band, where a palette is acceptable only because secondary encoding carries the identity. The charts do have that encoding (a legend, direct numeric labels, and labelled grid columns), so this is not a defect to fix. It is, however, the reason the preset set includes a high-separation alternative rather than five variations on the same weakness.

## Decisions taken

| Question | Decision |
|---|---|
| What is a scheme? | Curated presets, plus a Custom option that unlocks per-role pickers |
| What drives it? | Aesthetics. CVD separation is a secondary benefit, not the goal |
| How far does it reach? | All in-app surfaces: both traffic charts, the mini graph, the speed test charts, grid text and page chips |
| Light and dark? | Both, via derivation from one authored base colour per role |
| When does it apply? | Immediately — no restart |
| Does the digest follow it? | No. `DigestChartRenderer` keeps its own fixed dark/light constants |

The digest is excluded deliberately: an emailed report is a separate artefact, and a user with an adventurous Custom scheme should not make their own reports unreadable.

## Architecture

Pure colour logic in Core, resolved values in a Services singleton, live brushes in the App. Layering is `Models ← Core ← Services ← App` as usual.

### Core — `NetworkMonitor.Core/Charting/`

One type per file:

| Type | Purpose |
|---|---|
| `ChartRole` | enum: `Download`, `Upload`, `Latency`, `Jitter`, `Selection` |
| `ChartSurface` | enum: `Dark`, `Light` |
| `ChartPalette` | record holding one base hex per role |
| `ChartSchemePreset` | record: `Id`, `DisplayName`, `ChartPalette` |
| `ChartSchemeCatalog` | the fixed preset list, lookup by id, fallback to Classic on an unknown id |
| `Oklch` | An L/C/H triple |
| `OklchColour` | sRGB ↔ OKLab ↔ OKLCH conversion, plus `Contrast(a, b)` |
| `PaletteVariant` | the derivation: base hex + surface → display hex |

### The derivation rule

One authored base colour per role produces both surface variants:

1. Convert the base hex to OKLCH.
2. Clamp L into the surface's band — `0.48`–`0.67` on dark, `0.43`–`0.77` on light.
3. While contrast against the surface is below 3:1, step L away from the surface by `0.02` — lighter on dark, darker on light — stopping at the band edge.
4. Hold hue fixed throughout. Reduce chroma only where the result falls outside the sRGB gamut.
5. Convert back to hex.

Derivation is necessary, not a shortcut. No single hex works well on both surfaces: `#eda100` amber sits outside the dark band at L 0.764, and on the light card it falls to 2.09:1 contrast. Step 3 is what fixes both.

Surface constants: dark `#2D2D2D`, light `#FBFBFB`.

### Services — `NetworkMonitor.Services/Charting/ChartPaletteService.cs`

A DI singleton, the one place that answers "what colour is Download right now".

- Depends on `Settings`. Resolves scheme id → preset (or the Custom hexes) → `PaletteVariant.Derive` for the current surface.
- Caches the five derived `Windows.UI.Color` values. The Win2D draw path runs every frame while smooth scrolling is enabled and must never do colour maths.
- `Resolve(ChartRole)` returns a `Color`; `ResolveHex(ChartRole)` returns a string for `ChartSeries`.
- `SetSurface(ChartSurface)` recomputes when Windows switches light/dark at runtime.
- `Apply(...)` recomputes when the user picks a scheme or edits a custom colour.
- Both raise a single `PaletteChanged` event.

### App — live brushes

`ChartBrushes` registers five `SolidColorBrush` instances into `Application.Resources` at startup — `ChartDownloadBrush`, `ChartUploadBrush`, `ChartLatencyBrush`, `ChartJitterBrush`, `ChartSelectionBrush` — and mutates their `.Color` on `PaletteChanged`.

WinUI brushes are live `DependencyObject`s shared by reference, so every `{StaticResource ChartDownloadBrush}` repaints with no per-page code. This is what makes immediate application cheap across the twenty XAML literals — `InternetPage` 4, `LocalPage` 6, `SpeedTestPage` 8, `TrafficAreaChart` 2 — which all become resource references.

### Consumers

1. **`TrafficAreaChart`** — the eight `static readonly Color` fields become instance fields fed from the service. On `PaletteChanged`: dispose and rebuild `_downloadFill` / `_uploadFill`, refresh the stroke and selection colours, then `Invalidate()`. Subscribe on `Loaded`, unsubscribe on `Unloaded` — four instances live across two windows, and a leaked handler would keep a disposed canvas rooted.
2. **`SpeedTestViewModel`** — the three hexes at lines 118–124 come from `ResolveHex`. On `PaletteChanged` it rebuilds the `ChartSeries` collection, which is all `SpeedTrendChart` needs; that control already rebuilds its shapes from `ColorHex` on every render.
3. **`MiniGraphWindow`** — no colour code of its own. It hosts two `MiniTrafficSection` charts, so it inherits the `TrafficAreaChart` fix.

All four live traffic surfaces — Internet page, Local page, and both mini-graph sections — are the same `TrafficAreaChart` control, so there is one Win2D call-site, not four.

The theme hook goes on the `MainWindow` root's `ActualThemeChanged`, calling `SetSurface`. The mini graph needs no hook of its own; it listens to `PaletteChanged` like everything else. The mini graph floats over an arbitrary desktop at user-set opacity and has no real surface, so it follows the app theme rather than sampling what is behind it.

## The presets

Five, each authoring one base colour per role. ΔE figures are the download↔upload separation under the worst of protanopia and deuteranopia, OKLab ×100.

| Preset | Download | Upload | Latency | Jitter | Selection | ΔE |
|---|---|---|---|---|---|---|
| **Classic** (default) | `#1976D2` | `#AB47BC` | `#F57C00` | `#2E7D32` | `#F57C00` | 6.5 |
| **Horizon** | `#2a78d6` | `#eb6834` | `#eda100` | `#1baf7a` | `#e87ba4` | 24.7 |
| **Aurora** | `#1baf7a` | `#7c5cdb` | `#eda100` | `#2a78d6` | `#eb6834` | 24.1 |
| **Ember** | `#e34948` | `#eda100` | `#7c5cdb` | `#1baf7a` | `#2a78d6` | 15.3 |
| **Ocean** | `#2fc8ce` | `#3358c0` | `#eda100` | `#1baf7a` | `#eb6834` | 30.0 |

Classic remains the default and keeps its hues, but the derivation lifts each of its five roles slightly on the dark card: the legacy hexes measured 2.99:1 against `#2D2D2D`, just under the 3:1 floor, so `#1976D2`→`#227CD9`, `#AB47BC`→`#B24DC3`, `#F57C00`→`#E07311` and `#2E7D32`→`#3B893E`. On the light card, Latency and Selection also move, to `#E37200`. Every existing user therefore sees a very slightly lighter Classic palette on upgrade — the trade-off is that every chart now clears the contrast floor, including for users who never open Settings.

Selection is authored per preset so the hover line never collides with that preset's series hues. In Ember, amber is taken by Upload, so selection moves to blue.

Two pairs share a chart and both must stay distinguishable: Download↔Upload on the traffic charts, and Latency↔Jitter, which `SpeedTestViewModel` puts in the same `LatencySeries` and so on the same `SpeedTrendChart`. Selection never shares an axis with a series, so it needs only the per-colour band and contrast gates.

**Ocean was originally monochrome — one hue in two lightness steps — and that could not work.** On the dark card the band ceiling pulls Download down to L 0.670 while the 3:1 contrast floor pushes Upload up from L 0.484 to 0.564, leaving a usable window of roughly L 0.56–0.67. A base separation of 0.19 collapsed to 0.106, and the two blues derived to `#6099D8` and `#3677C2` — only ΔE 10.9 apart, below the 15 floor at which colours are hard to tell apart even with full colour vision. Contrast passed on both surfaces throughout; the two gates are independent. Ocean is now a cyan-to-blue pair, so it separates on hue and chroma as well as lightness, which is the only way to clear the floor inside that window.

## Settings

### Persisted values

Added to `Settings` (`settings.json`, no schema change):

- `ChartSchemeId` — string, default `"classic"`.
- `ChartCustomDownload`, `ChartCustomUpload`, `ChartCustomLatency`, `ChartCustomJitter`, `ChartCustomSelection` — base hex strings, seeded from Classic the first time Custom is chosen.

All five custom values persist unconditionally, so switching Preset → Custom → Preset → Custom does not lose the user's edits.

### The Theme tab

`SettingsPage.xaml` uses a `SelectorBar` at line 56 with `Tag`/`Text` pairs and one `ScrollViewer` panel per tab, switched in `TabBarSelectionChanged`. The Other tab has grown to seven sections and is the wrong home for this.

A fourth `SelectorBarItem` with `Tag="Theme"` is inserted **between Devices and Other**, with a matching `ThemePanel` `ScrollViewer` and a case in the selection handler. Tab order becomes Traffic, Devices, Theme, Other.

The Theme tab holds one "Chart colours" card:

- A `ComboBox` listing the five presets plus Custom.
- A live swatch row showing the five **derived** colours for the current theme, fed by `ChartPaletteService` so the preview is never a second copy of the palette.
- When Custom is selected, each swatch becomes a button opening a WinUI `ColorPicker` flyout. Picking writes the base hex to `Settings` and the palette updates instantly everywhere.
- A "Reset to Classic" link.

Naming the tab Theme rather than "Chart colours" leaves the obvious home for a future light/dark preference — the app sets no `RequestedTheme` today and follows Windows — but that is not in scope here.

## Testing

`NetworkMonitor.Tests` references Models and Core, so everything worth testing is reachable.

- `OklchColour` round-trips: hex → OKLCH → hex within a tolerance, across a spread of hues and both extremes.
- `PaletteVariant.Derive` holds hue within a small epsilon while moving lightness.
- **Every preset × every role × both surfaces clears 3:1 contrast against its surface constant.** This is the test that stops a preset shipping invisible, and it is the reason the derivation lives in Core rather than in the view.
- **Every preset × both shared-chart pairs × both surfaces stays at least 15 apart** in OKLab distance ×100, measured on the *derived* values. Contrast does not imply separation — Ocean passed every contrast case while its two blues collapsed to 10.9 on dark — so this is a second, independent gate. Without it a preset can ship readable against the background and still be unreadable against itself.
- Derived L lands inside the target band, or at its edge where the contrast loop stopped early.
- Gamut: no derived value has a channel outside `[0, 1]`.
- `ChartSchemeCatalog` falls back to Classic for an unknown or empty id, covering a hand-edited `settings.json`.

Behaviour that needs manual verification, since it spans WinUI and Win2D: switching scheme repaints the Internet page, Local page, speed test charts and both mini-graph orientations without a restart; switching Windows between light and dark re-derives; and a Custom colour picked in Settings reaches the mini graph while it is open.

## Out of scope

- Any change to `DigestChartRenderer` or the digest PDF.
- An in-app light/dark theme preference.
- Per-window or per-chart palette overrides.
- Importing or sharing scheme files.

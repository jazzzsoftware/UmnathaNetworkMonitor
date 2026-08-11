# Chunk 2 — DPI, geometry & multi-monitor

Range `c07260c..b215581`. Ledger: `progress.md`.

**11 findings — 3 BUG · 3 RISK · 5 CLEANUP. All `open`.**

## What was verified as correct

- **The DIP/physical contract is stated once and honoured on every path found.** `MiniGraphWindow.xaml.cs:33` and `:281-283` declare it; `SaveCurrentPlacement:286-289` divides by scale, `RestorePlacement:456-462,467` multiplies, `ClampWidgetSize:811-812` and `ClampStripSize:797-799` scale the DIP floors, and `SectionsPanelSizeChanged:348-355` feeds `HorizontalStripMetrics` from `SizeChangedEventArgs`, which is already DIPs. **No path was found where a raw DIP reaches `AppWindow` or a physical value is written to settings.** The old `RasterizationScale` path is gone from the widget entirely.
- **`app.manifest:18-19` declares `PerMonitorV2`**, so `GetDpiForWindow(_hwnd)` genuinely tracks the monitor rather than returning system DPI. The whole design depends on that and it is set correctly.
- **The drag rewrite is correct and loop-free.** `:846-847` and `:879` — both terms are absolute physical screen coordinates from `GetCursorPos`, so the target never reads back `AppWindow.Position`. `DragThreshold * GetCurrentScale()` at `:857` is the right conversion: a DIP-intent dead zone measured against physical cursor travel.
- **`ClampStripSize` provably converges in one pass rather than oscillating.** With `h` the physical height and `s` the scale it computes `Round(Clamp(h/s)·s)`. If `h/s` is interior the result is `Round(h) = h` — a fixed point. If it clamps, the result is `h* = Round(b·s)` for bound `b`, and `h*/s` is within `0.5/s` of `b`, so the second pass either lands interior at `h*/s` or re-clamps to the same `b`. Checked at s = 1.1, 1.25, 1.3, 1.4, 1.5, 1.75, 2.0 against both bounds — idempotent in every case. Width is a pure function of the converged height. The re-entrancy through `OnAppWindowChanged:758-763` bottoms out at depth 2.
- **`DisplayAreaFallback.Nearest` (`:435`) and `ExpandByFrameInsets` (`:505-531`) are evidence-driven fixes** with their measurements recorded in `7be89bc` and `05f7d92`. Asking DWM rather than `SM_CXSIZEFRAME` because the two disagree by one pixel is exactly right for a defect whose whole magnitude is 7px.
- **`HorizontalStripMetrics.Width` scaling cells but not `Gap`/`Padding` is correct, not an oversight**, and matches the XAML: `ApplyHorizontalLayout:615` sets `Padding = Thickness(4)` and `PlaceHorizontalCell:670` sets `Margin(0,0,4,0)`, neither multiplied by the font scale. Four visible cells produce four right-margins; the metric charges `(cellCount-1)·Gap` with the close cell counted, i.e. 4 gaps. Exact match, and `HorizontalStripMetricsTests.cs:79-84` pins the intent deliberately.
- **Star-weighted columns cannot clip from the weighting itself** — the weights *are* the nominal widths, so proportions are always right and the total always fills.
- **`OuterBounds` vs `WorkArea` at `:477` is the right choice**, and the reasoning in `819f390` is sound. A taskbar on the left or top is unaffected (`OuterBounds` is the whole display either way) and auto-hide is a non-issue (the work area then covers nearly the full screen). The never-placed default at `:469-470` is computed from `workArea`, so a first-run strip appears 16px *above* the taskbar rather than on it — deliberate per `819f390`, matches the README copy, leave it.

---

## C2-1 `[BUG]` — restore and save pick the monitor by two different rules, so the floating widget shrinks a step on every launch

`NetworkMonitor/MiniGraphWindow.xaml.cs:446` vs `:286`

`RestorePlacement:446` sizes from `GetScaleForPoint(positionX, positionY)` — the monitor under the window's **top-left corner**. `SaveCurrentPlacement:286` divides by `GetCurrentScale()` → `GetDpiForWindow` (`:305`), which under Per-Monitor-V2 is the monitor holding the **majority** of the window. Those two disagree whenever the widget spans a boundary between differently scaled displays.

Concretely — vertical widget 640×460 physical, mostly on a 200% monitor, top-left corner spilling onto the 100% monitor to its left:

1. Save writes 320×230 DIP (÷2, the majority monitor's scale).
2. Restore multiplies by the **corner** monitor's scale of 1 → 320×230 physical, which on the 200% monitor is 160×115 DIP — the exact half-size symptom `8023ffa` was written to fix, back again.
3. `ClampWidgetSize:815` forces the 240×120 DIP floor.
4. 400ms later the debounce persists **240×120**.

The stored size has now shrunk. Repeat per launch until it sticks at the minimum. The horizontal strip self-heals because `ClampStripSize` re-derives from the metrics, so this bites the floating panel only.

**Fix.** Use one rule. After `AppWindow.MoveAndResize:498`, re-read `GetCurrentScale()` and, if it differs from the `GetScaleForPoint` value used to size, re-apply the size at the live scale. Cheap, and it subsumes C2-2 and the first half of C2-5.

**Status:** `open`

---

## C2-2 `[RISK]` — nothing reconciles the size after a cross-monitor `MoveAndResize`, and there is no `WM_DPICHANGED` participation

`NetworkMonitor/MiniGraphWindow.xaml.cs:498`

`RestorePlacement` moves the window to a monitor that may differ in DPI from the one it was created on — the window is always constructed at the default position before being shown (`App.xaml.cs:322`, then `ShowWidget`). WinUI preserves a window's *logical* size across a DPI change, so a `MoveAndResize` that lands a pre-scaled physical size on a higher-DPI monitor is liable to be rescaled **again** by the system's suggested rect: pre-scaled ×2, then ×2 again. There is no handler and no post-move normalisation, and for the vertical widget `ClampWidgetSize` enforces only a floor, never a ceiling — so an oversized result is then persisted as an oversized DIP size by the debounce.

This could not be proven by reading alone; it depends on WinUI's own `WM_DPICHANGED` handling. **It was never exercised:** `8023ffa`'s repro was a **primary** 4K at 200%, where no DPI *transition* occurs.

**Fix.** The C2-1 fix (re-assert the size from the live window DPI once the move has settled) makes the outcome deterministic either way. Needs a laptop-plus-differently-scaled-external test to confirm.

**Status:** `open` — needs hardware verification

---

## C2-3 `[BUG]` — the strip's derived width and its rendered font scale come from two different heights, so the strip is systematically ~17% too wide

`NetworkMonitor/MiniGraphWindow.xaml.cs:769` vs `:348`

`DerivedStripWidth:769` feeds `AppWindow.Size.Height / scale` — the **outer window** height — into `FontScale`, then into `Width`. `SectionsPanelSizeChanged:348` feeds `args.NewSize.Height` — the **client/panel** height — into `FontScale` to set what the text actually renders at. The two differ by the invisible resize frame, measured by the author in `05f7d92` as 7px bottom / 0 top at 96 DPI, so ~7–8 DIP.

Using the author's own recorded strip (window height 53, visible 46):

| | height fed in | `FontScale` | width implied |
|---|---|---|---|
| `DerivedStripWidth` | 53 | 1.325 | `704×1.325 + 24` = **957 DIP** |
| actual layout | ≈45 | 1.125 | `704×1.125 + 24` = **816 DIP** |

≈**141 DIP of dead space**, and it is a constant proportion (`8/40 = 0.2` of font scale) for every height between roughly 48 and 112 DIP. It cannot clip — the layout scale is always the smaller of the two — but the entire premise of a *derived* width is that the strip is exactly as wide as its sections need, and on a taskbar 141 wasted DIP is the difference between fitting and not.

**Fix.** Derive the width from the same height the layout uses: either subtract the frame insets in `DerivedStripWidth`, or make `SectionsPanelSizeChanged` the single writer of the font scale and drive the width from it. This is also C1-6.

**Status:** `open`

---

## C2-4 `[BUG]` — clamping the strip's height anchors the top-left, so a top-edge over-drag lifts the strip off the taskbar and a left-edge drag translates it

`NetworkMonitor/MiniGraphWindow.xaml.cs:803`

`ClampStripSize` calls `AppWindow.Resize`, which keeps the origin fixed.

- **Top edge.** Drag above the 120 DIP ceiling and Windows sets `Y = newTop, height = H > 120`; the clamp then forces 120 anchored at `newTop`, so the **bottom edge rises by `H − 120`**. The strip leaves the taskbar it was docked to — on its only resize gesture.
- **Left edge.** Each mouse step during the modal resize loop sets `X−δ, width+δ`; the clamp restores the width anchored at `X−δ`, so the strip **walks left** for as long as you drag.

The `7cc385d` commit describes a side-drag as being "undone". Undone is not what happens; it translates.

**Fix.** Use `MoveAndResize` in `ClampStripSize`, holding the bottom edge (`Y + height`) and the pre-change left edge, rather than `Resize`.

**Status:** `open`

---

## C2-5 `[RISK]` — `ExpandByFrameInsets` applies one monitor's insets to a rect on another

`NetworkMonitor/MiniGraphWindow.xaml.cs:509-510`

The DWM query targets `_hwnd`, which at the first `RestorePlacement` (from the constructor, `:127`) still sits at its creation position — normally the primary monitor. The insets are DPI-scaled: 7 at 96 DPI, 14 at 192. Restoring onto a 200% monitor therefore allows only 7px of overhang where 14 is needed, leaving the strip ~7 physical px above flush; the reverse case allows 14 where 7 exists, so 7px of the strip falls off-screen. Small — but it is the same class of error the expansion was added to remove.

On the related question of whether DWM returns anything usable before the window is ever composed: the guard at `:519` would catch a zeroed rect (`visible.Left − outer.Left` goes negative), and `3144f8d`'s verification note ("a fresh start restores the strip to its saved (73,1153) exactly, with Y still above the pre-`05f7d92` clamp ceiling of 1147") is direct evidence that the insets **are** valid at construction time on at least one machine. The degenerate case that would slip past the guard is `visible == outer`, giving zero insets and silently reinstating the 7px creep.

**Fix.** An `AdjustWindowRectExForDpi` fallback computed for the *target* monitor would fix this and the degenerate case together.

**Status:** `open`

---

## C2-6 `[CLEANUP]` — `HorizontalStripMetrics.Width` describes content, but the result is applied as a window size

`NetworkMonitor/MiniGraphWindow.xaml.cs:456, 799`

Both sites set the derived width as `AppWindow.Size.Width`, which includes the ~7 DIP left and right frame — so the columns receive ~14 DIP less than the metric reserved. At the default 40 DIP height both font scales are floored at 1.0, so there is no slack to absorb it and every cell lands ~2% under nominal: `Internet` gets ~166 against the 170 it was tuned to, which is also `MiniTrafficSection.MinimumLabelledWidth` (`MiniTrafficSection.xaml.cs:20`). Harmless today only because the labels are already suppressed at that height by the `MinimumLabelledHeight` test.

**Fix.** Add the frame width when converting the content metric to a window size.

**Status:** `open`

---

## C2-7 `[CLEANUP]` — the spec's claim that the 34 DIP peak threshold is unreachable is wrong

`Documents/superpowers/specs/2026-08-05-horizontal-mini-graph-design.md` · `NetworkMonitor/MiniGraphWindow.xaml.cs:364, 550`

`e2ef93f` documented `HorizontalStripMetrics.ShowsPeak`'s 34.0 as "unreachable at a 40px floor, a retained guard, not exercised behaviour". But `ComputeShowPeak` is fed the **panel** height, not the window height, and at the 40 DIP window minimum the panel is ~32 — below 34, so the peak **is** dropped at the minimum strip height.

The behaviour is fine, probably desirable. The recorded understanding is not, and a future change to `MinimumHeight` would be reasoned about from a false premise.

**Fix.** Correct the note in the spec.

**Status:** `open`

---

## C2-8 `[RISK]` — `RootPointerPressed` uses `GetCursorPos` unconditionally, so a touch or pen drag teleports the widget

`NetworkMonitor/MiniGraphWindow.xaml.cs:834`

For a touch or pen contact the mouse cursor is stale, so `_dragOffsetX/Y` is captured against a position unrelated to the contact point and the first move jumps the widget to wherever the mouse happened to be.

**Fix.** Guard on `args.Pointer.PointerDeviceType == PointerDeviceType.Mouse`, or take the screen position from the pointer point rather than the cursor.

**Status:** `open`

---

## C2-9 `[CLEANUP]` — grab-point drift across a mid-drag DPI boundary

`NetworkMonitor/MiniGraphWindow.xaml.cs:846-847`

`_dragOffsetX/Y` is a fixed physical offset. When the window doubles in physical size on entering a 200% monitor, the offset does not — so the cursor's relative position within the widget jumps (grabbed at the centre of a 320px window, you are suddenly 25% across a 640px one). No divergence and no feedback loop; purely cosmetic. Scaling the offset by `newScale/oldScale` on a DPI change would make it seamless.

**Status:** `open`

---

## C2-10 `[CLEANUP]` — no re-placement on display topology change

`NetworkMonitor/MiniGraphWindow.xaml.cs:127, 151, 741`

`RestorePlacement` runs only from the constructor and on an orientation flip; `ShowWidget` does not otherwise re-clamp. A widget left on a monitor that is subsequently disconnected relies entirely on Windows' own window relocation.

**Fix.** Call `RestorePlacement` — or at least the clamp — from `ShowWidget` unconditionally. Costs nothing and closes the gap.

**Status:** `open`

---

## C2-11 `[CLEANUP]` — every conversion this chunk found a problem in is untestable

`NetworkMonitor/MiniGraphWindow.xaml.cs` (whole placement path)

`HorizontalStripMetricsTests` is good as far as it goes, but the DIP↔physical rounding round-trip, the clamp arithmetic and the max-position computation all live in the **app** project, which `NetworkMonitor.Tests` cannot reference (Models + Core only, per CLAUDE.md).

**Fix.** Extract a `PlacementMath` into `NetworkMonitor.Core/Widget` — inputs: saved DIP size, scale, bounds rect, frame insets; output: a `RectInt32`. That would let C2-1, C2-3 and C2-6 be pinned by tests rather than by a manual multi-monitor walkthrough.

**Status:** `open`

---

## Files reviewed

- `NetworkMonitor/MiniGraphWindow.xaml.cs`, `NetworkMonitor/MiniGraphWindow.xaml`
- `NetworkMonitor.Core/Widget/HorizontalStripMetrics.cs`
- `NetworkMonitor.Tests/HorizontalStripMetricsTests.cs`
- `NetworkMonitor.Services/Data/Settings.cs` (the `MiniGraph*` placement keys)
- `NetworkMonitor/Views/Controls/MiniTrafficSection.xaml`, `MiniTrafficSection.xaml.cs`
- `NetworkMonitor/app.manifest`
- Commit messages `8023ffa`, `abea7c8`, `05f7d92`, `3144f8d`, `7be89bc`, `819f390`, `7cc385d`, `e2ef93f`

## User findings

None. Co-reviewed 2026-08-11 — no `U2-n` IDs assigned.

## Co-review outcome

**All 11 findings confirmed for fixing.** None rejected, none deferred, none marked `won't-fix`.

That includes **C2-11**, so the `PlacementMath` extraction into `NetworkMonitor.Core/Widget` is in scope — C2-1, C2-3 and C2-6 get pinned by tests rather than by a manual multi-monitor walkthrough.

**C2-2 and C2-5 cannot be closed by code alone.** Both turn on WinUI's own `WM_DPICHANGED` handling, and the DPI *transition* path has never been walked on real hardware (`8023ffa` was verified on a primary 4K at 200%, where no transition occurs). The fix may be applied and the tests may pass, but neither finding moves to `fixed` until the mixed-DPI multi-monitor check in the ledger's *Manual verification* section is done.

They stay `open` because nothing has been fixed yet; the fix phase runs once every chunk is co-reviewed.

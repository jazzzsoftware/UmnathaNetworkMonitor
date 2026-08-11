# Chunk 5 — Tests & incidentally-touched code

Range `c07260c..b215581`. Ledger: `progress.md`.

**9 findings — 2 BUG · 4 RISK · 3 CLEANUP/PERF. All `open`.** Plus a coverage-gap inventory at the end.

## Test suite result

```
dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj -v q
Passed! - Failed: 0, Passed: 307, Skipped: 0, Total: 307, Duration: 306 ms
```

Clean build, **307/307 pass**. Run during the review, output observed — not assumed.

## What was verified as correct

- **`AxisScale` extraction is the model of how to do this.** `TrafficAreaChart.RoundAxisMax` had a hand-rolled decade-ceiling that turned a 2 Gb/s peak into a 10 Gb/s axis. The logic moved to `NetworkMonitor.Core/Charting/AxisScale.cs`, the call site collapsed to one line, and `AxisScaleTests.cs:36` is a genuine **property test** (`NiceMax >= value` swept over ~70 decades), not a happy-path echo. The 1-2-5-10 ladder comment even explains the halving invariant the mid-gridline depends on.
- **Tests assert against literals, not reimplementations.** `HorizontalStripMetricsTests.WidthSumsEveryVisibleCellPlusGapsAndPadding` asserts the bare number `728.0` with the arithmetic in a comment. Nothing in the new test files re-derives the production formula.
- **The `Watchdog` fix is small, correct, and has a test that would catch its regression** (`Watchdog.cs:27` + `WatchdogTests.cs:52`).
- **The `UpdateChecker` error-classification split is good design.** Separating "couldn't reach the server" (info, ordinary) from "server answered garbage" (error), and refusing to treat an `HttpClient` timeout as user cancellation (`UpdateChecker.cs:34`, `UpdateService.cs:103`), fixes a real class of bug — a cancelled outcome makes the caller suppress the banner entirely. **(But see C5-1: one arm of it is unreachable.)**
- **The SHA-256 verification path is untouched.** `ChecksumVerifier.cs`, `UpdateDownloader.cs` and `ReleaseInfoParser.cs` have **zero diff** in this range; the installer-name-matched checksum lookup is intact.

---

## C5-1 `[BUG]` — the auto-update client logs nothing on a garbage response, and the test that claims to cover it asserts nothing

`NetworkMonitor.Tests/Update/UpdateCheckerTests.cs:142-147` · `NetworkMonitor.Core/Update/UpdateChecker.cs:104-108` · `NetworkMonitor.Core/Update/ReleaseInfoParser.cs:39`

`AnUnreadableResponseIsStillLoggedAsAnError` captures `loggedError` at line 144, wires it at line 147 — and **never asserts on it**. It only checks `Cancelled` and `Availability`.

Worse, if it *did* assert, it would fail. `ReleaseInfoParser.TryParseVersionTag` catches `JsonException` internally (`:39`) and returns `false`, so `Evaluate` takes the `TryParseVersionTag` false branch and **never reaches the `catch` that calls `_logError`** (`UpdateChecker.cs:104-108`).

**Why it matters.** The error-logging arm added in this range is unreachable for the one realistic fault it exists for. A publicly auto-updating client that receives a corrupt payload, an HTML error page or a GitHub rate-limit response now logs **nothing at all** — while the sibling test at line 110 correctly proves the offline path logs info. When a user reports "updates stopped working", there is no evidence to read.

**Fix.** Either assert `Assert.NotEmpty(loggedError)` and make the malformed-JSON path actually log — distinguishing "not JSON" from "no `tag_name`" in `TryParseVersionTag` — or rename the test to what it checks and delete the dead `loggedError` capture. The first is the right fix.

**Status:** `fixed` — 2026-08-11, fix-phase batch 3. Took the harder fix. `TryParseVersionTag` gained a three-argument overload reporting *why* it failed (malformed JSON, no `tag_name`, an uncomparable tag, or an empty body); `UpdateChecker.Evaluate` logs that through `_logError` on the false branch it previously took silently. The test now asserts `NotEmpty(loggedError)` plus `IsAssignableFrom<JsonException>` — note `JsonDocument.Parse` throws the derived `JsonReaderException`, so exact-type assertions fail. A second test covers the valid-JSON-no-tag case (a GitHub rate-limit body), which logs a non-`JsonException`.

---

## C5-2 `[BUG]` — `_lastFlushUtc` goes stale across navigation, smearing a phantom baseline over the whole chart

`NetworkMonitor/ViewModels/InternetViewModel.cs:81, 230` · `NetworkMonitor/ViewModels/LocalViewModel.cs:110, 298`

Both view models are DI singletons (`App.xaml.cs:167-169`), but `InternetPage`/`LocalPage` subscribe to `TrafficTracker.Flushed` on `Loaded` and unsubscribe on `Unloaded` (`InternetPage.xaml.cs:82-90`). So `_lastFlushUtc` **freezes while the page is off-screen** and is not reset by `LoadAsync`.

On returning after N minutes, the first flush computes `intervalStartUtc` = N minutes ago and hands it to `FlushSpread.Distribute`, which normalises by the sum of *in-window* overlaps — so the entire flush is spread evenly across **every bucket in the window** rather than the last one or two. For a 5-minute/1-second window and a flush carrying tens of MB, that lifts a uniform phantom floor of order Mb/s across the whole chart, on top of freshly-loaded historical data.

Same effect after sitting on the 24h range (where `ApplyLiveFlushAsync` is skipped entirely) and then switching to 5m.

**Fix.** Reset `_lastFlushUtc = DateTime.MinValue` in `LoadAsync`, so the first flush after any reload falls back to the existing one-bucket-wide default at `:230` / `:298`. One line each.

**Status:** `fixed` — 2026-08-11, fix-phase batch 4. Reset in `SeedWindowState` in both view models rather than in `LoadAsync` directly — `SeedWindowState` runs on every reload and is where the rest of the window state is established, so a future load path cannot miss it.

---

## C5-3 `[RISK]` — the two rules that keep the widget honest live in `Services` and are therefore untestable

`NetworkMonitor.Services/Traffic/LiveTrafficFeed.cs:172, 191-195` · `NetworkMonitor.Services/Platform/MiniGraphState.cs:134-139`

Against CLAUDE.md's own "New pure logic that needs tests goes in Core, not Services".

- `LiveTrafficFeed.cs:172` (skip `ProcessName == "System"`) and `:191-195` (skip anything not `FlowCategory.Data`) are the **entire reason** the widget's numbers agree with the Internet and Local tabs — the comments say as much, and chunk 3 verified they currently do match. **Zero tests.** A pure `(entries, localDeltas) → (wan, lan)` aggregator in `Core/Traffic` would be trivially testable.
- `MiniGraphState.ApplySection` enforces "the last section can never be turned off" — the widget's only real invariant, driven from three separate UIs (tray menu, widget right-click menu, Settings page). **Zero tests.** It needs `Settings` only for storage; the count-and-refuse decision is pure.

The rest of this feature followed the layering rule well (see chunk 4), which makes these two the exceptions rather than the pattern.

**Status:** `open`

---

## C5-4 `[RISK]` — `_miniGraphVisible` is set before the window is constructed, so a construction failure disables the widget until restart

`NetworkMonitor/App.xaml.cs:314`

The flag is assigned `= visible` before `new MiniGraphWindow(...)`. If construction throws, the catch logs and the flag stays `true` — so every later toggle is a no-op and the widget is unavailable for the rest of the session.

**Fix.** Assign the flag after a successful show.

**Status:** `open`

---

## C5-5 `[RISK]` — `DigestReportView` never unsubscribes

`NetworkMonitor/Views/Controls/DigestReportView.xaml.cs:41-47`

`Loaded`, `SizeChanged` and `_resizeTimer.Tick` are wired in the constructor with no `Unloaded` teardown. A tick landing after unload calls `RenderAsync` on a detached control (`XamlRoot` null → `PreviewDpi` falls back), and the running `DispatcherTimer` roots the control until it fires. Not a growing leak, but the timer should stop on `Unloaded`.

**Status:** `open`

---

## C5-6 `[RISK]` — `RetentionProbe` prints the user's Windows username, in a public repo

`Tools/RetentionProbe/Program.cs:49`

The tool prints the full database path, which for a copy under the user's profile contains their Windows username — the **one** piece of identifying data on stdout, in a diagnostic whose whole purpose is to have its output pasted into an issue. The repo is public.

Everything else it prints is clean: row counts, page counts, minute epochs, MB totals. No MACs, IPs, device names or process names.

**Fix.** Print only the file name.

**Status:** `open`

---

## C5-7 `[RISK]` — `RetentionProbe`'s safety guard is a bare prefix compare, on a tool that deletes

`Tools/RetentionProbe/Program.cs:34`

`dbPath.StartsWith(liveFolder)` has no trailing-separator check (so `UmnathaNetworkMonitorBackup\` is wrongly *refused* — harmless) and does not resolve junctions, symlinks, UNC paths or `subst` drives pointing at the live folder (which is **not** harmless).

For the record: **this tool is not read-only and cannot be made so** — issuing `DELETE FROM` and `PRAGMA wal_checkpoint(TRUNCATE)` on a read-write connection is its purpose. The guard plus the file-header warning are the only protection there is.

**Fix.** Tighten to a canonicalised comparison using `Path.GetFullPath` and `Path.EndsInDirectorySeparator`.

**Status:** `open`

---

## C5-8 `[PERF]` — `DigestWorker.GenerateMissedWindowsAsync` re-evaluates every skipped window on every cycle

`NetworkMonitor.Services/Digest/DigestWorker.cs:130-142` · `CatchUpAsync.cs:107-112`

A window with no data never advances `GetLastPeriodEndUtcAsync`, so it stays in `MissedWindows` and costs 3 `AnyAsync` queries per cycle **forever** (bounded only by `DigestPurgeDays`). This now runs at startup too, so a cold start pays it.

**Fix.** Record a high-water mark rather than re-deriving from the last successful period.

**Status:** `open`

---

## C5-9 `[CLEANUP]` — small items in touched code

- **`UpdateViewModel.cs:19`** — `_manualResult` is never cleared, so it holds the last manual check result for the app's lifetime. The race it guards is safe only because `CheckManuallyAsync` is UI-thread-invoked; worth a comment or a `null` reset after `Apply`.
- **`Tools/RetentionProbe/Program.cs:28`** — `int.Parse(args[1])` is unguarded; a typo produces an unhandled `FormatException` stack trace instead of the usage text. Use `int.TryParse`.
- **`LiveRateBuffer.AddInterval` silently drops bytes on a stale flush.** If `endEpoch < _lastEpoch` (a drain arriving after a newer `Add`), `Advance` is a no-op, `firstEpoch` clamps above `endEpoch`, the loop at `:68` produces an empty `bucketStarts`, and `Distribute` returns an empty array. No crash, but the bytes vanish with no diagnostic. Untested. Related to chunk 3's C3-1.

**Status:** `open`

---

## Coverage gaps

Specific untested edge cases, per production file. Ordered roughly by value.

**Highest value first:** every `FlushSpread` test hardcodes `bucketSeconds = 1.0`, but the Internet and Local view models pass `_windowBucketSeconds`, which is **60+ on wide ranges**. The multi-second-bucket path that production actually takes has **no coverage at all** — and C3-4 and C5-2 are both defects in exactly that path.

**`FlushSpread.cs`** — negative `totalBytes`; an empty `bucketStartsUtc` list; `bucketSeconds <= 0`; `bucketSeconds` other than `1.0` (above).

**`LiveRateBuffer.cs`** — well covered for gaps, eviction and zero-fill. Not covered: a **small backwards clock step** landing inside the held window (this is C3-1); two consecutive `AddInterval` calls sharing a boundary second (the exact production pattern from `LiveTrafficFeed.cs:203-211` — chunk 3 proved by hand that it is *not* double-counted, but nothing pins it); `AddInterval` with `intervalEnd <= intervalStart` (the `Add` fallback at `:50`); `AddInterval` before any `Add`; a stale `AddInterval` whose end precedes `_lastEpoch` (C5-9); negative byte counts; `capacitySeconds == 1`.

**`HorizontalStripMetrics.cs`** — `Width` with all four sections off (returns 30.0; `MiniGraphState` prevents it, but `Width` itself has no guard); `FontScale` at 0 or negative height; `Width` with `fontScale` 0 or negative; `ShowsPeak` just below the 34.0 threshold (30.0 is tested, 33.9 is not — and C2-7 shows the threshold *is* reachable); `ClampHeight` at exactly `MinimumHeight`/`MaximumHeight`.

**`MiniGraphFormatter.cs`** — good coverage of both line forms. Not covered: the `Scaled` boundary at exactly `10.0`; `Scaled` below 0.05, where `"0.#"` renders `"0"` — **the precise failure the comment at line 70 claims to prevent** ("a slow link reads as zero"); `UnknownDevices` with a negative count; a successful result with zero/negative/NaN rates.

**`AxisScale.cs`** — every `InlineData` is `>= 1.0`. Sub-unit values (0.3 → 0.5) are reachable when a large bucket holds a few bytes, and are untested. `double.NegativeInfinity` untested (`PositiveInfinity` is). The exact decade boundary `10.0` untested.

**`Watchdog.cs`** — cancellation and timeout completing simultaneously (the code prefers cancellation via the `ThrowIfCancellationRequested` ordering; nothing pins that).

**`UpdateChecker.cs`** — the reworded "download is incomplete" message at `:94` (the change that dropped the doubled `Version v`); a release whose `tag_name` parses but whose assets are missing; a `cancellationToken` cancelled *while* a non-`OperationCanceledException` is in flight.

**Entirely untested new logic:** nothing in Core/Models is wholly uncovered — every new Core/Models file has at least one test file. The gap is not in Core; it is that the widget's two most consequential rules were placed in `Services` where the test project cannot reach them (**C5-3**), and that the whole placement/DPI path lives in the app project (**C2-11**).

---

## Files reviewed

- `NetworkMonitor.Tests/` — `AxisScaleTests.cs`, `FlushSpreadTests.cs`, `HorizontalStripMetricsTests.cs`, `LiveRateBufferTests.cs`, `MiniGraphFormatterTests.cs`, `TrafficRateFormatterTests.cs`, `WatchdogTests.cs`, `Update/UpdateCheckerTests.cs`
- `NetworkMonitor.Core/Update/UpdateChecker.cs`, `ReleaseInfoParser.cs`, `ChecksumVerifier.cs`, `UpdateDownloader.cs`
- `NetworkMonitor.Core/Common/Watchdog.cs`
- `NetworkMonitor.Services/Update/UpdateService.cs`
- `NetworkMonitor.Services/Digest/DigestWorker.cs`, `DigestChartRenderer.cs`, `CatchUpAsync.cs`
- `NetworkMonitor.Services/SpeedTest/SpeedTestService.cs`
- `NetworkMonitor/ViewModels/InternetViewModel.cs`, `LocalViewModel.cs`, `AllDevicesViewModel.cs`, `SettingsViewModel.cs`, `UpdateViewModel.cs`
- `NetworkMonitor/Views/Controls/DigestReportView.xaml.cs`
- `NetworkMonitor/App.xaml.cs`
- `Tools/RetentionProbe/Program.cs`

## User findings

None. Co-reviewed 2026-08-11 — no `U5-n` IDs assigned.

## Co-review outcome

**All 9 findings confirmed for fixing.** None rejected, none deferred, none marked `won't-fix`.

**The coverage-gap inventory above is in scope too**, not merely a record. Highest value first, per that section: `FlushSpread` has no coverage at all for `bucketSeconds != 1.0`, while production passes `_windowBucketSeconds` — 60+ on wide ranges — and both C3-4 and C5-2 are defects in exactly that path. Close that gap before or alongside those two fixes.

Two notes carried into the fix phase:

- **C5-1 takes the harder of the two fixes offered.** Make the malformed-JSON path actually log — distinguishing "not JSON" from "no `tag_name`" in `ReleaseInfoParser.TryParseVersionTag` — and then assert `Assert.NotEmpty(loggedError)`. Renaming the test to match what it currently checks would close the finding while leaving a publicly auto-updating client silent on corrupt payloads.
- **C5-9 is three unrelated items** (`_manualResult` never cleared, unguarded `int.Parse`, `AddInterval` silently dropping bytes on a stale flush). The third is related to C3-1 and should be fixed with it, not with the other two.

They stay `open` because nothing has been fixed yet; the fix phase begins now that all five chunks are co-reviewed.

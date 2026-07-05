# Chunk 5 — Daily digest (reviewed 2026-06-23)

Overall: the **most polished** subsystem — it went through the SDD task process, and `DigestSummaryBuilder` + `DigestSchedule` are pure and unit-tested (63/63). `DigestWorker` is resilient (try/catch loops, catch-up bounded by retention). Findings here are mostly **minor leaks/cleanups and consistency**, plus one **UI-thread render** to verify in Chunk 6. No correctness bugs in the core summary/schedule logic.

---

## Findings

### C5-1 [CLEANUP] `CanvasPathBuilder` not disposed in `DrawDonut`
`DigestChartRenderer.cs` (~`:258`) — `CanvasPathBuilder pathBuilder = new CanvasPathBuilder(device);` is used to build each donut slice's geometry but is never disposed (it's `IDisposable`). The resulting `CanvasGeometry` *is* wrapped in `using`, but the builder itself leaks a COM object per slice per render. (Sibling `CanvasTextFormat`s are correctly `using`-scoped — those were fixed during SDD.)
**Fix:** `using CanvasPathBuilder pathBuilder = new(device);`.
Status: **FIXED 2026-06-25 (leaks batch)** — `pathBuilder` is now a `using` declaration; disposed per slice.

### C5-2 [PERF — verify in Chunk 6] PDF chart rendering runs synchronously in `BuildPdf`
`DigestPdfExporter.BuildPdf` calls `chartRenderer.RenderTrafficChart/Split` (Win2D `CanvasRenderTarget` → PNG) synchronously. The in-app preview path was deliberately moved off the UI thread during SDD, but the **PDF export** path goes through `ReportsViewModel.BuildPdf`. If the Reports page export handler calls it on the UI thread, the Win2D render + QuestPDF generation blocks the UI.
**Action:** verify the export click handler offloads to `Task.Run` (Chunk 6). If not, flag there.
Status: **FIXED 2026-06-25 (batch 3)** via C6-3 — `ReportsPage.SaveBytesAsync` now builds the PDF/CSV inside `Task.Run` after the save dialog, so `BuildPdf`'s Win2D + QuestPDF work no longer runs on the UI thread.

### C5-3 [CLEANUP] Two CSV exporters use inconsistent escaping strategies
`DigestCsvExporter.Quote` *always* wraps a value in quotes and is applied only to some columns (ProcessName, DisplayName, Vendor); IP, MAC, Type, `LastSeenDisplay`, `ConnectActivity` are emitted unquoted. Meanwhile `DeviceCsvExporter.Escape` (Chunk 6 area) *conditionally* quotes only when needed and covers every field. The unquoted columns are safe today (no commas), but the divergence is fragile and worth unifying on one escape helper.
Status: **FIXED 2026-06-25 (CSV pair)** — new shared `Services/CsvField.Escape` (conditional quoting). Both exporters route **every** field through it; `DeviceCsvExporter.Escape` and `DigestCsvExporter.Quote` removed. (CsvField linked into the test csproj.) All exporter/round-trip tests still pass.

### C5-4 [RISK, low] CSV formula injection not mitigated
`DigestCsvExporter` / `DeviceCsvExporter` — a `ProcessName`/`DisplayName`/`Vendor` beginning with `=`, `+`, `-`, or `@` is written verbatim; Excel/Sheets interpret it as a formula on open. Low severity for a local single-user tool, but a known CSV hardening gap.
**Fix (if desired):** prefix such fields with `'` or a space when exporting.
Status: **FIXED 2026-06-25 (CSV pair)** — `CsvField.Escape` prefixes any value starting with `= + - @` with a leading `'` (then applies CSV quoting). Applies to both exporters. (Round-trip tests unaffected — no test value starts with those chars.)

### C5-5 [CLEANUP] `DigestWorker` reads `DateTime.Now` twice per loop iteration
`DigestWorker.cs` (~`:39-40`) — `NextRunLocal(DateTime.Now, …)` then `delay = nextRunLocal - DateTime.Now` sample the clock twice (tiny drift). Assign once to a local. (Previously noted as a deferred SDD minor.)
Status: **FIXED 2026-06-25 (cleanups)** — `DateTime now = DateTime.Now;` sampled once, used for both `NextRunLocal` and the delay.

---

## Notes (not findings)
- **Strength:** `DigestSummaryBuilder` and `DigestSchedule` are pure, deterministic, and unit-tested; `DigestGenerator` uses `TrafficRollups` (the long-lived aggregate) as the traffic source — correct choice.
- `DigestSummary.OnlineCount`/`OfflineCount` reflect the *current* device state at generation time, not the windowed state — by design; noted for awareness.
- `DigestSchedule` 24h windows are computed via local-time boundaries → `ToUniversalTime`; around DST transitions a window can be ±1h. Edge-case only.
- Unit base: headline/byte sizes use binary (1024) consistently here; the SI-vs-binary inconsistency is the rate formatter (C2-7), not this chunk.
- QuestPDF license is set once at startup — correct.

## Triage / actions
No fixes applied (record-only). Priority when fixing: C5-2 (verify UI-thread render in Ch6), C5-1 (leak). C5-3/C5-4/C5-5 are cosmetic/low.

---

## Files reviewed
- `NetworkMonitor/Services/DigestWorker.cs`
- `NetworkMonitor/Services/DigestSchedule.cs`
- `NetworkMonitor/Services/DigestGenerator.cs`
- `NetworkMonitor/Services/DigestSummaryBuilder.cs`
- `NetworkMonitor/Services/DigestChartRenderer.cs`
- `NetworkMonitor/Services/DigestPdfExporter.cs`
- `NetworkMonitor/Services/DigestCsvExporter.cs`

## User findings (reconciled)

### U5-1 [ACTION — cross-cutting] Refactor `Services` folder into sub-folders
The `NetworkMonitor/Services` folder has grown large; group its files into sub-folders by concern (e.g. `Services/Scanning`, `Services/Traffic`, `Services/Digest`, `Services/Backup`, `Services/Startup`). Surfaced while reviewing the 7 digest files, but **scope is the whole `Services` folder**, not just digest — so this is a codebase-wide structural change, not Chunk-5-only.
**Implications to handle in the fix phase:** namespaces will change if folders map to namespaces (update `using`s and DI registrations in `App.xaml.cs`); update `NetworkMonitor.csproj`/`.slnx` if any explicit file references exist; keep test references (`NetworkMonitor.Tests`) compiling. Decide foldering scheme before moving.
Status: open (batch — fix phase; codebase-wide, sequence carefully vs. other Services edits)

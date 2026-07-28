# Manual Smoke Checklist

Post-change walkthrough for the regression classes the 2026-07-27 review introduced: the shifted
live window on both traffic tabs, the fixed query shapes, the update path's new shutdown route, and
the shared rate window. Run after any change touching `LocalViewModel`, `InternetViewModel`, the
traffic tracker/collector, or the update service.

Everything below is DB- or timing-bound and therefore outside the unit tests. **No DB delete is
required for this branch** — no schema changed.

Estimated time: ~20 minutes, most of it waiting for the live window to move.

## Live window — Local tab

P = Pass, F = Fail, [ ] = Not Tested

- [P] Open **Local** on the default **5-minute** range. The chart scrolls smoothly, one bucket per second, with no visible flicker or full-grid flash.
- [P] Leave it running for 5+ minutes: the **time-axis labels move with the window** (they used to freeze at whatever they were on navigation).
- [P] Start a LAN transfer (a NAS copy is ideal). The new app/device row **appears within a second or two** — it does not wait for the next full reload.
- [P] Expand a drill-down, then let a new device start talking. The open drill-down **stays open** and the new row appears without the grid re-sorting under it.
- [P] After several minutes, hit refresh (or change range and come back). The totals **match** what the live window was showing — no drift.
- [ ] Rows for flows that stop **age out** of the window rather than lingering at their last total.
- [ ] Repeat the first three checks on **1 h** and **6 h**.

## Live window — Internet tab

- [P] Same as above on **5 min**, **1 h** and **6 h**. The Internet tab got the identical shift (F-1), so it can fail independently.
- [P] Both tabs open in sequence over several minutes: neither leaves a spinner up, and neither shows a stale total after switching back.

## Chart queries (the C4-10 rewrite)

- [P] Local chart draws on the **5-minute** range (raw-entry path) **and** on **1 h+** (rollup path).
- [P] Internet chart draws on both paths.
- [P] **Click a chart bucket to drill into it** on both tabs, on both paths. This is the path whose bucket-range filter moved from appended SQL to nullable parameters — if the parameters are wrong it will show everything, or nothing, rather than that bucket.
- [P] Switch the Local lens **By app ↔ By device** with a bucket selected; the chart and grid stay consistent.

## Live rate chips (the shared `RateWindow`)

- [P] During a transfer, rate chips appear on rows above 500 kbit/s and read plausibly.
- [P] Stop all traffic and wait: chips **age down to zero and disappear** rather than freezing at the last non-zero rate.
- [P] Leave live mode (pause / navigate away and back): chips clear, and re-populate when traffic resumes.

## Update path

- [P] **Check for updates** from Settings with no update available — the card shows "up to date", no error.
- [ ] With an update available: banner appears → **Download** shows whole-percent progress → **Cancel** mid-download leaves no error state and no partial file in the Updates folder.
- [ ] Download again to completion, then **Install**. The app performs its graceful exit (window placement saved, tray icon removed) and the installer runs.
- [ ] After the silent install the app returns **visible**, not hidden in the tray.
- [P] Launch once with no network — the check fails with a connection message rather than reporting "up to date".

## Storage retention

- [P] After upgrading over a build that predates the 1-hour raw retention, the first flush clears the multi-day backlog of raw rows. It is one large delete guarded by a 2-minute watchdog — if it times out it retries next cycle, so confirm the DB size settles rather than growing.
- [P] The daily digest and every range beyond 5 minutes still show history (they read rollups, which keep the full retention period).

Evidence, 2026-07-28, measured against a copy of the live database with the retention shortened to
2 minutes (the probe was shortened, not `TrafficTracker`):

- The backlog clear had already happened on the first run of `51c2911`. The raw tables held 1 432
  `TrafficEntries` + 4 908 `LocalTrafficEntries` spanning a 32-minute window, well inside the 1-hour
  retention.
- Purging every remaining raw row took **0.02 s against the 120 s watchdog** — 0.02% of budget, so
  the timeout branch is not a realistic risk at this data volume.
- **The file plateaus, it does not shrink.** 90.9 MB of which 68.4 MB (75%) was already free pages;
  after the purge free pages rose to 69.3 MB and the file stayed at exactly 90.9 MB. There is no
  `VACUUM` and no `auto_vacuum` anywhere in the codebase, so freed pages go on the freelist and are
  reused. Treat ~91 MB as the ceiling — a file that stops growing is the pass, not one that drops.
- The WAL went 48.2 MB → 0.0 MB on `PRAGMA wal_checkpoint(TRUNCATE)`, so a large live WAL is normal
  and collapses on the graceful exit path.
- Rollups were untouched by the purge — 14 526 `TrafficRollups` + 147 691 `LocalTrafficRollups`
  before and after — spanning 5 days. Of the minutes carrying raw rows, **zero** lacked a matching
  rollup minute, so nothing is lost when raw is purged.
- Item 54 confirmed in the UI: the 6-hour range still renders the 09:12 session whose raw rows were
  purged hours earlier, so that data is necessarily rollup-sourced. The neighbouring 09:18 gap is
  correct — the app was stopped 09:17:43–10:43.

## Cross-cutting

- [P] No unhandled-exception dialogs during any of the above.
- [P] With diagnostic logging on, the log shows no new exceptions from `TrafficTracker`, `UpdateService`/`UpdateDownloader`, or the view models.

## Why these items

| Check | Regression it guards |
|---|---|
| Chart scrolls, labels move | C4-1 window shift + C4-5 axis refresh — the whole live path was rewritten |
| New row appears mid-window | C4-2 — the reconcile could not insert at all in `reorder: false` mode |
| Totals match after minutes | C4-3 re-entrancy + C4-9 flow aging — stale snapshots used to win the race |
| Bucket drill-down | C4-10 — bucket-range moved from appended SQL to nullable parameters |
| Chips age to zero | C4-6 unconditional `Flushed` + the shared `RateWindow` running total |
| App visible after install | C1-1 / C1-13 — the installer route bypassed the graceful shutdown |
| Backlog clears, DB settles | C2-5 — raw retention dropped from 7 days to 1 hour |

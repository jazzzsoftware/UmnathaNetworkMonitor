# Manual Smoke Checklist

Post-change walkthrough for the regression classes the 2026-07-27 review introduced: the shifted
live window on both traffic tabs, the fixed query shapes, the update path's new shutdown route, and
the shared rate window. Run after any change touching `LocalViewModel`, `InternetViewModel`, the
traffic tracker/collector, or the update service.

Everything below is DB- or timing-bound and therefore outside the unit tests. **No DB delete is
required for this branch** — no schema changed.

Estimated time: ~20 minutes, most of it waiting for the live window to move.

## Live window — Local tab

- [ ] Open **Local** on the default **5-minute** range. The chart scrolls smoothly, one bucket per second, with no visible flicker or full-grid flash.
- [ ] Leave it running for 5+ minutes: the **time-axis labels move with the window** (they used to freeze at whatever they were on navigation).
- [ ] Start a LAN transfer (a NAS copy is ideal). The new app/device row **appears within a second or two** — it does not wait for the next full reload.
- [ ] Expand a drill-down, then let a new device start talking. The open drill-down **stays open** and the new row appears without the grid re-sorting under it.
- [ ] After several minutes, hit refresh (or change range and come back). The totals **match** what the live window was showing — no drift.
- [ ] Rows for flows that stop **age out** of the window rather than lingering at their last total.
- [ ] Repeat the first three checks on **1 h** and **6 h**.

## Live window — Internet tab

- [ ] Same as above on **5 min**, **1 h** and **6 h**. The Internet tab got the identical shift (F-1), so it can fail independently.
- [ ] Both tabs open in sequence over several minutes: neither leaves a spinner up, and neither shows a stale total after switching back.

## Chart queries (the C4-10 rewrite)

- [ ] Local chart draws on the **5-minute** range (raw-entry path) **and** on **1 h+** (rollup path).
- [ ] Internet chart draws on both paths.
- [ ] **Click a chart bucket to drill into it** on both tabs, on both paths. This is the path whose bucket-range filter moved from appended SQL to nullable parameters — if the parameters are wrong it will show everything, or nothing, rather than that bucket.
- [ ] Switch the Local lens **By app ↔ By device** with a bucket selected; the chart and grid stay consistent.

## Live rate chips (the shared `RateWindow`)

- [ ] During a transfer, rate chips appear on rows above 500 kbit/s and read plausibly.
- [ ] Stop all traffic and wait: chips **age down to zero and disappear** rather than freezing at the last non-zero rate.
- [ ] Leave live mode (pause / navigate away and back): chips clear, and re-populate when traffic resumes.

## Update path

- [ ] **Check for updates** from Settings with no update available — the card shows "up to date", no error.
- [ ] With an update available: banner appears → **Download** shows whole-percent progress → **Cancel** mid-download leaves no error state and no partial file in the Updates folder.
- [ ] Download again to completion, then **Install**. The app performs its graceful exit (window placement saved, tray icon removed) and the installer runs.
- [ ] After the silent install the app returns **visible**, not hidden in the tray.
- [ ] Launch once with no network — the check fails with a connection message rather than reporting "up to date".

## Storage retention

- [ ] After upgrading over a build that predates the 1-hour raw retention, the first flush clears the multi-day backlog of raw rows. It is one large delete guarded by a 2-minute watchdog — if it times out it retries next cycle, so confirm the DB size settles rather than growing.
- [ ] The daily digest and every range beyond 5 minutes still show history (they read rollups, which keep the full retention period).

## Cross-cutting

- [ ] No unhandled-exception dialogs during any of the above.
- [ ] With diagnostic logging on, the log shows no new exceptions from `TrafficTracker`, `UpdateService`/`UpdateDownloader`, or the view models.

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

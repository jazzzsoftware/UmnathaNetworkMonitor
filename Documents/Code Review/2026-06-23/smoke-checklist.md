# Manual Smoke Checklist

Quick post-change walkthrough to catch the regression classes that the 2026-06-23 code review
introduced (incremental device grids, scan gating/MAC keying, partial MVVM move). Run after any
change touching the device pages, the reconciler, scanning, or navigation.

Estimated time: ~5 minutes.

## Scanning

- [Pass] Click **Scan Network** on the Devices tab — status shows "Scanning…", then a "Last scan" summary with counts.
- [Pass] A second scan runs (no permanent "already scanning" lockout from the concurrent-scan gate).
- [Pass] A newly-connected device appears after a scan; a disconnected one flips to offline / "Xm ago".
- [Pass] A device with a randomized (private) MAC shows the private-MAC pill and isn't duplicated across scans.
- [Pass] Let the background timer fire one scan unattended — the grid updates without a manual scan.

## Device grid — incremental reconciler & highlight

- [Pass] Unapproved devices show the **red row tint**; approved devices do not.
- [Pass] **Approve** a device from the All tab — its red tint clears **immediately** (no tab-switch needed).
- [Pass] **Edit** a device's friendly name/type/notes — the row text updates immediately.
- [Pass] **Delete** a device — it disappears immediately and does not reappear until next scanned.
- [Pass] After a background scan, existing rows keep correct tint and online status (no stale highlight).
- [Pass] **Sort** by each column — order changes, indicator arrow shows, preference persists after app restart.
- [Pass] **Search/filter** narrows the list and clears correctly.

## Tab switching (cached frames must reload)

- [Pass] Switch All → Approved → Unapproved → History → back to All. Each tab shows **current** data, not stale.
- [Pass] Approve a device on All, switch to Approved — it's there; switch to Unapproved — it's gone.
- [Pass] Click a device's History action — jumps to History tab populated for that MAC.
- [Pass] Leaving History clears its search box.

## CSV import / export (approved devices)

- [Fixed] Export approved devices to CSV — file opens with expected rows, fields escaped correctly. Exported but did not open. Was never added.
- [Pass] Import a CSV — added/updated counts reported; matched by MAC; existing devices updated not duplicated.

## Cross-cutting

- [Pass] No unhandled-exception dialogs during any of the above.
- [Pass] With diagnostic logging enabled, the log file records scans/events and no unexpected exceptions.
- [Pass] Approved-device backup file is written (check backup folder timestamp after first run of the day).

## Why these items

| Check | Regression it guards |
|---|---|
| Red tint clears on approve | Imperative `LoadingRow` highlight goes stale (commits 5b0bbc9 / f203c67) |
| Tab data is current | Cached frames not reloading on re-selection |
| Second scan runs | C4-3 concurrent-scan gate over-blocking |
| No duplicate MACs | MAC canonicalization batch (30810a2) keying changes |
| Edit/Delete reflect immediately | `_allDevices` / `Devices` shared-reference invariant in the reconciler |

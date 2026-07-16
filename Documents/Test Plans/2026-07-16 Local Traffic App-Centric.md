# Local Traffic App-Centric — Manual Test Plan

Feature: Local tab redesigned device→app-centric (apps over the LAN, remote device as a per-app drill-down); Internet made WAN-only. Commits `a2f04ad..58ce85b`.

Tick each box as you verify. **Expected** describes the pass condition; if it doesn't match, note it under the item.

---

## 0. Setup (do this first)

- [ ] **Delete the database.** Close the app. Delete `networkmonitor.db` (+ `-wal`/`-shm`) from the data folder (Settings → Data folder shows the path). *Required — the schema changed with no migration; skipping this causes silent Local-data loss.*
- [ ] Build & run (x64). App launches normally.
- [ ] Let it run a few minutes so at least one traffic flush (per the Traffic scan interval) has happened.

---

## 1. Local tab — basics

- [ ] Open **Traffic → Local**. A grid of **apps** appears (not devices). **Expected:** columns **App · Peers · Download · Upload · Total**.
- [ ] The first row is **All Apps** with the summed totals; status line reads "N apps · X total".
- [ ] Rows are sorted by **Total** descending.
- [ ] The **Peers** column shows the app's top peer name (e.g. `SurfratNas`) and, for multi-peer apps, a `Name +N` summary. Hover shows the full peer list tooltip.
- [ ] An app whose peer isn't a known device shows the **bare IP** as the peer.

## 2. Drill-down (per-device breakdown)

- [ ] Click an app row that talks to more than one device. **Expected:** a details strip expands showing per-device rows (Device name over its IP · Download · Upload · Total).
- [ ] Single-peer apps and the **All Apps** row do **not** expand a redundant strip (the single peer already shows in the Peers column).
- [ ] In the expanded strip, the **sum of the child device rows equals the app's parent Total** (Download, Upload, Total all reconcile).

## 3. NAS / SMB attribution (the headline case)

- [ ] Start a file copy to/from the NAS/SMB share (e.g. a Macrium backup or a large file copy).
- [ ] Within one Traffic interval, a **`System`** app row grows on the Local tab.
- [ ] Expand `System` → it **drills down to the NAS device** (e.g. `SurfratNas`) with the bytes.

## 4. Browser / app LAN traffic

- [ ] Open a LAN device's web UI in a browser (e.g. a router/NAS admin page at `192.168.x.x`).
- [ ] The traffic attributes to the **browser app** (chrome/msedge/etc.), and expanding it shows the target device as the peer.

## 5. D1 — Internet and Local are complementary (no double-count)

- [ ] While the NAS copy from step 3 is running, open the **Internet** tab. **Expected:** those LAN bytes are **absent** from Internet (Internet is WAN-only now).
- [ ] `System` does **not** appear on the **Internet** tab (it's excluded there) but **does** on Local.
- [ ] A genuine internet transfer (e.g. a speed test or a download) appears on **Internet** but **not** on **Local**.

## 6. Sub-minute chart (5-minute range)

- [ ] Set the Local range to **5m**. **Expected:** the chart is **populated** (not empty) and updates roughly per Traffic interval.
- [ ] The grid totals for the 5m window match what the chart shows (grid reads the same sub-minute source).

## 7. Live behaviour

- [ ] Watch the Local tab live during an active transfer. The relevant app row's **Total climbs live**.
- [ ] With an app **expanded** during a live transfer, its **child device rows also update live** (parent stays equal to the sum of children — no stale/frozen children).
- [ ] If the app starts talking to a **new device** mid-window, that new peer **appears** in the drill-down without needing a manual refresh.
- [ ] When a **brand-new app** first sends LAN traffic, it appears in the list (a full refresh happens under the hood).

## 8. Chart filtering & history

- [ ] Click an app row → the chart **filters to that app**; the chart label shows the app name.
- [ ] Click the **All Apps** row → the filter clears (chart shows all apps again).
- [ ] Click a point/bucket on the chart → the grid switches to **that bucket's history** (mode badge shows History/Paused); the label shows the timestamp.
- [ ] Click the mode badge to **resume Live**; the grid returns to live totals.

## 9. Time ranges & persistence

- [ ] Cycle ranges **5m / 1h / 6h / 24h / 7d** — each loads without error and the chart/labels update.
- [ ] Change the range, navigate away to another page and back — the **selected range is remembered**.

## 10. Digest report — Local section is app-keyed

- [ ] Generate/open a digest report (or wait for the daily one). The **Local traffic** section is keyed by **App**, with columns **App · Peer · Download · Upload · Total**.
- [ ] The Local chart in the report labels bars by **app name** (not device).
- [ ] **Export all reports to CSV** — the Local section has App + Peer text columns and the **Raw + Friendly** paired byte columns (Download, Upload, Total).
- [ ] The **PDF** Local section matches (App · Peer · Download · Upload · Total).

## 11. Retention (optional / longer-run)

- [ ] After the app has run beyond the Traffic retention window (Settings → TrafficPurgeDays), old Local rows/entries are purged (no unbounded DB growth).

## 12. Presentation

- [ ] Toggle **Dark** and **Light** mode — the Local grid, drill-down strip, and chart render correctly in both.
- [ ] (Optional) Cross-check: the Local **App** column header reads "App"; the Internet tab uses "Application" — flag if you'd prefer them consistent.

---

## Sign-off

- [ ] All critical paths (0–7, 10) pass.
- [ ] Any issues found are logged below.

**Issues found:**

1.
2.
3.

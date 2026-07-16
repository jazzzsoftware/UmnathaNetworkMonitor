# Local Traffic — App-Centric Redesign — Design Spec

- **Date:** 2026-07-16
- **Status:** Proposed — approved in discussion; ready for a build plan
- **Author:** Brainstormed with Claude
- **Supersedes:** the *presentation* model of `2026-07-05-local-traffic-attribution-design.md`
  and the shipped device-centric Local tab. The classification foundation (LAN detection,
  remote-address capture) from that spec is retained; only the attribution dimension changes.
- **Related area:** `NetworkMonitor/Services/Traffic/` (TrafficCollector, TrafficTracker,
  LanClassifier), the Internet/Local traffic UI, and the digest reports.

## 1. Background — what shipped, and why it changes

The Local tab shipped **device-centric**: LAN traffic aggregated by remote LAN endpoint
(`LocalTrafficRollup { MinuteEpoch, RemoteIp, … }`), the list showing LAN peers (NAS, etc.).
That deliberately sidestepped app attribution because SMB/NAS traffic is owned by the kernel
(System). See the 2026-07-05 spec §2/§7.

The tabs are now meant to be a symmetric, complementary set:

| Tab | Dimension | Scope |
|---|---|---|
| **Internet** | apps (this PC) | traffic to/from the **WAN** |
| **Local** | apps (this PC) | traffic to/from the **LAN** |
| **Devices** | devices | hosts seen on the LAN/Wi-Fi |

The problem being fixed: Local currently shows *devices*, duplicating the Devices tab's
dimension and breaking the "Internet=apps / Local=apps" symmetry. Local should show **which
of this PC's apps are talking on the LAN**, mirroring Internet.

## 2. The model (approved)

**Local = this PC's apps and how much each talked on the LAN**, with the **remote device as a
drill-down**, not the primary axis.

Hard reality carried from the old spec, accepted by decision: the ETW collector only sees
**this PC's** processes, and SMB/file-share + receive-side (pid-less) bytes are owned by the
kernel, so they attribute to **`System`**. Option **B** keeps the remote IP alongside the
process, so a `System` row still **drills down to the real device** ("System → SurfratNas
4.1 GB"). That is the identification win preserved from the device-centric design, now nested
under the app.

## 3. Decisions

1. **D1 — Internet is WAN-only; Local is LAN-only (complementary, no double-count).**
   Today the by-PID counter accumulates *all* of an app's traffic (WAN + LAN), so LAN bytes
   would appear in both tabs. The collector will now split at capture: LAN-classified bytes go
   **only** to the LAN counter, non-LAN bytes go **only** to the Internet counter. Result:
   `Internet(app) + Local(app) = app's total`. The digest's Internet section shifts to WAN-only
   to match.

2. **D2 — Add a raw per-flush table for sub-minute chart parity.** Local rollups are
   per-minute; the 5-minute range therefore collapses to a single bar (known bug). Internet
   avoids this by reading raw `TrafficEntries` at sub-minute ranges. Add a raw
   `LocalTrafficEntry` table, written each flush, read by the Local chart at sub-minute ranges —
   giving true Internet parity and fixing the empty 5m chart.

3. **D3 — Unattributable LAN bytes bucket under plain `System`.** SMB and pid-less receive
   bytes resolve to process name `System`. No special label; the drill-down carries the device.

## 4. Data model

Two tables replace the single device-keyed rollup. **Both are LAN-only.**

- **`LocalTrafficRollup`** — per-minute aggregate, **re-keyed**:
  `{ Id, MinuteEpoch, ProcessName, ProcessPath?, RemoteIp, BytesUploaded, BytesDownloaded }`,
  unique index `(MinuteEpoch, ProcessName, RemoteIp)`.
  - Group by `ProcessName` → **app rows** (primary grid).
  - Group by `RemoteIp` within a `ProcessName` → **per-app device drill-down**.
  - Group by `ProcessName` across all IPs, summed → chart series for a selected app.

- **`LocalTrafficEntry`** — raw per-flush row (new, per D2):
  `{ Id, Timestamp, ProcessName, ProcessPath?, RemoteIp, BytesUploaded, BytesDownloaded }`.
  Read only for sub-minute chart buckets; rollups still serve the grid and coarser ranges.

Cardinality stays small: (few LAN peers) × (few apps doing LAN I/O) per minute. `RemoteIp`
stays a **string** (IPv6-ready, IPv4-only in v1, per the old spec). Both tables purge under the
existing `TrafficPurgeDays` policy.

**`TrafficRollup` / `TrafficEntry` (Internet + digest) are unchanged in schema** but now hold
**WAN-only** bytes (see §6).

## 5. Collector & tracker

- **`TrafficCollector.AddBytes(pid, source, destination, bytes, upload)`**:
  - Classify the remote (via `LanClassifier.TryClassifyRemote`, retained from the parked work —
    it picks the non-local address as the peer, correct for both send and recv).
  - **LAN** → accumulate into `_localCounters` keyed by a `(pid, packedRemoteIp)` struct.
    Pid-less receives (`pid < 0`) are kept and keyed with a sentinel that resolves to `System`.
  - **Not LAN** → accumulate into the existing `_counters` keyed by `pid` (Internet). **LAN bytes
    no longer touch `_counters`** (D1).
- **`DrainAndResetLocal()`** returns `(pid, packedRemoteIp) → (up, down)`.
- **`TrafficTracker.FlushAsync`**: resolve each `pid → (ProcessName, ProcessPath)` via the
  existing `ResolveProcessInfo` (System/kernel and unresolvable pids → `ProcessName = "System"`,
  `ProcessPath = null`); format `packedRemoteIp → RemoteIp` string; write one raw
  `LocalTrafficEntry` per `(ProcessName, RemoteIp)` and upsert the per-minute
  `LocalTrafficRollup` on `(MinuteEpoch, ProcessName, RemoteIp)`. Carry the per-`(app, device)`
  deltas on the `Flushed` event for the live path.

## 6. Internet WAN-only (D1)

Because the collector no longer routes LAN bytes into `_counters`, `TrafficEntries` /
`TrafficRollups` become WAN-only with **no query change** — the existing Internet VM and digest
read the same tables, now holding only internet traffic. The existing "exclude System from
Internet" behaviour stays. Net user-visible effect: Internet totals drop by their former LAN
portion, and that portion now appears (per-app) on Local.

## 7. UI

Mirror `InternetPage` (area chart, range buttons 5m/1h/6h/24h/7d, Live/Paused/History badge,
pause-on-scroll, click-a-bar history, click-row-to-filter-chart), with the app dimension and a
device drill-down.

- **Primary grid** `x:DataType = LocalTrafficAppRow`: **App · Download · Upload · Total**,
  plus a **Peers** column — the peer device name, or "SurfratNas +2" when several (full list in a
  tooltip/flyout). Sorted by Total, "All Apps" summary row like Internet's "All Apps".
- **Row click** = Internet parity: filter the chart to that app **and** reveal the app's
  **per-device breakdown** (inline expander / detail list of `LocalTrafficDeviceRow`:
  Device · Download · Upload · Total for each LAN peer of that app).
- **Chart** filters by the selected app (all its LAN peers summed), sub-minute ranges read
  `LocalTrafficEntry`, coarser ranges read `LocalTrafficRollup` — same shape as Internet.

## 8. Reports / digest

- Digest "Top local **devices**" table becomes **Top local apps** — App · Peer(s) · Download ·
  Upload · Total — built from `LocalTrafficRollup` grouped by `ProcessName` (peer = the app's
  top `RemoteIp`, resolved to a device name). Local split chart = Download-vs-Upload over the top
  local **apps**.
- Internet digest section is unchanged in code but now reflects WAN-only totals (§6).
- CSV keeps the Raw + Friendly paired-column style, now app-keyed.

## 9. Migration / DB impact

Re-keyed `LocalTrafficRollup` + new `LocalTrafficEntry` = schema change under EF Core
`EnsureCreated` (no migrations) ⇒ **one-time local DB delete on upgrade**. Existing device-keyed
LAN history is discarded (acceptable; it is short-retention telemetry). State this in the
completion summary.

## 10. Non-goals

- Per-app attribution of SMB traffic (still `System`; §2) — unchanged from the old spec §7.
- Traffic between *other* devices (never traverses this PC).
- IPv6 LAN traffic (IPv4-only v1; `RemoteIp` string keeps the door open).
- Changing the Devices tab.

## 11. Testing

- `LanClassifier` classification/packing (retain existing tests).
- Collector/tracker: `(pid, remoteIp)` keying; pid-less → `System`; LAN bytes excluded from the
  Internet counter (D1); non-LAN bytes excluded from the LAN counter.
- Aggregation: rollups → app rows; per-app device drill-down; peer summary ("+N").
- Manual e2e: delete DB once; run a NAS/SMB copy → a `System` app row grows, drills down to the
  NAS device; a browser hitting a local web UI attributes to the browser; confirm the same LAN
  bytes are **absent** from Internet; confirm the 5m chart is populated (D2); confirm the digest
  Local section is app-keyed.

## 12. Carried assumptions / parked work

- The uncommitted parked work (`LanClassifier.TryClassifyRemote`, pid-less receive counting in
  `TrafficCollector`/`TrafficTracker`, and `LanClassifierTests`) folds into this redesign: keep
  `TryClassifyRemote` and the pid-less handling; re-key the LAN counter from `packedRemoteIp` to
  `(pid, packedRemoteIp)`.
- `Device.DisplayName` is `[NotMapped]` → materialise `Devices` before building the IP→name map.
- TraceEvent `args.saddr` / `args.daddr` remain the one external assumption to confirm at build.

# Local Traffic Redesign — Design Spec

**Date:** 2026-07-20
**Status:** Approved for planning
**Supersedes / builds on:** `2026-07-05-local-traffic-attribution-design.md`, `2026-07-16-local-traffic-app-centric.md` (the original per-app LAN attribution — still accurate; this redesign layers on top of it, it does not undo it).

---

## 1. Problem

The Local tab is technically correct but not useful. Every flow renders as an equal row, so a 900-byte mDNS discovery ping looks identical to a multi-gigabyte NAS backup. Two concrete complaints from the owner:

1. **Noise drowns signal.** Chrome (Cast/mDNS/SSDP discovery) and Kaspersky/`avp` (LAN device sweep) legitimately touch *every* device on the network. Verified real, not a bug — the kernel attributes each packet to the correct PID (see investigation notes below). But it fills the list with dozens of tiny, meaningless rows.
2. **The one thing worth watching is hard to find.** When Macrium backs up to the NAS, the owner wants to *see* it. Today it's buried, and it appears under **System**, not Macrium.

### Verified constraints (do not try to "fix" these — they are OS facts)

- **SMB traffic is attributed to System (PID 4), never the originating app.** Macrium hands the file to Windows; the kernel SMB redirector opens the socket. Resource Monitor, GlassWire, and every host-based tool show the same. See `[[project_smb_traffic_attributed_to_system]]`.
- **We only see traffic that touches *this* PC.** A phone→NAS transfer never reaches us and cannot be attributed to an app by any host tool. The owner has explicitly accepted this scope.
- **Attribution granularity is process *name*.** All `chrome.exe` processes merge into one "chrome". Correct and unchanged.

## 2. Goal

Reorganise the same data around **signal vs noise**, and add a **device lens** so a big transfer to the NAS is obvious regardless of which app (System) did it. Keep the app's exact visual language (dark WinUI, `#1976D2` download / `#AB47BC` upload, pills, area chart, DataGrid).

Mockup reference (approved): the three-frame artifact — *By app (de-noised)*, *By device*, and the *Macrium→NAS walkthrough*.

## 3. Approach (agreed decisions)

| # | Decision | Choice |
|---|----------|--------|
| 1 | Capture **port + protocol** per flow | **Yes.** ETW events already carry `sport`/`dport`; protocol is implied by the event. Enables factual classification instead of guessing. Requires a schema change → **one-time DB delete.** |
| 2 | How to classify "background/discovery" | **Port/protocol based** (known discovery services + multicast), not a volume heuristic. Accurate. |
| 3 | Show the folded group | **Collapsed, with a count** — never hidden. `"23 devices — discovery only"`. |
| 4 | Default lens | **By app** (least change from today). By device is one toggle click away. |
| 5 | `SMB · file share` tag | **Yes**, driven by TCP port 445/139. Only shown when we actually have the port. |

Non-negotiable data facts stored raw; **classification and labels are derived in C#** (one place: `LocalFlowClassifier`) so rules can change without re-collecting.

## 4. Data model changes

### 4.1 Capture (`TrafficCollector`)
- ETW `TcpIpSend/Recv` and `UdpIpSend/Recv` already expose `sport`/`dport`. The **remote service port** is:
  - Send (`upload`): `dport` (we send *to* `daddr:dport`).
  - Recv (`download`): `sport` (packet came *from* `saddr:sport`).
- **Protocol**: `6` (TCP) for TcpIp events, `17` (UDP) for UdpIp events.
- `AddBytes` gains `byte protocol, ushort remotePort` parameters.
- `LocalFlowKey` becomes `(int Pid, uint RemoteIp, byte Protocol, ushort RemotePort)`. Internet counters (`_counters` keyed by PID) are **unchanged**.

### 4.2 Storage
Add to **both** `LocalTrafficEntry` and `LocalTrafficRollup`:
- `int Protocol`  (6 / 17)
- `int RemotePort`

Rollup uniqueness changes:
```
(MinuteEpoch, ProcessName, RemoteIp, Protocol, RemotePort)   -- was (MinuteEpoch, ProcessName, RemoteIp)
```
Upsert SQL `ON CONFLICT` target and column list update accordingly.

**DB action: one-time delete required** (EnsureCreated, no migrations — house rule). No data preserved; local traffic history is disposable.

### 4.3 Classification (new, pure)
`NetworkMonitor.Services.Traffic.LocalFlowClassifier` (static):

```
FlowClassification Classify(int protocol, int remotePort)
```
returns `record struct FlowClassification(FlowCategory Category, string? ServiceTag)`
with `enum FlowCategory { Data, Discovery }`.

Rules (remote port):
- **Discovery** (fold to background): UDP 5353 mDNS, 5355 LLMNR, 1900 SSDP, 3702 WS-Discovery, 137 NetBIOS-NS, 138 NetBIOS-DGM, 67/68 DHCP, 5350/5351 NAT-PMP/PCP.
- **Data + ServiceTag**: TCP 445 → `"SMB"`, 139 → `"SMB"`, 2049 → `"NFS"`, 548 → `"AFP"`, 80/8080 → `"HTTP"`, 443/8443 → `"HTTPS"`, 22 → `"SSH"`, 3389 → `"RDP"`.
- Everything else → `Data`, `ServiceTag = null`.

Note: multicast/broadcast *destinations* (e.g. SSDP to `239.255.255.250`) are already outside the LAN unicast ranges and don't reach `_localCounters`; the device *responses* (from `device:1900` etc.) are what we classify — hence port-based rules on the responses catch them.

## 5. Presentation model

Replace the two bespoke row records with a generic two-level structure used by both lenses:

```
record LocalTrafficLeafRow(string RemoteKeyOrProcess, string DisplayName, string? SubLabel,
                           long BytesUploaded, long BytesDownloaded, string? ServiceTag)

record LocalTrafficGroupRow(string? Key, string DisplayName, string? SubLabel,
                            long BytesUploaded, long BytesDownloaded,
                            IReadOnlyList<LocalTrafficLeafRow> Children,
                            GroupKind Kind, string? ServiceTag)

enum GroupKind { All, Normal, Background }
```

- **By app:** group = app, children = devices. `SubLabel` = null. `ServiceTag` on a device child (e.g. SMB) and bubbled to the group if all/most traffic is that service.
- **By device:** group = device (DisplayName from `namesByIp`, `SubLabel` = IP), children = apps.
- **All** row: totals, no children.
- **Background** row: single collapsed group aggregating all `Discovery` flows, `DisplayName = "{n} devices — discovery only"`, expandable to per-device (or per-app) leaves. Always last.

The existing `LocalTrafficAppRow`/`LocalTrafficDeviceRow` are retired in favour of the generic rows (fewer types, both lenses share one DataGrid template).

## 6. View model

`LocalViewModel`:
- New `enum LocalLens { ByApp, ByDevice }` + `Lens` property (persisted to `Settings.LocalLens`); setting it reloads.
- `Groups` (`ObservableCollection<LocalTrafficGroupRow>`) replaces `Apps`.
- `AppOrDeviceHeader` / `ChildHeader` string properties for the two swappable column headers ("App"/"Device", "Peers"/"Apps").
- `BuildDataAsync` fetches classified minutes once, then builds groups per the active lens via a new `LocalTrafficGrouper` (replaces `LocalTrafficAggregator`).
- Live-flush path (`ApplyFlushToWindow` / `RebuildAppRows`) carries `Protocol`/`RemotePort` on `LocalTrafficDelta` and re-classifies, so the background fold stays correct live.

## 7. UI (`LocalPage.xaml` / `.xaml.cs`)

- Toolbar gains a segmented **By app / By device** toggle (two `ToggleButton`s or a `SelectorBar`), styled to match; wired to `Lens`.
- DataGrid rebinds to `Groups`; `App`/`Peers` headers bind to `AppOrDeviceHeader`/`ChildHeader`.
- Group rows show a `ServiceTag` chip (amber for SMB) when present; the **Background** group renders greyed with a `"discovery only"` chip and is expandable like any other.
- Existing click-to-collapse and bucket-selection behaviour preserved.

## 8. Reports / digest consistency

The digest Local section (`DigestGenerator.LoadLocalTrafficTotalsAsync` → app-keyed with peer device) must **exclude Discovery flows** so reports show real transfers only, consistent with the tab. Classification reuses `LocalFlowClassifier`. Background totals are not reported (or reported as a single "background" line — TBD in plan, default: excluded from top-apps, noted in headline).

## 9. Out of scope

- Whole-network / cross-device conversation mapping (impossible for a host tool).
- Renaming System→Macrium (impossible — SMB is kernel-owned).
- Per-tab / per-connection breakdown within an app.
- Live per-row Mb/s rate badge — **nice-to-have, deferred** to a final optional task; core ships without it.

## 10. Testing strategy

- **Pure unit tests** (xUnit, existing `NetworkMonitor.Tests`): `LocalFlowClassifier` (port→category/tag table), `LocalTrafficGrouper` (app vs device grouping, background fold, All-row totals, service-tag bubbling).
- Tests project **links prod sources via `<Compile Include>`** (no ProjectReference) — every new prod `.cs` used by a test MUST be added to `NetworkMonitor.Tests.csproj`. See `[[project_tests_csproj_links_sources]]`.
- Schema round-trip verified by running the app once after DB delete (manual).

## 11. Risks

| Risk | Mitigation |
|------|------------|
| Rollup row count grows (per proto+port) | Bounded by apps × devices × few services; acceptable. Purge unchanged. |
| Live-flush classification drift | Carry proto/port on the delta; re-classify in `RebuildAppRows`; fall back to full `LoadAsync` on any new group. |
| Port-based rules miss an oddball device | Rules live in one pure class; trivially extendable. Unknown ports default to visible `Data` (fail-safe: shows rather than hides). |
| DB delete forgotten | Plan's final task explicitly instructs the one-time delete + relaunch elevated. |

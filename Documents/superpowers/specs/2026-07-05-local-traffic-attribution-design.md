# Local Traffic Attribution — Design Spec

- **Date:** 2026-07-05
- **Status:** SUPERSEDED 2026-07-16 by `2026-07-16-local-traffic-app-centric-design.md` (Local pivoted to app-centric; Internet made WAN-only). The LAN-classification foundation here is retained; the device-centric *presentation* is replaced. Kept for history.
- **Author:** Brainstormed with Claude
- **Related area:** `NetworkMonitor/Services/Traffic/` (TrafficCollector, TrafficTracker), Traffic UI

## Purpose of this document

This is a **decision spec**, not a build spec. It captures a problem discovered while
testing the Traffic feature, explains the root cause, and lays out the options. It commits
to a *foundation* (capture the remote address and classify LAN-local traffic). The
sub-decisions that were originally left open were resolved on 2026-07-06 and are recorded
inline in section 8. Implementation should still flow through a follow-up build spec / plan
rather than being coded directly from this document.

## 1. Background — what was observed

While validating whether the app sees LAN-local traffic, a Macrium Reflect backup was run
from this PC to a NAS over an SMB share. The backup peaked at ~672 Mbps. The traffic **was**
captured, but it appeared in the Traffic view under the **System** process — not under
Macrium — and with no indication that it was LAN-local rather than internet traffic.

## 2. Root cause — a layer mismatch

The byte counts come from ETW's `TcpIpSend` / `TcpIpRecv` (and UDP equivalents) kernel
events, consumed in `TrafficCollector`. Two facts combine:

1. **Attribution is by `ProcessID` only.** `TrafficCollector.AddBytes` reads just
   `args.ProcessID` and `args.size` and discards everything else — including the remote
   address already carried on every event.
2. **SMB traffic runs in the kernel redirector.** Writes to an SMB/CIFS share are performed
   by the kernel SMB redirector (`mrxsmb`), not by the originating app's own sockets. ETW
   therefore stamps those `TcpIp*` events with `ProcessID = 4` (System). This is the same
   reason Task Manager and Resource Monitor show NAS copies under "System."

The information needed to say *which app* is already gone by the TCP layer, because SMB
multiplexes many apps' I/O over a small pool of shared TCP connections owned by System.

Consequence today: **LAN-local traffic is counted, but it is unlabeled** (indistinguishable
from internet traffic) and frequently **stuck under System**.

## 3. Goals

- **Primary goal — identification.** Answer the human question: *"what on my LAN is driving
  traffic, and to/from which device?"*
- **Deliverable concept — a Local Traffic tab** that isolates traffic whose remote endpoint
  is on the LAN, broken down **by endpoint (IP / known device)**, with up, down, current
  rate, and peak.

## 4. Non-goals

- **Exact per-app accounting of SMB traffic** (making the System bucket say "Macrium"). This
  is a separate, harder problem — see section 7. It is explicitly out of scope for the
  foundation and deferred to a later spec.
- **Monitoring traffic between *other* devices** (e.g. a second PC backing up to the NAS).
  That traffic never traverses this machine's stack and cannot be seen by this approach.
- Redesigning the existing (internet/all) Traffic view.

## 5. Definition of "local"

"Local" is defined by the **remote endpoint's IP address being on the LAN** (chosen over the
narrower "SMB-share traffic" definition). An endpoint is LAN-local when its address falls in:

- RFC1918 private ranges: `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`
- Link-local: `169.254.0.0/16` (IPv4), `fe80::/10` (IPv6)
- Unique local addresses: `fc00::/7` (IPv6)
- **The machine's actual active subnet(s)**, derived from `NetworkInterface.GetAllNetworkInterfaces()`
  unicast addresses + prefix lengths — the most precise signal, and it correctly excludes a
  private-range peer that happens to be on a different segment.

Loopback (`127.0.0.0/8`, `::1`) is ignored. Everything not classified as local is treated as
internet traffic and stays in the existing view.

## 6. Technical foundation (the enabling change)

1. **Capture the remote address.** `TcpIpSend` / `UdpIpSend` carry the destination
   (`daddr`); `TcpIpRecv` / `UdpIpRecv` carry the source (`saddr`). The remote endpoint is
   `daddr` on send and `saddr` on recv. `TrafficCollector.AddBytes` currently ignores both.
2. **Classify** each flow as LAN-local vs internet using the section 5 rules.
3. **Resolve endpoint → device name — the key synergy.** The app already discovers LAN
   devices by IP↔MAC from the ARP scan (`NetworkScanner` / device list). A LAN remote IP can
   therefore be resolved to a **named known device** — e.g. *"672 Mbps → Synology NAS
   (aa:bb:cc:…)"* — **even when the process is System**. This is the identification win and it
   sidesteps app-level attribution entirely.

**IPv4 / IPv6 note:** the current handlers (`TcpIpSend`, `TcpIpRecv`, `UdpIpSend`,
`UdpIpRecv`) are IPv4-only. The kernel provider emits separate `...IPV6` events. If IPv6 LAN
traffic matters, the IPv6 variants must be wired up in addition; otherwise IPv6 LAN traffic
is missed. To be decided in the build spec.

## 7. Deferred sub-problem — app-level attribution

Making SMB traffic report the originating app (Macrium) instead of System is a separate
problem with only approximate solutions, documented here so it is not re-litigated:

- **Handle-enumeration hint** — enumerate processes holding open handles to redirector
  devices (`\Device\Mup`, `\Device\LanmanRedirector`) via `NtQuerySystemInformation`
  (`SystemHandleInformation`) and annotate the System row with likely owners. Cheap; a hint,
  not accounting.
- **Kernel `FileIo` events** — carry the initiating PID + bytes, but file bytes ≠ network
  bytes (caching, read-ahead, write-behind, protocol overhead), so figures won't match TCP.
- **`Microsoft-Windows-SMBClient` ETW provider** — closer to the wire, but whether the
  initiating user-mode PID is reliably stamped on I/O events (redirector async worker
  threads) needs verification with a real capture before committing.

None of these is part of the foundation. If pursued, the endpoint-level tab can carry the
app as a *secondary, best-effort* column.

## 8. Resolved decisions (2026-07-06)

Each question below was resolved through discussion. These are the decisions the build spec
must implement.

1. **App attribution overlay → DEFERRED. v1 is pure endpoint attribution.**
   The identification win is the endpoint→device mapping, not app-level attribution. The
   `NtQuerySystemInformation` handle hint (section 7) is approximate, needs native interop,
   and adds cost for a "maybe Macrium" guess. Ship endpoint-only; revisit the app overlay
   only if the endpoint view proves insufficient.

2. **Data model → a separate LAN-only table keyed by `(MinuteEpoch, RemoteIp)`.**
   Do **not** add a remote-endpoint dimension to `TrafficEntry` / `TrafficRollup`: that would
   explode cardinality on the internet view (every remote IP a browser touches) and change
   the by-process key the existing tab depends on. Introduce a new
   `LocalTrafficRollup { MinuteEpoch, RemoteIp, BytesUploaded, BytesDownloaded }` table; only
   LAN-classified bytes land in it, and the existing tables are untouched. Process is kept
   out of the key (endpoint-only). No separate raw per-flush LAN table — per-minute rollups
   are sufficient for up / down / rate / peak, and live rate comes from the in-memory drain
   delta as it does today.

3. **Hot-path cost → keep the existing `pid` dictionary as-is; add a second, parallel
   LAN-only dictionary.**
   Do not re-key `TrafficCollector` by `(pid, remoteIp)`. Keep `pid → (up,down)` exactly as
   it is, and accumulate into a second dictionary **only when the remote IP classifies as
   LAN**. Internet packets are classified out with a few integer bitmask compares and never
   touch the second map, so its cardinality stays naturally bounded to the handful of LAN
   endpoints. Precompute the active subnet ranges as start/end `uint` pairs once on
   network-change so the callback is integer compares + one `Interlocked.Add`. Build-spec
   detail to verify: read the IPv4 address off the ETW event in the lowest-allocation way
   (avoid a per-packet `IPAddress.GetAddressBytes()` if a packed-int accessor exists).

4. **IP→device mapping freshness → resolve at display time; no staleness policy.**
   Store only the raw `RemoteIp`. When rendering the tab, resolve against the *current*
   known-device list (`Device.IpAddress` → `Device.DisplayName`); an unmatched IP shows as a
   bare IP. Historical rows then auto-acquire names as devices become known, and there is no
   threshold to tune. DHCP reassignment (an IP later belonging to a different device) is
   accepted for LAN identification and out of scope for v1.

5. **IPv6 → DEFERRED. v1 is IPv4-only, but the schema accommodates IPv6 with no later
   change.**
   SMB/NAS backups — the motivating case — are overwhelmingly IPv4 on SOHO LANs; wiring the
   `...IPV6` ETW variants plus 128-bit classification (`fe80::/10`, `fc00::/7`) doubles the
   surface for little near-term value. The in-memory collector uses a packed `uint` (IPv4);
   the DB `RemoteIp` column stores the **canonical string** form (converted on the cold flush
   path), so IPv6 rows fit later without a migration. Document explicitly in-app/logs that
   IPv6 LAN traffic is uncounted in v1.

6. **Retention / purge → follow the existing `TrafficPurgeDays` / 7-day trim.**
   Same class of data; no second retention knob. Wire `LocalTrafficRollup` into the current
   purge + trim routine.

## 9. Database impact

Per decision 8.2, this introduces exactly one new table — `LocalTrafficRollup`
(`MinuteEpoch`, `RemoteIp` string, `BytesUploaded`, `BytesDownloaded`) — and no changes to
the existing `TrafficEntry` / `TrafficRollup` schema. The project uses EF Core
`EnsureCreated` with **no migrations**, so adding the table requires a **one-time local DB
delete** on upgrade. (No DB delete is needed for this spec itself — it is documentation
only.)

## 10. Testing considerations

- Unit-test the LAN classifier against a table of addresses (RFC1918 boundaries, link-local,
  ULA, on-subnet vs off-subnet private, loopback, public).
- Unit-test endpoint→device resolution with a stub ARP/known-device source.
- Manual: run a NAS backup and confirm it appears on the Local Traffic tab attributed to the
  NAS device, with the byte total in the expected ballpark.

## 11. Recommendation

Build the **section 6 foundation** — remote-IP capture, LAN classification, and an
endpoint-keyed Local Traffic tab tied to the known-devices list — as the identification win.
Treat **app-level attribution (section 7)** as a separate, later spec, added only if the
endpoint view proves insufficient.

# Local Traffic Attribution — Design Spec

- **Date:** 2026-07-05
- **Status:** Decision spec (for discussion — not yet approved to build)
- **Author:** Brainstormed with Claude
- **Related area:** `NetworkMonitor/Services/Traffic/` (TrafficCollector, TrafficTracker), Traffic UI

## Purpose of this document

This is a **decision spec**, not a build spec. It captures a problem discovered while
testing the Traffic feature, explains the root cause, and lays out the options so a
direction can be chosen through discussion. It commits to a *foundation* (capture the
remote address and classify LAN-local traffic) and deliberately leaves the finer
sub-decisions open. Nothing should be implemented directly from this document until the
open questions in section 8 are resolved and a follow-up build spec / plan is written.

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

## 8. Open questions for discussion (deliberately unresolved)

1. **App attribution overlay** — do we want the handle-based hint at all in v1, or pure
   endpoint attribution first?
2. **Data model** — add a remote-endpoint dimension to `TrafficEntry` / `TrafficRollups`, or
   introduce a separate LAN-traffic aggregation keyed by endpoint? Impacts row cardinality
   and storage. A backup touching one NAS is low-cardinality; many endpoints is not.
3. **Hot-path cost** — keying the collector by `(pid, remoteIp)` instead of `pid` adds work
   to a very high-frequency ETW callback during a backup. The aggregation must stay lean
   (e.g. a concurrent dictionary keyed by a packed endpoint value).
4. **IP→device mapping freshness** — the ARP/known-device table can lag; how stale a mapping
   is acceptable before an endpoint is shown as a bare IP.
5. **IPv6** — in scope or explicitly deferred (see section 6 note).
6. **Retention / purge** — does LAN-endpoint data follow the existing `TrafficPurgeDays`
   policy, or its own?

## 9. Database impact

If built, this introduces a schema change (new column(s) or table for the endpoint
dimension). The project uses EF Core `EnsureCreated` with **no migrations**, so the change
would require a **one-time local DB delete** on upgrade. (No DB delete is needed for this
spec itself — it is documentation only.)

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

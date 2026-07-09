# mDNS Enrichment Design

**Date:** 2026-07-07
**Status:** Approved (design)

## 1. Goal & Scope

During a network scan, run one mDNS / DNS-SD discovery pass, correlate the responses to devices by IP address, and fill in a friendly **name** and a **model** string for any device that is missing them. The primary beneficiaries are **randomized-MAC devices** (the locally-administered `0x02` bit), where the OUI vendor lookup is useless and reverse-DNS usually fails, leaving the device shown as a bare IP.

**In scope:**

- One mDNS discovery pass per scan, inline with the existing ping/ARP/DNS pipeline.
- Enrich **all** devices, **fill-blanks only** — never overwrite a user-curated `FriendlyName`.
- Store a discovered friendly name (`MdnsName`) and a discovered `Model`.

**Out of scope (YAGNI):**

- Device-type inference from mDNS service types; no `Printer` enum member. The user chooses the device type manually.
- A settings toggle or configurable listen window (hardcoded constant for v1; can be added later if scan latency becomes a concern).
- A persistent passive-listener background service (evaluated as option C during brainstorming; inline-per-scan chosen instead).

## 2. Background — Current Naming Pipeline

- `NetworkScanner.ScanAsync` performs a ping sweep, reads the ARP table, and per responding IP resolves a reverse-DNS hostname and an OUI vendor, producing `ScannedDevice(Ip, Mac, Hostname, Vendor)`.
- `DeviceTracker.MergeAsync` merges scans into the DB keyed by normalized MAC.
- `Device.DisplayName => FriendlyName ?? Hostname ?? IpAddress`.
- Randomized MACs already get limited re-identification (`DeviceTracker.cs`): a new randomized-MAC device that shares a hostname with an approved device inherits its name/type. There is **no model field** today.

**Why randomized MACs are the pain point:** the OUI prefix is random (no real vendor), and reverse-DNS frequently returns nothing, so the device surfaces as a bare IP. mDNS is MAC-independent — Apple/Google/IoT devices advertise a friendly service instance name (`Kitchen HomePod._airplay._tcp.local`) and Apple devices publish `model=` in `_device-info._tcp` TXT records — so it can name and model these devices where the current pipeline cannot.

## 3. Data Model Changes (`Device.cs`)

One-time local DB delete required on upgrade (EF Core `EnsureCreated`, no migrations).

Two new nullable string columns, each a hand-written `SetProperty` property with its backing field directly above it (per coding conventions — no `[ObservableProperty]`):

- **`MdnsName`** (`string?`) — the mDNS service instance name (e.g. "Kitchen HomePod"). Its setter raises `OnPropertyChanged(nameof(DisplayName))`.
- **`Model`** (`string?`) — e.g. `MacBookPro18,3`.

Changes:

- `DisplayName => FriendlyName ?? MdnsName ?? Hostname ?? IpAddress`.
- `CopyValuesFrom` copies both new fields.
- No `AppDbContext` configuration needed — the columns auto-map on the existing `Devices` DbSet.

**UI surfacing:** `Model` is displayed as a read-only field in the device details / edit view. `MdnsName` needs no dedicated UI element because it flows into `DisplayName` everywhere. The exact view/placement is confirmed against the actual views during planning.

## 4. New Components (`Services/Scanning`)

Split into a pure, unit-tested parser and a thin I/O layer, mirroring the codebase's pure-aggregator + tests pattern.

- **`MdnsInfo`** — record `(string? Name, string? Model)`.
- **`MdnsResponseParser`** — pure. Takes the collected mDNS records and returns `IReadOnlyDictionary<string, MdnsInfo>` keyed by IP string. Correlation:
  - A records give `host → IP`.
  - PTR / SRV records give `service instance name → host`.
  - TXT records give the model via `model=` / `md=`, most reliably on `_device-info._tcp.local`.
  - Records that cannot be correlated to an IP produce no entry (no phantom rows).
  - **Name hardening (added during implementation, 2026-07-09):** the chosen friendly name has DNS presentation-format escapes decoded (`\032` → space, etc.) so names with spaces render correctly; and opaque instance labels — GUID-form names and known infra/pairing service types (`_remotepairing`, `_apple-mobdev`, `_sleep-proxy`, `_rdlink`) — are skipped so an opaque identifier never becomes `MdnsName` and outranks a good hostname.
- **`MdnsProbe`** — thin I/O layer over `Makaretu.Dns`. Sends the DNS-SD meta-query (`_services._dns-sd._udp.local`), collects `AnswerReceived` messages for a ~2s window, hands the accumulated records to `MdnsResponseParser`, and returns the IP → `MdnsInfo` map. **Fully guarded:** any failure (no multicast route, firewall block, timeout) returns an empty map. Signature: `Task<IReadOnlyDictionary<string, MdnsInfo>> DiscoverAsync(TimeSpan window, CancellationToken ct)`.

## 5. Scan Pipeline Changes

- `ScannedDevice` gains `MdnsName` and `Model` (record positional members).
- `NetworkScanner(OuiDatabase oui, MdnsProbe mdnsProbe)` — kicks off `mdnsProbe.DiscoverAsync(window, ct)` **in parallel** with the ping sweep, awaits both, then attaches each responding IP's `MdnsInfo` to its `ScannedDevice`. This overlaps the ~2s listen window with the ping/DNS work rather than adding it serially.
- `DeviceTracker.MergeAsync` — sets `device.MdnsName` / `device.Model` whenever the scan supplied a non-null value (authoritative auto-data; a device silent this round keeps its prior value — never nulled out). `FriendlyName` is never touched.

## 6. DI & Dependency

- Add the `Makaretu.Dns` NuGet reference to `NetworkMonitor.csproj`.
- Register `MdnsProbe` as a singleton in `App.xaml.cs`; inject it into `NetworkScanner`.

## 7. Error Handling

mDNS is strictly best-effort enrichment. The probe never throws into the scan; a failed or empty pass degrades gracefully to today's hostname + vendor behavior. No user-visible error surface for a failed probe.

## 8. Testing

- **`MdnsResponseParserTests`** — synthetic PTR / SRV / TXT / A record sets asserting:
  - instance name correlates to the correct IP,
  - model is extracted from `_device-info` TXT (`model=` / `md=`),
  - uncorrelated records produce no phantom entries.
- **`DeviceTracker` tests** — verify `MdnsName` / `Model` are filled from a scan, and that `FriendlyName` is never overwritten.

## 9. Decisions (from brainstorming)

1. **Which devices:** all devices, fill-blanks only (never overwrite a curated `FriendlyName`).
2. **How we speak mDNS:** `Makaretu.Dns` NuGet library (raw PTR/SRV/TXT/A records, handles name-compression and multi-NIC multicast).
3. **Data model:** dedicated `MdnsName` + `Model` columns (avoids reverse-DNS clobbering the mDNS name; clear provenance). One-time DB delete.
4. **Where/when:** inline in the scan, ~2s listen window overlapped with the ping sweep.
5. **Type inference:** excluded — the user chooses the device type manually.

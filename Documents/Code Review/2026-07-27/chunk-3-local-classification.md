# Chunk 3 — Local traffic classification & grouping

Reviewed 2026-07-27. Fix phase completed 2026-07-27 — see `progress.md`.

7 findings: **1 RISK · 6 CLEANUP**. This is the strongest part of the new work: the classifier and grouper are pure, allocation-sane, and genuinely unit-tested (`LanClassifierTests`, `LocalFlowClassifierTests`, `LocalTrafficGrouperTests`, `LocalTrafficNameResolverTests`). The one substantive finding is a mask-agnostic broadcast test that silently discards real traffic.

Verified correct while reading, worth recording as *checked*:

- `LocalFlowClassifier.DiscoverySqlPredicate` and `Classify` are driven by the same `DiscoveryPorts` array, so the SQL and in-memory definitions of "discovery" cannot drift (`LocalFlowClassifier.cs:10-19`).
- The chart SQL excludes discovery (`LocalViewModel.cs:693`) and the live delta path applies the same `FlowCategory.Data` filter (`LocalViewModel.cs:248`) — chart and live increments agree.
- `_ranges` / `_selfAddresses` are `volatile` fields swapped by reference in `Refresh`, so the ETW hot path never sees a half-built collection.

---

## C3-1 [RISK] Every address ending in `.255` is treated as broadcast — status: fixed

`NetworkMonitor.Core/Traffic/LanClassifier.cs:101-113`

```csharp
bool broadcast = (packed & 0x000000FFu) == 0x000000FFu;
```

The test is mask-agnostic: it assumes /24. Consequences, both at `TrafficCollector.cs:124` where the result **discards the packet entirely**:

- On any subnet wider than /24 — /23 and /22 are ordinary on business and campus networks — `x.y.z.255` is a perfectly valid **host** address. All traffic to and from that machine vanishes from the Local tab with no trace.
- The check runs before the LAN/WAN split, so it also applies to public addresses. Any internet host whose IPv4 ends in `.255` (a valid unicast address outside /24 land) is dropped from **Internet** totals too.

The classifier already has what it needs to do this properly: `Refresh()` computes `(start, end)` per adapter, and `end` *is* that subnet's broadcast address.

**Proposed fix:** keep the multicast test as-is (224/4 is correct); replace the last-octet test with a check against `255.255.255.255` plus the computed `end` of each known local range, stored alongside `_ranges`.

---

## C3-2 [CLEANUP] `Refresh()` is un-debounced and runs on the DI critical path — status: fixed

`NetworkMonitor.Core/Traffic/LanClassifier.cs:15-22,115-165,180-183`

Three small things in one place:

1. The constructor calls `Refresh()` synchronously, so `NetworkInterface.GetAllNetworkInterfaces()` (tens of milliseconds, more with VPN/virtual adapters) runs while the DI container resolves the singleton — which happens during `AppHost.StartAsync()` on the splash path.
2. `NetworkAddressChanged` fires in **bursts** (adapter up, DHCP lease, VPN connect, media disconnect can produce a handful within a second) and each callback rebuilds both collections from scratch on a threadpool thread.
3. The handler is never detached. Harmless for a process-lifetime singleton, but it's the pattern the 2026-06-23 review flagged repeatedly (C6-1).

**Proposed fix:** debounce the refresh (a short timer coalescing bursts) and detach the handler in a `Dispose`.

**Fixed as proposed — sub-point 1 deliberately left alone.** The 2-second debounce timer and the `Dispose` that detaches `NetworkAddressChanged` are both in (`LanClassifier.cs:11,25,131-135,255-260`). The constructor still calls `Refresh()` synchronously (`:27`), and that stays: deferring it would leave the first packets after startup classified against `FixedRanges` only, with self-address detection and subnet-broadcast detection both degraded until the first refresh landed. Confirmed with the user during the 2026-07-28 co-review — a few tens of milliseconds on the splash path is the cheaper side of that trade.

---

## C3-3 [CLEANUP] A group's service tag comes from an arbitrary child — status: fixed

`NetworkMonitor.Core/Traffic/LocalTrafficGrouper.cs:134-144`

```csharp
foreach (LeafAccumulator leaf in _children.Values)   // dictionary order
{
    …
    groupTag ??= row.ServiceTag;                     // first non-null wins
}

leaves.Sort(…);                                      // sorted only afterwards
```

`groupTag` is taken from whichever child the dictionary happens to enumerate first, and the sort by size happens after. A device that does 4 GB of SMB and 2 KB of HTTP can therefore be tagged **HTTP** in the grid — and the tag can change between refreshes as the dictionary's layout changes.

**Proposed fix:** sort the leaves first, then take the tag from the largest child (or the largest tagged child).

---

## C3-4 [CLEANUP] Double lookup on the discovery path — status: fixed

`NetworkMonitor.Core/Traffic/LocalTrafficGrouper.cs:19-22`

`namesByIp.ContainsKey(minute.RemoteIp)` immediately followed by `LocalTrafficNameResolver.Resolve(minute.RemoteIp, namesByIp)`, which does the same lookup again. On a 6-hour window this runs per flow-minute per refresh.

**Proposed fix:** one `TryGetValue`.

---

## C3-5 [CLEANUP] Magic rate threshold in a model type — status: fixed

`NetworkMonitor.Models/Traffic/LocalTrafficGroupRow.cs:10,157`

`RateThresholdBytesPerSec = 62_500.0` is 500 kbit/s expressed in decimal units, hard-coded into the row model with no comment, and it gates whether the live rate chip appears at all. It also silently assumes SI units while the app now supports both SI and binary modes (`RateUnitMode`).

**Proposed fix:** name it for what it is (`ShowRateAboveBitsPerSecond = 500_000`), derive the byte figure from it, and comment the WHY.

---

## C3-6 [CLEANUP] Discovery traffic from unknown peers is dropped without trace — status: fixed

`NetworkMonitor.Core/Traffic/LocalTrafficGrouper.cs:16-25`

If a discovery flow's remote IP isn't in the known-devices map, the whole flow-minute is discarded — not folded into the background row, not counted anywhere. This is deliberate (commit `7e7b370`, "Drop broadcast/multicast and non-device discovery peers from Local traffic") and it keeps the grid clean, but the consequence is that the Local tab's totals cannot be reconciled against the captured counters, and nothing in the UI says so.

**Proposed fix:** no behaviour change — document it in `Documents/Overview.md` (or the Local traffic doc) so the totals are explainable.

---

## C3-7 [CLEANUP] Design notes worth recording — status: fixed

- All of RFC1918 is treated as LAN (`BuildFixedRanges`), so traffic to a *remote* private network over a VPN is reported as **Local**, not Internet. Defensible, but it should be a written decision.
- CGNAT space (100.64.0.0/10, used by Tailscale and some ISPs) is neither LAN nor excluded, so a Tailscale peer counts as Internet traffic.
- `LanClassifier` is not `sealed`.

---

## Files reviewed

- `NetworkMonitor.Core/Traffic/LanClassifier.cs`
- `NetworkMonitor.Core/Traffic/LocalFlowClassifier.cs`
- `NetworkMonitor.Core/Traffic/FlowCategory.cs`, `FlowClassification.cs`
- `NetworkMonitor.Core/Traffic/LocalTrafficGrouper.cs`
- `NetworkMonitor.Core/Traffic/LocalTrafficNameResolver.cs`
- `NetworkMonitor.Core/Traffic/LocalFlowMinute.cs`, `LocalTrafficMinute.cs`, `LocalLens.cs`
- `NetworkMonitor.Core/Traffic/TrafficWindow.cs`
- `NetworkMonitor.Models/Traffic/LocalTrafficGroupRow.cs`, `LocalTrafficLeafRow.cs`, `GroupKind.cs`
- `NetworkMonitor.Tests/LanClassifierTests.cs`, `LocalFlowClassifierTests.cs`, `LocalTrafficGrouperTests.cs`, `LocalTrafficNameResolverTests.cs`

## User findings

Co-reviewed 2026-07-28. **No user findings.** The two classification decisions recorded in C3-7 were put to the user and both confirmed:

- All of RFC1918 counts as LAN, so a remote private network reached over a VPN reports as **Local** rather than Internet.
- CGNAT space (100.64.0.0/10) counts as Internet, so a Tailscale peer appears on the Internet tab.

The user also asked for the chunk's fixes to be verified against the code rather than the ledger. All seven are present; the one wording correction that produced is recorded under C3-2 above.

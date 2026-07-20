# Local Traffic Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reorganise the Local traffic tab around signal-vs-noise, add a By-device lens, and make a Macrium→NAS backup obvious (under System, tagged SMB).

**Architecture:** Capture protocol + remote port per LAN flow via existing ETW events; persist them; classify each flow (Data vs Discovery, plus a service tag) in one pure class; a pure grouper turns classified minutes into a generic two-level row model that feeds both an app lens and a device lens through the same DataGrid; discovery folds into one collapsed "background" group.

**Tech Stack:** WinUI 3 (.NET 10, x64), CommunityToolkit.Mvvm (hand-written `SetProperty`), CommunityToolkit DataGrid, EF Core 10 + SQLite (EnsureCreated, no migrations), TraceEvent (ETW kernel provider), xUnit.

## Global Constraints

- **Coding conventions (CLAUDE.md):** no `var`; no single-char names; always curly braces; blank line after opening `{` and before closing `}` of every method/block and around every block; single exit point (one `return` at end); returns stand alone (assign to local first); one type per file; class member order Fields → Ctor → Properties → Public → Override → Private; backing field directly above its property; hand-written `SetProperty` (no `[ObservableProperty]`); `string.Empty` not `""`; no comments unless WHY is non-obvious.
- **XAML conventions:** blank line after `<?xml?>`; one attribute per line indented 4 spaces; attribute order = simple assignments, then events/`Command`, then value bindings; blank line around every element; `DevicesPage.xaml` is the reference.
- **Platform:** build x64 (not Any CPU). Colours: download `#1976D2`, upload `#AB47BC`.
- **Tests project links prod sources** via `<Compile Include>` in `NetworkMonitor.Tests.csproj` — every NEW prod `.cs` referenced by a test must be added there, or the test won't compile.
- **No EF migrations.** Schema change means the user deletes the SQLite DB once (`%LOCALAPPDATA%\UmnathaNetworkMonitor\*.db`). State this at the end.
- **Git:** commit per task; do not push (owner runs the checkin skill). Subject lines end with a full stop.

---

### Task 1: Add Protocol + RemotePort to the traffic storage models

**Files:**
- Modify: `NetworkMonitor/Models/LocalTrafficEntry.cs`
- Modify: `NetworkMonitor/Models/LocalTrafficRollup.cs`
- Modify: `NetworkMonitor/Data/AppDbContext.cs:42-44` (rollup unique index)

**Interfaces:**
- Produces: `LocalTrafficEntry.Protocol` (int), `LocalTrafficEntry.RemotePort` (int); same on `LocalTrafficRollup`; rollup unique index `(MinuteEpoch, ProcessName, RemoteIp, Protocol, RemotePort)`.

- [ ] **Step 1: Add properties to `LocalTrafficEntry`** — after `RemoteIp`, before `BytesUploaded`, following the file's property style (braces on own lines):

```csharp
public int Protocol
{
    get;
    set;
}

public int RemotePort
{
    get;
    set;
}
```

- [ ] **Step 2: Add the same two properties to `LocalTrafficRollup`** (same placement, same style).

- [ ] **Step 3: Widen the rollup unique index** in `AppDbContext.cs`:

```csharp
modelBuilder.Entity<LocalTrafficRollup>()
    .HasIndex(rollup => new { rollup.MinuteEpoch, rollup.ProcessName, rollup.RemoteIp, rollup.Protocol, rollup.RemotePort })
    .IsUnique();
```

- [ ] **Step 4: Build** — `dotnet build NetworkMonitor/NetworkMonitor.csproj -c Debug -p:Platform=x64`. Expected: succeeds (copy-to-bin may fail if the app is running; compile must be clean).

- [ ] **Step 5: Commit** — `git add -A && git commit -m "Add Protocol and RemotePort to local traffic storage."`

---

### Task 2: `LocalFlowClassifier` (pure classification)

**Files:**
- Create: `NetworkMonitor/Services/Traffic/FlowCategory.cs`
- Create: `NetworkMonitor/Services/Traffic/FlowClassification.cs`
- Create: `NetworkMonitor/Services/Traffic/LocalFlowClassifier.cs`
- Test: `NetworkMonitor.Tests/LocalFlowClassifierTests.cs`
- Modify: `NetworkMonitor.Tests/NetworkMonitor.Tests.csproj` (link the 3 new prod files)

**Interfaces:**
- Produces: `enum FlowCategory { Data, Discovery }`; `readonly record struct FlowClassification(FlowCategory Category, string? ServiceTag)`; `static FlowClassification LocalFlowClassifier.Classify(int protocol, int remotePort)`.

- [ ] **Step 1: Write the failing test** `NetworkMonitor.Tests/LocalFlowClassifierTests.cs`:

```csharp
using NetworkMonitor.Services.Traffic;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class LocalFlowClassifierTests
    {
        [Theory]
        [InlineData(17, 5353)]
        [InlineData(17, 1900)]
        [InlineData(17, 5355)]
        [InlineData(17, 137)]
        [InlineData(17, 3702)]
        public void ClassifiesKnownDiscoveryPortsAsDiscovery(int protocol, int remotePort)
        {
            FlowClassification classification = LocalFlowClassifier.Classify(protocol, remotePort);

            Assert.Equal(FlowCategory.Discovery, classification.Category);
        }

        [Theory]
        [InlineData(6, 445, "SMB")]
        [InlineData(6, 139, "SMB")]
        [InlineData(6, 80, "HTTP")]
        [InlineData(6, 443, "HTTPS")]
        public void TagsKnownDataServices(int protocol, int remotePort, string expectedTag)
        {
            FlowClassification classification = LocalFlowClassifier.Classify(protocol, remotePort);

            Assert.Equal(FlowCategory.Data, classification.Category);
            Assert.Equal(expectedTag, classification.ServiceTag);
        }

        [Fact]
        public void TreatsUnknownPortAsUntaggedData()
        {
            FlowClassification classification = LocalFlowClassifier.Classify(6, 51413);

            Assert.Equal(FlowCategory.Data, classification.Category);
            Assert.Null(classification.ServiceTag);
        }
    }
}
```

- [ ] **Step 2: Create the three prod files.**

`FlowCategory.cs`:
```csharp
namespace NetworkMonitor.Services.Traffic
{
    public enum FlowCategory
    {
        Data,
        Discovery
    }
}
```

`FlowClassification.cs`:
```csharp
namespace NetworkMonitor.Services.Traffic
{
    public readonly record struct FlowClassification(FlowCategory Category, string? ServiceTag);
}
```

`LocalFlowClassifier.cs`:
```csharp
namespace NetworkMonitor.Services.Traffic
{
    public static class LocalFlowClassifier
    {
        private const int Tcp = 6;
        private const int Udp = 17;

        public static FlowClassification Classify(int protocol, int remotePort)
        {
            FlowClassification result;

            if (protocol == Udp && IsDiscoveryPort(remotePort))
            {
                result = new FlowClassification(FlowCategory.Discovery, null);
            }
            else
            {
                string? tag = ServiceTagFor(protocol, remotePort);
                result = new FlowClassification(FlowCategory.Data, tag);
            }

            return result;
        }

        private static bool IsDiscoveryPort(int remotePort)
        {
            bool discovery = remotePort switch
            {
                5353 => true,
                5355 => true,
                1900 => true,
                3702 => true,
                137 => true,
                138 => true,
                67 => true,
                68 => true,
                5350 => true,
                5351 => true,
                _ => false
            };

            return discovery;
        }

        private static string? ServiceTagFor(int protocol, int remotePort)
        {
            string? tag = null;

            if (protocol == Tcp)
            {
                tag = remotePort switch
                {
                    445 => "SMB",
                    139 => "SMB",
                    2049 => "NFS",
                    548 => "AFP",
                    80 => "HTTP",
                    8080 => "HTTP",
                    443 => "HTTPS",
                    8443 => "HTTPS",
                    22 => "SSH",
                    3389 => "RDP",
                    _ => null
                };
            }

            return tag;
        }
    }
}
```

- [ ] **Step 3: Link the new prod files in the test csproj.** Add to `NetworkMonitor.Tests.csproj` inside the existing `<ItemGroup>` of `<Compile Include>` entries:

```xml
<Compile Include="..\NetworkMonitor\Services\Traffic\FlowCategory.cs" Link="Services\Traffic\FlowCategory.cs" />
<Compile Include="..\NetworkMonitor\Services\Traffic\FlowClassification.cs" Link="Services\Traffic\FlowClassification.cs" />
<Compile Include="..\NetworkMonitor\Services\Traffic\LocalFlowClassifier.cs" Link="Services\Traffic\LocalFlowClassifier.cs" />
```

- [ ] **Step 4: Run tests** — `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj`. Expected: PASS (all `LocalFlowClassifierTests`).

- [ ] **Step 5: Commit** — `git add -A && git commit -m "Add LocalFlowClassifier for local flow categorisation."`

---

### Task 3: Capture protocol + remote port in `TrafficCollector`

**Files:**
- Modify: `NetworkMonitor/Services/Traffic/LocalFlowKey.cs`
- Modify: `NetworkMonitor/Services/Traffic/TrafficCollector.cs`

**Interfaces:**
- Consumes: none new.
- Produces: `LocalFlowKey(int Pid, uint RemoteIp, byte Protocol, ushort RemotePort)`; `DrainAndResetLocal()` return dictionary now keyed by the widened `LocalFlowKey`.

- [ ] **Step 1: Widen `LocalFlowKey`:**

```csharp
namespace NetworkMonitor.Services.Traffic
{
    public readonly record struct LocalFlowKey(int Pid, uint RemoteIp, byte Protocol, ushort RemotePort);
}
```

- [ ] **Step 2: Update the ETW subscriptions** in `TrafficCollector.ExecuteAsync` to pass protocol (6/17) and the remote service port (dport on send, sport on recv):

```csharp
_session.Source.Kernel.TcpIpSend += args => AddBytes(args.ProcessID, args.daddr, args.size, upload: true, protocol: 6, remotePort: (ushort)args.dport);
_session.Source.Kernel.TcpIpRecv += args => AddBytes(args.ProcessID, args.saddr, args.size, upload: false, protocol: 6, remotePort: (ushort)args.sport);
_session.Source.Kernel.UdpIpSend += args => AddBytes(args.ProcessID, args.daddr, args.size, upload: true, protocol: 17, remotePort: (ushort)args.dport);
_session.Source.Kernel.UdpIpRecv += args => AddBytes(args.ProcessID, args.saddr, args.size, upload: false, protocol: 17, remotePort: (ushort)args.sport);
```

- [ ] **Step 3: Update `AddBytes` signature and the local key construction:**

```csharp
private void AddBytes(int pid, IPAddress remote, int bytes, bool upload, byte protocol, ushort remotePort)
{

    if (bytes > 0)
    {

        if (!_lanClassifier.IsSelfOrLoopback(remote))
        {
            int slot = upload ? 0 : 1;

            if (_lanClassifier.TryClassifyLocal(remote, out uint packed))
            {
                int keyPid = pid < 0 ? SystemPid : pid;
                LocalFlowKey key = new LocalFlowKey(keyPid, packed, protocol, remotePort);
                long[] localCounter = _localCounters.GetOrAdd(key, static missingKey => new long[2]);

                Interlocked.Add(ref localCounter[slot], bytes);
            }
            else if (pid >= 0)
            {
                long[] counter = _counters.GetOrAdd(pid, static missingPid => new long[2]);

                Interlocked.Add(ref counter[slot], bytes);
            }

        }

    }

}
```

- [ ] **Step 4: Build** — `dotnet build NetworkMonitor/NetworkMonitor.csproj -c Debug -p:Platform=x64`. Expected: compile clean. (`DrainAndResetLocal` already returns `Dictionary<LocalFlowKey, ...>`; callers in Task 4 consume the new fields.)

- [ ] **Step 5: Commit** — `git add -A && git commit -m "Capture protocol and remote port for local flows."`

---

### Task 4: Persist protocol + port through `TrafficTracker`

**Files:**
- Modify: `NetworkMonitor/Services/Traffic/LocalTrafficDelta.cs`
- Modify: `NetworkMonitor/Services/Traffic/TrafficTracker.cs` (`FlushAsync`, `UpsertLocalRollupsAsync`)

**Interfaces:**
- Consumes: `LocalFlowKey.Protocol`, `LocalFlowKey.RemotePort`.
- Produces: `LocalTrafficDelta(string ProcessName, string? ProcessPath, string RemoteIp, int Protocol, int RemotePort, long BytesUploaded, long BytesDownloaded)`.

- [ ] **Step 1: Widen `LocalTrafficDelta`:**

```csharp
namespace NetworkMonitor.Services.Traffic
{
    public record LocalTrafficDelta(string ProcessName, string? ProcessPath, string RemoteIp, int Protocol, int RemotePort, long BytesUploaded, long BytesDownloaded);
}
```

- [ ] **Step 2: In `FlushAsync`**, carry protocol/port from the drained key into `LocalTrafficEntry` and `LocalTrafficDelta`:

```csharp
foreach (KeyValuePair<LocalFlowKey, (long Upload, long Download)> pair in localSnapshot)
{
    (string processName, string? processPath) = ResolveLocalProcess(pair.Key.Pid);
    string remoteIp = LanClassifier.Format(pair.Key.RemoteIp);
    int protocol = pair.Key.Protocol;
    int remotePort = pair.Key.RemotePort;

    localEntries.Add(new LocalTrafficEntry
    {
        Timestamp = timestamp,
        ProcessName = processName,
        ProcessPath = processPath,
        RemoteIp = remoteIp,
        Protocol = protocol,
        RemotePort = remotePort,
        BytesUploaded = pair.Value.Upload,
        BytesDownloaded = pair.Value.Download
    });

    localDeltas.Add(new LocalTrafficDelta(processName, processPath, remoteIp, protocol, remotePort, pair.Value.Upload, pair.Value.Download));
}
```

- [ ] **Step 3: Update `UpsertLocalRollupsAsync`** — add `$protocol`/`$port` params, widen INSERT columns and the `ON CONFLICT` target:

```sql
INSERT INTO LocalTrafficRollups (MinuteEpoch, ProcessName, ProcessPath, RemoteIp, Protocol, RemotePort, BytesUploaded, BytesDownloaded)
VALUES ($minute, $name, $path, $ip, $protocol, $port, $upload, $download)
ON CONFLICT(MinuteEpoch, ProcessName, RemoteIp, Protocol, RemotePort) DO UPDATE SET
    BytesUploaded = BytesUploaded + excluded.BytesUploaded,
    BytesDownloaded = BytesDownloaded + excluded.BytesDownloaded,
    ProcessPath = COALESCE(ProcessPath, excluded.ProcessPath)
```

Add the two parameters (mirror the existing `$ip` parameter block) and set them in the per-delta loop:
```csharp
protocolParameter.Value = delta.Protocol;
portParameter.Value = delta.RemotePort;
```

- [ ] **Step 4: Build** — expected compile clean.

- [ ] **Step 5: Commit** — `git add -A && git commit -m "Persist protocol and port in local entries and rollups."`

---

### Task 5: Generic presentation row models

**Files:**
- Create: `NetworkMonitor/Models/GroupKind.cs`
- Create: `NetworkMonitor/Models/LocalTrafficLeafRow.cs`
- Create: `NetworkMonitor/Models/LocalTrafficGroupRow.cs`
- Delete (after Task 7 rewires): `NetworkMonitor/Models/LocalTrafficAppRow.cs`, `NetworkMonitor/Models/LocalTrafficDeviceRow.cs` (retire in Task 7's commit, not here)

**Interfaces:**
- Produces: `enum GroupKind { All, Normal, Background }`; the two records below with `TotalBytes`, `DownloadText`, `UploadText`, `TotalText` (via `ByteSizeFormatter`), `HasChildren`, `IsAll`, `IsBackground`, `ChildSummary`, `ChildTooltip`.

- [ ] **Step 1: Create `GroupKind.cs`:**
```csharp
namespace NetworkMonitor.Models
{
    public enum GroupKind
    {
        All,
        Normal,
        Background
    }
}
```

- [ ] **Step 2: Create `LocalTrafficLeafRow.cs`:**
```csharp
using NetworkMonitor.Services.Common;

namespace NetworkMonitor.Models
{
    public record LocalTrafficLeafRow(string Key, string DisplayName, string? SubLabel, long BytesUploaded, long BytesDownloaded, string? ServiceTag)
    {
        public long TotalBytes => BytesUploaded + BytesDownloaded;

        public string DownloadText => ByteSizeFormatter.Format(BytesDownloaded);

        public string UploadText => ByteSizeFormatter.Format(BytesUploaded);

        public string TotalText => ByteSizeFormatter.Format(TotalBytes);
    }
}
```

- [ ] **Step 3: Create `LocalTrafficGroupRow.cs`:**
```csharp
using NetworkMonitor.Services.Common;

namespace NetworkMonitor.Models
{
    public record LocalTrafficGroupRow(string? Key, string DisplayName, string? SubLabel, long BytesUploaded, long BytesDownloaded, IReadOnlyList<LocalTrafficLeafRow> Children, GroupKind Kind, string? ServiceTag)
    {
        public long TotalBytes => BytesUploaded + BytesDownloaded;

        public bool IsAll => Kind == GroupKind.All;

        public bool IsBackground => Kind == GroupKind.Background;

        public bool HasChildren => Children.Count > 1;

        public string DownloadText => ByteSizeFormatter.Format(BytesDownloaded);

        public string UploadText => ByteSizeFormatter.Format(BytesUploaded);

        public string TotalText => ByteSizeFormatter.Format(TotalBytes);

        public string ChildSummary => Children.Count switch
        {
            0 => string.Empty,
            1 => Children[0].DisplayName,
            _ => $"{Children[0].DisplayName} +{Children.Count - 1}"
        };

        public string ChildTooltip => string.Join(", ", Children.Select(child => child.DisplayName));
    }
}
```

- [ ] **Step 4: Build** — expected clean (old rows still present and unused by these).

- [ ] **Step 5: Commit** — `git add -A && git commit -m "Add generic two-level local traffic row models."`

---

### Task 6: `LocalTrafficGrouper` (pure — classify, fold, both lenses)

**Files:**
- Create: `NetworkMonitor/Services/Traffic/LocalFlowMinute.cs` (classified input record — replaces the ad-hoc `LocalTrafficMinute` tuple with proto/port)
- Create: `NetworkMonitor/Services/Traffic/LocalTrafficGrouper.cs`
- Test: `NetworkMonitor.Tests/LocalTrafficGrouperTests.cs`
- Modify: `NetworkMonitor.Tests/NetworkMonitor.Tests.csproj`

**Interfaces:**
- Consumes: `LocalFlowClassifier.Classify`, `LocalTrafficNameResolver.Resolve`, `LocalTrafficGroupRow`, `LocalTrafficLeafRow`, `GroupKind`.
- Produces: `record LocalFlowMinute(string ProcessName, string RemoteIp, int Protocol, int RemotePort, long BytesUploaded, long BytesDownloaded)`; `enum LocalLens { ByApp, ByDevice }`; `static IReadOnlyList<LocalTrafficGroupRow> LocalTrafficGrouper.Build(IReadOnlyList<LocalFlowMinute> minutes, IReadOnlyDictionary<string,string> namesByIp, LocalLens lens)`.

The `Build` output invariant: index 0 is the `All` group (totals across foreground only); then `Normal` groups sorted by TotalBytes desc; then at most one `Background` group (all Discovery flows) last.

- [ ] **Step 1: Create `LocalFlowMinute.cs`:**
```csharp
namespace NetworkMonitor.Services.Traffic
{
    public record LocalFlowMinute(string ProcessName, string RemoteIp, int Protocol, int RemotePort, long BytesUploaded, long BytesDownloaded);
}
```

- [ ] **Step 2: Create `LocalLens.cs`:**
```csharp
namespace NetworkMonitor.Services.Traffic
{
    public enum LocalLens
    {
        ByApp,
        ByDevice
    }
}
```

- [ ] **Step 3: Write the failing test** `NetworkMonitor.Tests/LocalTrafficGrouperTests.cs`:
```csharp
using System.Collections.Generic;
using NetworkMonitor.Models;
using NetworkMonitor.Services.Traffic;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class LocalTrafficGrouperTests
    {
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            ["192.168.1.50"] = "Surfrat NAS",
            ["192.168.1.126"] = "Geyser IOT"
        };

        [Fact]
        public void ByApp_FoldsDiscoveryIntoBackgroundAndKeepsDataUpFront()
        {
            List<LocalFlowMinute> minutes = new List<LocalFlowMinute>
            {
                new LocalFlowMinute("System", "192.168.1.50", 6, 445, 10, 4000),
                new LocalFlowMinute("chrome", "192.168.1.126", 17, 5353, 0, 200)
            };

            IReadOnlyList<LocalTrafficGroupRow> groups = LocalTrafficGrouper.Build(minutes, Names, LocalLens.ByApp);

            Assert.Equal(GroupKind.All, groups[0].Kind);
            Assert.Equal(4010, groups[0].TotalBytes);
            Assert.Equal("System", groups[1].DisplayName);
            Assert.Equal("SMB", groups[1].ServiceTag);
            Assert.True(groups[^1].IsBackground);
            Assert.Equal(200, groups[^1].TotalBytes);
        }

        [Fact]
        public void ByDevice_GroupsOnRemoteIpWithFriendlyName()
        {
            List<LocalFlowMinute> minutes = new List<LocalFlowMinute>
            {
                new LocalFlowMinute("System", "192.168.1.50", 6, 445, 10, 4000)
            };

            IReadOnlyList<LocalTrafficGroupRow> groups = LocalTrafficGrouper.Build(minutes, Names, LocalLens.ByDevice);

            Assert.Equal("Surfrat NAS", groups[1].DisplayName);
            Assert.Equal("192.168.1.50", groups[1].SubLabel);
            Assert.Equal("System", groups[1].Children[0].DisplayName);
        }
    }
}
```

- [ ] **Step 4: Run test to verify it fails** — `dotnet test --filter LocalTrafficGrouperTests`. Expected: FAIL (grouper not defined).

- [ ] **Step 5: Create `LocalTrafficGrouper.cs`** implementing the invariant. Classify each minute; Discovery → background accumulator; Data → nested `group → child → (up,down)` plus per-child `ServiceTag` (first non-null wins) with the group tag bubbled when a single tag dominates. In `ByApp`, group key = ProcessName (child = RemoteIp→name, SubLabel = IP); in `ByDevice`, group key = RemoteIp (DisplayName = name, SubLabel = IP; child = ProcessName). Build `All` from summed foreground totals; append single `Background` group built the same way (its children by device in both lenses). Full method body — obey single-exit, braces, blank-line rules:

```csharp
using NetworkMonitor.Models;

namespace NetworkMonitor.Services.Traffic
{
    public static class LocalTrafficGrouper
    {
        public static IReadOnlyList<LocalTrafficGroupRow> Build(IReadOnlyList<LocalFlowMinute> minutes, IReadOnlyDictionary<string, string> namesByIp, LocalLens lens)
        {
            Dictionary<string, GroupAccumulator> foreground = new Dictionary<string, GroupAccumulator>();
            GroupAccumulator background = new GroupAccumulator("__background", "background", null);

            foreach (LocalFlowMinute minute in minutes)
            {
                FlowClassification classification = LocalFlowClassifier.Classify(minute.Protocol, minute.RemotePort);

                if (classification.Category == FlowCategory.Discovery)
                {
                    string deviceName = LocalTrafficNameResolver.Resolve(minute.RemoteIp, namesByIp);
                    background.Add(minute.RemoteIp, deviceName, minute.RemoteIp, minute.BytesUploaded, minute.BytesDownloaded, null);
                }
                else
                {
                    AddForeground(foreground, minute, classification.ServiceTag, namesByIp, lens);
                }

            }

            List<LocalTrafficGroupRow> groups = new List<LocalTrafficGroupRow>();
            long totalUpload = 0;
            long totalDownload = 0;
            List<LocalTrafficGroupRow> normals = new List<LocalTrafficGroupRow>();

            foreach (KeyValuePair<string, GroupAccumulator> entry in foreground)
            {
                LocalTrafficGroupRow row = entry.Value.ToRow(GroupKind.Normal);

                normals.Add(row);
                totalUpload += row.BytesUploaded;
                totalDownload += row.BytesDownloaded;
            }

            normals.Sort((left, right) => right.TotalBytes.CompareTo(left.TotalBytes));

            LocalTrafficGroupRow allRow = new LocalTrafficGroupRow(null, "All Apps", null, totalUpload, totalDownload, Array.Empty<LocalTrafficLeafRow>(), GroupKind.All, null);
            groups.Add(allRow);
            groups.AddRange(normals);

            if (background.HasAny)
            {
                groups.Add(background.ToBackgroundRow());
            }

            return groups;
        }

        private static void AddForeground(Dictionary<string, GroupAccumulator> foreground, LocalFlowMinute minute, string? serviceTag, IReadOnlyDictionary<string, string> namesByIp, LocalLens lens)
        {
            string groupKey;
            string groupName;
            string? groupSub;
            string childKey;
            string childName;
            string? childSub;

            if (lens == LocalLens.ByApp)
            {
                groupKey = minute.ProcessName;
                groupName = minute.ProcessName;
                groupSub = null;
                childKey = minute.RemoteIp;
                childName = LocalTrafficNameResolver.Resolve(minute.RemoteIp, namesByIp);
                childSub = minute.RemoteIp;
            }
            else
            {
                groupKey = minute.RemoteIp;
                groupName = LocalTrafficNameResolver.Resolve(minute.RemoteIp, namesByIp);
                groupSub = minute.RemoteIp;
                childKey = minute.ProcessName;
                childName = minute.ProcessName;
                childSub = null;
            }

            if (!foreground.TryGetValue(groupKey, out GroupAccumulator? accumulator))
            {
                accumulator = new GroupAccumulator(groupKey, groupName, groupSub);
                foreground[groupKey] = accumulator;
            }

            accumulator.Add(childKey, childName, childSub, minute.BytesUploaded, minute.BytesDownloaded, serviceTag);
        }

        private sealed class GroupAccumulator
        {
            private readonly string _key;
            private readonly string _name;
            private readonly string? _sub;
            private readonly Dictionary<string, LeafAccumulator> _children = new Dictionary<string, LeafAccumulator>();

            public GroupAccumulator(string key, string name, string? sub)
            {
                _key = key;
                _name = name;
                _sub = sub;
            }

            public bool HasAny => _children.Count > 0;

            public void Add(string childKey, string childName, string? childSub, long upload, long download, string? serviceTag)
            {

                if (!_children.TryGetValue(childKey, out LeafAccumulator? leaf))
                {
                    leaf = new LeafAccumulator(childKey, childName, childSub);
                    _children[childKey] = leaf;
                }

                leaf.Add(upload, download, serviceTag);
            }

            public LocalTrafficGroupRow ToRow(GroupKind kind)
            {
                List<LocalTrafficLeafRow> leaves = new List<LocalTrafficLeafRow>();
                long upload = 0;
                long download = 0;
                string? groupTag = null;

                foreach (LeafAccumulator leaf in _children.Values)
                {
                    LocalTrafficLeafRow row = leaf.ToRow();

                    leaves.Add(row);
                    upload += row.BytesUploaded;
                    download += row.BytesDownloaded;
                    groupTag ??= row.ServiceTag;
                }

                leaves.Sort((left, right) => right.TotalBytes.CompareTo(left.TotalBytes));

                LocalTrafficGroupRow result = new LocalTrafficGroupRow(_key, _name, _sub, upload, download, leaves, kind, groupTag);

                return result;
            }

            public LocalTrafficGroupRow ToBackgroundRow()
            {
                LocalTrafficGroupRow inner = ToRow(GroupKind.Background);
                string label = $"{inner.Children.Count} device{(inner.Children.Count == 1 ? string.Empty : "s")} — discovery only";
                LocalTrafficGroupRow result = inner with { DisplayName = label };

                return result;
            }
        }

        private sealed class LeafAccumulator
        {
            private readonly string _key;
            private readonly string _name;
            private readonly string? _sub;
            private long _upload;
            private long _download;
            private string? _tag;

            public LeafAccumulator(string key, string name, string? sub)
            {
                _key = key;
                _name = name;
                _sub = sub;
            }

            public void Add(long upload, long download, string? serviceTag)
            {
                _upload += upload;
                _download += download;
                _tag ??= serviceTag;
            }

            public LocalTrafficLeafRow ToRow()
            {
                LocalTrafficLeafRow result = new LocalTrafficLeafRow(_key, _name, _sub, _upload, _download, _tag);

                return result;
            }
        }
    }
}
```

- [ ] **Step 6: Link new prod files** in `NetworkMonitor.Tests.csproj`: `LocalFlowMinute.cs`, `LocalLens.cs`, `LocalTrafficGrouper.cs`, `GroupKind.cs`, `LocalTrafficLeafRow.cs`, `LocalTrafficGroupRow.cs` (and confirm `LocalTrafficNameResolver.cs` is already linked; add if not).

- [ ] **Step 7: Run tests** — `dotnet test --filter LocalTrafficGrouperTests`. Expected: PASS.

- [ ] **Step 8: Commit** — `git add -A && git commit -m "Add LocalTrafficGrouper with discovery fold and dual lenses."`

---

### Task 7: Rewire `LocalViewModel` to lenses + grouper

**Files:**
- Modify: `NetworkMonitor/ViewModels/LocalViewModel.cs`
- Modify: `NetworkMonitor/Data/Settings.cs` (add `LocalLens` persisted setting)
- Delete: `NetworkMonitor/Models/LocalTrafficAppRow.cs`, `NetworkMonitor/Models/LocalTrafficDeviceRow.cs`, `NetworkMonitor/Services/Traffic/LocalTrafficAggregator.cs` (+ their `<Compile Include>` lines in the test csproj if present)

**Interfaces:**
- Consumes: `LocalTrafficGrouper.Build`, `LocalFlowMinute`, `LocalLens`, `LocalTrafficGroupRow`.
- Produces: `LocalViewModel.Groups` (`ObservableCollection<LocalTrafficGroupRow>`), `LocalViewModel.Lens` (`LocalLens`), `LocalViewModel.GroupHeader` / `LocalViewModel.ChildHeader` (string).

- [ ] **Step 1: Add `LocalLens LocalLens` to `Settings`** (default `LocalLens.ByApp`), persisted like the other settings; add a matching `Save()`-backed property. (Store as string or int in `settings.json`.)

- [ ] **Step 2: Replace `Apps` with `Groups`** and add `Lens`, `GroupHeader`, `ChildHeader` backing-field properties (hand-written `SetProperty`, backing field directly above). `Lens` setter persists to `_settings.LocalLens`, updates the two header strings (`ByApp` → "App"/"Peers", `ByDevice` → "Device"/"Apps"), and calls `_ = LoadAsync(true)`.

- [ ] **Step 3: Change the SQL in `LoadAppRowsAsync`** to also select and group by `Protocol, RemotePort`, and return `LocalFlowMinute`:
```sql
SELECT ProcessName, RemoteIp, Protocol, RemotePort,
       SUM(BytesUploaded)   AS Upload,
       SUM(BytesDownloaded) AS Download
FROM {sourceTable}
WHERE {whereClause}
GROUP BY ProcessName, RemoteIp, Protocol, RemotePort
```
Read the two extra columns and build `List<LocalFlowMinute>`, then `LocalTrafficGrouper.Build(minutes, namesByIp, Lens)`.

- [ ] **Step 4: Update the chart query filter.** `LoadChartBucketsAsync` should only count foreground (non-discovery) bytes so the chart matches the de-noised list. Two options; use the simpler SQL-side exclusion of known discovery UDP ports:
```sql
AND NOT (Protocol = 17 AND RemotePort IN (5353,5355,1900,3702,137,138,67,68,5350,5351))
```
Add this to the existing `WHERE`.

- [ ] **Step 5: Update the live-flush path** (`SeedWindowState`, `ApplyFlushToWindow`, `RebuildAppRows`) to key window state on the group/child appropriate to `Lens` and to classify each `LocalTrafficDelta` (now carrying Protocol/RemotePort) via `LocalFlowClassifier`, folding Discovery into a background accumulator. Simplest robust approach: store the raw window minutes as `List<LocalFlowMinute>` and rebuild groups via `LocalTrafficGrouper.Build` on each flush (cheap for a single window). Replace the bespoke `_windowAppPeerTotals` dictionary with a `List<LocalFlowMinute> _windowMinutes` accumulator; `RebuildAppRows` becomes `Groups = new(...Grouper.Build(_windowMinutes, _namesByIp, Lens))`.

- [ ] **Step 6: Delete** `LocalTrafficAppRow.cs`, `LocalTrafficDeviceRow.cs`, `LocalTrafficAggregator.cs` and remove any now-dangling `<Compile Include>` lines.

- [ ] **Step 7: Build** — expected compile clean once XAML (Task 8) still references old names? No — XAML changes in Task 8. To keep Task 7 independently buildable, do Task 7 + Task 8 as a paired change if the XAML won't compile against `Groups` yet. **Note for executor:** Tasks 7 and 8 share a compile boundary (the XAML binds to the VM); commit them together if the build can't pass in between.

- [ ] **Step 8: Commit** — `git add -A && git commit -m "Rewire LocalViewModel to lenses and the grouper."`

---

### Task 8: `LocalPage` — toggle, generic grid, chips, background row

**Files:**
- Modify: `NetworkMonitor/Views/LocalPage.xaml`
- Modify: `NetworkMonitor/Views/LocalPage.xaml.cs`

**Interfaces:**
- Consumes: `ViewModel.Groups`, `ViewModel.Lens`, `ViewModel.GroupHeader`, `ViewModel.ChildHeader`.

- [ ] **Step 1: Add the lens toggle** to the Row 0 toolbar (left of the range pills), two `ToggleButton`s ("By app" / "By device") or a `SelectorBar`; handler sets `ViewModel.Lens`. Follow XAML conventions (one attribute per line, event handlers before bindings).

- [ ] **Step 2: Rebind the DataGrid** `ItemsSource="{x:Bind ViewModel.Groups, Mode=OneWay}"`; change the App column header to `{x:Bind ViewModel.GroupHeader, Mode=OneWay}` and the Peers column header to `{x:Bind ViewModel.ChildHeader, Mode=OneWay}`; update cell `x:DataType` to `models:LocalTrafficGroupRow` and bind `DisplayName`, `SubLabel`, `ChildSummary`/`ChildTooltip`, `DownloadText`, `UploadText`, `TotalText`, `IsAll`, `IsBackground`.

- [ ] **Step 3: Add the ServiceTag chip** (amber for "SMB") in the group-name cell template, visible when `ServiceTag` is non-null (use a `StringToVisibilityConverter` or a bool wrapper property). Grey the Background row's text and show a `"discovery only"` chip.

- [ ] **Step 4: Update the RowDetails template** to bind `x:DataType="models:LocalTrafficLeafRow"` with `DisplayName`, `SubLabel`, `DownloadText`, `UploadText`, `TotalText`, and its own `ServiceTag` chip.

- [ ] **Step 5: Update `LocalPage.xaml.cs`** — the click-to-collapse handler (`AppGridTapped`/`FindTappedAppRow`) now works against `LocalTrafficGroupRow` (rename to match); `IsAllApps` → `IsAll`; selection logic binds to `ViewModel.Lens` grouping. Keep the existing `AddHandler(TappedEvent, ...)` collapse behaviour.

- [ ] **Step 6: Build + run** — build x64; launch the app (owner: exit tray, rebuild, relaunch elevated). Verify both lenses render and toggle.

- [ ] **Step 7: Commit** — `git add -A && git commit -m "Redesign Local page with app/device lenses and background fold."`

---

### Task 9: Keep the digest consistent (exclude discovery)

**Files:**
- Modify: `NetworkMonitor/Services/Digest/DigestGenerator.cs` (`LoadLocalTrafficTotalsAsync`)

**Interfaces:**
- Consumes: the same discovery-port exclusion used by the chart query.

- [ ] **Step 1: Add the discovery exclusion** to the digest Local rollup query so digest Local top-apps/totals count foreground only:
```sql
AND NOT (Protocol = 17 AND RemotePort IN (5353,5355,1900,3702,137,138,67,68,5350,5351))
```
(If `LoadLocalTrafficTotalsAsync` aggregates in SQL, add to its WHERE; if in C#, filter via `LocalFlowClassifier`.)

- [ ] **Step 2: Build + run the existing digest tests** — `dotnet test`. Expected: PASS (adjust any digest test that asserted discovery-inclusive local totals; update the assertion to foreground-only and note why).

- [ ] **Step 3: Commit** — `git add -A && git commit -m "Exclude discovery flows from the digest Local section."`

---

### Task 10 (optional, deferred): Live rate badge

**Files:**
- Modify: `NetworkMonitor/Models/LocalTrafficGroupRow.cs` (add `RateText`), `LocalViewModel` (compute last-bucket rate), `LocalPage.xaml` (chip).

- [ ] Compute per-group bytes in the most recent bucket ÷ interval seconds → `"148 Mb/s"`; show an `● active` chip on groups with rate > 0. Ship the core (Tasks 1–9) first; only build this if the owner still wants it after seeing the live result.

---

## Final: one-time DB delete + verification (owner)

- [ ] The schema changed (Protocol/RemotePort columns + new unique index). EnsureCreated does not migrate, so the owner must **delete the SQLite DB once**: exit via tray → delete `%LOCALAPPDATA%\UmnathaNetworkMonitor\*.db` → rebuild Debug x64 → relaunch **elevated** (ETW needs admin).
- [ ] Manual e2e: run a Macrium backup to the NAS; confirm on **By device** the NAS climbs with a large upload under **System** tagged **SMB**; confirm chrome/avp discovery collapses into the greyed **background** group; toggle lenses; check the digest Local section excludes discovery.

---

---

## Tasks 7 & 8 — FIRMED-UP CONCRETE DESIGN (2026-07-20)

> This section supersedes the outline versions of Task 7 and Task 8 above. It is the authoritative spec for the view-model + UI integration. **Correction discovered during firming:** `LocalTrafficAppRow`, `LocalTrafficDeviceRow`, `LocalTrafficMinute`, and `LocalTrafficAggregator` are ALSO used by the digest (`DigestSummaryBuilder`, `DigestGenerator`, `DigestSummaryBuilderTests`). Therefore Task 7 **DELETES NOTHING** — the redesign is purely additive to the view-model. No files removed, no test-csproj link removals.

### Task 7 (firmed): Rewire `LocalViewModel` to lenses + grouper

**Files:**
- Modify: `NetworkMonitor/Models/LocalTrafficGroupRow.cs`, `NetworkMonitor/Models/LocalTrafficLeafRow.cs` (add two bool helpers each)
- Modify: `NetworkMonitor/Data/Settings.cs` (add `LocalLens`)
- Modify: `NetworkMonitor/ViewModels/LocalLoadResult.cs`
- Modify: `NetworkMonitor/ViewModels/LocalViewModel.cs`
- **Delete nothing.** `LocalTrafficAppRow/DeviceRow/Minute/Aggregator` stay (digest owns them now).

**Interfaces produced:** `LocalViewModel.Groups` (`ObservableCollection<LocalTrafficGroupRow>`), `LocalViewModel.Lens` (`LocalLens`), `LocalViewModel.GroupHeader`/`ChildHeader` (string), `LocalViewModel.SelectedGroupKey` (string?). `LocalTrafficGroupRow.HasServiceTag`/`HasSubLabel`, `LocalTrafficLeafRow.HasServiceTag`/`HasSubLabel`.

**7.1 — Row model helpers** (avoids needing a string→visibility converter in XAML). Add to `LocalTrafficGroupRow` (and the two byte-based ones already exist):
```csharp
public bool HasServiceTag => ServiceTag is not null;

public bool HasSubLabel => SubLabel is not null;
```
Add the identical two expression-bodied properties to `LocalTrafficLeafRow`.

**7.2 — `Settings.cs`**: add `using NetworkMonitor.Services.Traffic;` and, alongside `LocalTimeRangeHours`:
```csharp
public LocalLens LocalLens
{
    get;
    set;
} = LocalLens.ByApp;
```

**7.3 — `LocalLoadResult.cs`** — change the display-row type and add the raw minutes (this record is VM-internal only):
```csharp
using System.Collections.Generic;
using NetworkMonitor.Models;
using NetworkMonitor.Services.Traffic;

namespace NetworkMonitor.ViewModels
{
    internal record LocalLoadResult(
        List<ChartPoint> ChartPoints,
        List<LocalTrafficGroupRow> Groups,
        List<LocalFlowMinute> Minutes,
        string StatusText,
        long CutoffEpoch,
        long BucketSeconds);
}
```

**7.4 — `LocalViewModel.cs`** — the concrete changes:

Fields: replace `_windowAppPeerTotals` with a flow accumulator; keep `_namesByIp`:
```csharp
private Dictionary<(string ProcessName, string RemoteIp, int Protocol, int RemotePort), (long Upload, long Download)> _windowFlows = new();
```

Constructor — seed lens + headers from settings:
```csharp
public LocalViewModel(IDbContextFactory<AppDbContext> dbFactory, Settings settings)
{
    _dbFactory = dbFactory;
    _settings = settings;
    _timeRangeHours = settings.LocalTimeRangeHours;
    _lens = settings.LocalLens;
    _groupHeader = _lens == LocalLens.ByApp ? "App" : "Device";
    _childHeader = _lens == LocalLens.ByApp ? "Peers" : "Apps";
}
```

Properties — replace `Apps` with `Groups`; rename `SelectedApp` → `SelectedGroupKey`; add `Lens`, `GroupHeader`, `ChildHeader` (all hand-written `SetProperty`, backing field directly above):
```csharp
private ObservableCollection<LocalTrafficGroupRow> _groups = [];

public ObservableCollection<LocalTrafficGroupRow> Groups
{
    get => _groups;
    set => SetProperty(ref _groups, value);
}

private LocalLens _lens;

public LocalLens Lens
{
    get => _lens;
    set
    {

        if (SetProperty(ref _lens, value))
        {
            _settings.LocalLens = value;
            _settings.Save();
            GroupHeader = value == LocalLens.ByApp ? "App" : "Device";
            ChildHeader = value == LocalLens.ByApp ? "Peers" : "Apps";
            SelectedGroupKey = null;
            _ = LoadAsync(true);
        }

    }
}

private string _groupHeader = "App";

public string GroupHeader
{
    get => _groupHeader;
    set => SetProperty(ref _groupHeader, value);
}

private string _childHeader = "Peers";

public string ChildHeader
{
    get => _childHeader;
    set => SetProperty(ref _childHeader, value);
}

private string? _selectedGroupKey;

public string? SelectedGroupKey
{
    get => _selectedGroupKey;
    set => SetProperty(ref _selectedGroupKey, value);
}
```
(Remove the old `_selectedApp`/`SelectedApp` and `_apps`/`Apps` members.)

`BuildDataAsync` — rename the `selectedApp` param to `selectedGroupKey`; replace the app-row/aggregator section with grouper output and carry the raw minutes:
```csharp
List<LocalFlowMinute> minutes = await LoadFlowMinutesAsync(db, useRollup, cutoff, cutoffEpoch, bucketRangeStart, bucketRangeEnd);
IReadOnlyList<LocalTrafficGroupRow> groups = LocalTrafficGrouper.Build(minutes, namesByIp, Lens);

Dictionary<int, (long Upload, long Download)> dataByBucket =
    await LoadChartBucketsAsync(db, useRollup, cutoff, cutoffEpoch, bucketSeconds, Lens, selectedGroupKey);
```
...then build `chartPoints` exactly as before, and compute status/result:
```csharp
int normalCount = 0;

foreach (LocalTrafficGroupRow row in groups)
{

    if (row.Kind == GroupKind.Normal)
    {
        normalCount++;
    }

}

long totalBytes = groups.Count > 0 ? groups[0].TotalBytes : 0;
string unit = Lens == LocalLens.ByApp ? "app" : "device";
string scopeText = selectedBucketStart.HasValue
    ? $"at {selectedBucketStart.Value.ToLocalTime():dd MMM HH:mm:ss}"
    : "total";
string statusText = $"{normalCount} {unit}{(normalCount == 1 ? string.Empty : "s")} · {ByteSizeFormatter.Format(totalBytes)} {scopeText}";

LocalLoadResult result = new LocalLoadResult(chartPoints, groups.ToList(), minutes, statusText, cutoffEpoch, bucketSeconds);

return result;
```

`LoadAppRowsAsync` → rename to `LoadFlowMinutesAsync`, drop the `namesByIp` param and the aggregator call, widen the SQL, return `List<LocalFlowMinute>`:
- SQL: `SELECT ProcessName, RemoteIp, Protocol, RemotePort, SUM(BytesUploaded) AS Upload, SUM(BytesDownloaded) AS Download FROM {sourceTable} WHERE {whereClause} GROUP BY ProcessName, RemoteIp, Protocol, RemotePort`
- Reader: `new LocalFlowMinute(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt64(4), reader.GetInt64(5))`
- Return the `List<LocalFlowMinute>` (no `LocalTrafficAggregator`, no `LocalTrafficMinute`).

`LoadChartBucketsAsync` — add `LocalLens lens, string? selectedGroupKey` params (drop `selectedApp`); exclude discovery and filter by the lens-appropriate column:
```csharp
string selectionColumn = lens == LocalLens.ByApp ? "ProcessName" : "RemoteIp";
```
WHERE becomes (keep the existing cutoff clause, then):
```
AND NOT (Protocol = 17 AND RemotePort IN (5353,5355,1900,3702,137,138,67,68,5350,5351))
AND ($key IS NULL OR {selectionColumn} = $key)
```
Rename the `$app` parameter to `$key` and set it from `selectedGroupKey`.

Live-flush — replace `SeedWindowState`, `ApplyFlushToWindow`, `RebuildAppRows`:
```csharp
private void SeedWindowState(LocalLoadResult result)
{
    _windowCutoffEpoch = result.CutoffEpoch;
    _windowBucketSeconds = result.BucketSeconds;
    _windowChartPoints = new List<ChartPoint>(result.ChartPoints);
    _windowFlows = new Dictionary<(string ProcessName, string RemoteIp, int Protocol, int RemotePort), (long Upload, long Download)>();

    foreach (LocalFlowMinute minute in result.Minutes)
    {
        (string ProcessName, string RemoteIp, int Protocol, int RemotePort) key = (minute.ProcessName, minute.RemoteIp, minute.Protocol, minute.RemotePort);
        _windowFlows.TryGetValue(key, out (long Upload, long Download) current);
        _windowFlows[key] = (current.Upload + minute.BytesUploaded, current.Download + minute.BytesDownloaded);
    }

}
```
`ApplyFlushToWindow` becomes synchronous (no more new-app full-reload fallback — the grouper rebuild handles new groups); update the caller `ApplyLiveFlushAsync` to call it without `await` (change `else { await ApplyFlushToWindow(deltas); }` to `else { ApplyFlushToWindow(deltas); }`):
```csharp
private void ApplyFlushToWindow(IReadOnlyList<LocalTrafficDelta> deltas)
{
    string? selectedKey = SelectedGroupKey;
    LocalLens lens = Lens;
    long chartDeltaUpload = 0;
    long chartDeltaDownload = 0;

    foreach (LocalTrafficDelta delta in deltas)
    {
        FlowClassification classification = LocalFlowClassifier.Classify(delta.Protocol, delta.RemotePort);

        if (classification.Category == FlowCategory.Data)
        {
            string groupKey = lens == LocalLens.ByApp ? delta.ProcessName : delta.RemoteIp;

            if (selectedKey is null || groupKey == selectedKey)
            {
                chartDeltaUpload += delta.BytesUploaded;
                chartDeltaDownload += delta.BytesDownloaded;
            }

        }

        (string ProcessName, string RemoteIp, int Protocol, int RemotePort) key = (delta.ProcessName, delta.RemoteIp, delta.Protocol, delta.RemotePort);
        _windowFlows.TryGetValue(key, out (long Upload, long Download) current);
        _windowFlows[key] = (current.Upload + delta.BytesUploaded, current.Download + delta.BytesDownloaded);
    }

    int lastIndex = _windowChartPoints.Count - 1;
    ChartPoint last = _windowChartPoints[lastIndex];
    _windowChartPoints[lastIndex] = last with
    {
        BytesUploaded = last.BytesUploaded + chartDeltaUpload,
        BytesDownloaded = last.BytesDownloaded + chartDeltaDownload
    };

    ChartPoints = new ObservableCollection<ChartPoint>(_windowChartPoints);

    RebuildGroups();
}

private void RebuildGroups()
{
    List<LocalFlowMinute> minutes = new List<LocalFlowMinute>(_windowFlows.Count);

    foreach (KeyValuePair<(string ProcessName, string RemoteIp, int Protocol, int RemotePort), (long Upload, long Download)> flow in _windowFlows)
    {
        minutes.Add(new LocalFlowMinute(flow.Key.ProcessName, flow.Key.RemoteIp, flow.Key.Protocol, flow.Key.RemotePort, flow.Value.Upload, flow.Value.Download));
    }

    IReadOnlyList<LocalTrafficGroupRow> groups = LocalTrafficGrouper.Build(minutes, _namesByIp, Lens);
    int normalCount = 0;

    foreach (LocalTrafficGroupRow row in groups)
    {

        if (row.Kind == GroupKind.Normal)
        {
            normalCount++;
        }

    }

    long totalBytes = groups.Count > 0 ? groups[0].TotalBytes : 0;
    string unit = Lens == LocalLens.ByApp ? "app" : "device";
    string statusText = $"{normalCount} {unit}{(normalCount == 1 ? string.Empty : "s")} · {ByteSizeFormatter.Format(totalBytes)} total";

    Groups = new ObservableCollection<LocalTrafficGroupRow>(groups);
    StatusText = statusText;
}
```
Also update `ApplyLiveFlushAsync`'s early guard `if (_windowChartPoints.Count == 0)` (unchanged) — it still calls `LoadAsync()` on first flush.

**7.5 — Build only** (this task alone won't compile against the old XAML which still binds `Apps`/`SelectedApp`). Per the compile-boundary note, DO Task 8 in the SAME working session and make ONE combined commit `Redesign Local page with app/device lenses and background fold.` after both compile.

### Task 8 (firmed): `LocalPage.xaml` + `.xaml.cs`

**8.1 — Toolbar lens toggle** (`LocalPage.xaml`, Row 0). Put two Buttons before the status text in column 0 (mirror the range-button pattern; selected state applied in code-behind):
```xml
<Button
    x:Name="LensAppButton"
    Content="By app"
    Tag="ByApp"
    Padding="10,5"
    Click="LensButtonClick" />

<Button
    x:Name="LensDeviceButton"
    Content="By device"
    Tag="ByDevice"
    Padding="10,5"
    Click="LensButtonClick" />
```

**8.2 — DataGrid** (`LocalPage.xaml`):
- `ItemsSource="{x:Bind ViewModel.Groups, Mode=OneWay}"`.
- Group column `x:Name="GroupColumn"` (header set in code-behind), cell `x:DataType="models:LocalTrafficGroupRow"`: `DisplayName` (FontWeight bound to `IsAll` via existing `BoolToFontWeightConverter`); a second muted `TextBlock` bound to `SubLabel` with `Visibility="{x:Bind HasSubLabel, Converter={StaticResource BoolToVisibilityConverter}}"`; a service-tag chip `Border` (amber) visible on `HasServiceTag`; a "discovery only" chip visible on `IsBackground`.
- Child column `x:Name="ChildColumn"`: bind `ChildSummary` / tooltip `ChildTooltip` (was `PeerSummary`/`PeerTooltip`).
- Download/Upload/Total cells: `DownloadText` (`#1976D2`), `UploadText` (`#AB47BC`), `TotalText`; Total FontWeight bound to `IsAll`.
- RowDetails: `x:DataType="models:LocalTrafficLeafRow"`, visibility bound to `HasChildren` (was `HasMultiplePeers`); bind `DisplayName`, `SubLabel` (its own `HasSubLabel`), `DownloadText`, `UploadText`, `TotalText`, and a service-tag chip on `HasServiceTag`.

**8.3 — `LocalPage.xaml.cs`** renames/handlers:
- Everywhere: `ViewModel.SelectedApp` → `ViewModel.SelectedGroupKey`; `ViewModel.Apps` → `ViewModel.Groups`; `LocalTrafficAppRow` → `LocalTrafficGroupRow`; `.IsAllApps` → `.IsAll`.
- `SyncGridSelection`: match on `row.Key == ViewModel.SelectedGroupKey` (was `row.ProcessName == ...SelectedApp`); the "all" target is `ViewModel.Groups.FirstOrDefault(row => row.IsAll)`.
- `AppGridSelectionChanged`: `string? newKey = row.IsAll ? null : row.Key;` compare/assign against `ViewModel.SelectedGroupKey`.
- `UpdateChartLabel` line ~368: replace `ViewModel.SelectedApp ?? "All Apps"` with `(AppGrid.SelectedItem as LocalTrafficGroupRow)?.DisplayName ?? "All Apps"`.
- `AppGridTapped`/`FindTappedAppRow`: retype to `LocalTrafficGroupRow`, `IsAllApps`→`IsAll`.
- Add `LensButtonClick` (sets `ViewModel.Lens` from the button `Tag` via `Enum.Parse<LocalLens>`; then `UpdateLensButtonStyles(button)` and `UpdateColumnHeaders()`), `UpdateLensButtonStyles(Button active)` (mirror `UpdateRangeButtonStyles`, AccentButtonStyle on the active one, over `[LensAppButton, LensDeviceButton]`), and `UpdateColumnHeaders()` (`GroupColumn.Header = ViewModel.GroupHeader; ChildColumn.Header = ViewModel.ChildHeader;`).
- In `OnNavigatedTo`, after `UpdateRangeButtonStyles(...)`, also call `UpdateLensButtonStyles(ViewModel.Lens == LocalLens.ByApp ? LensAppButton : LensDeviceButton)` and `UpdateColumnHeaders()`.

**8.4 — Build the WinUI project** x64. MSB3027/MSB3021 exe-copy-lock = success (app running); only `error CS...`/XAML compile errors are real failures. Then ONE combined commit for Tasks 7+8.

---

## Self-Review

- **Spec coverage:** capture (T3/T4) · classification (T2) · storage+schema (T1) · generic rows (T5) · grouper/lenses/fold (T6) · VM (T7) · UI toggle/chips/background (T8) · digest consistency (T9) · rate badge deferred (T10) · DB delete (Final). All spec §4–§10 mapped.
- **Placeholder scan:** pure-logic tasks (T2, T5, T6) carry complete code + tests. Integration tasks (T3, T4, T7, T8, T9) give exact SQL/XAML/signatures; executor reads the surrounding method (paths + line refs provided).
- **Type consistency:** `LocalFlowMinute`, `LocalFlowClassifier.Classify`, `LocalTrafficGrouper.Build(minutes, namesByIp, lens)`, `LocalTrafficGroupRow`/`LocalTrafficLeafRow` names/signatures identical across T5–T8. `LocalFlowKey(Pid, RemoteIp, Protocol, RemotePort)` and `LocalTrafficDelta(..., Protocol, RemotePort, ...)` consistent across T3/T4/T7.
- **Known coupling:** Tasks 7 & 8 share a compile boundary — commit together if the interim build can't pass (noted in T7 Step 7).

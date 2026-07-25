# mDNS Enrichment Implementation Plan

> **STATUS: COMPLETE (2026-07-09).** All 7 tasks implemented; 114/114 tests pass; Release x64 build clean. Live-verified end-to-end (see **Completion & Verification** at the bottom). Two bugs surfaced during live verification and fixed on top of the base plan (opaque-name filtering, DNS-escape decoding) plus a listen-window bump 2s → 4s. **One-time local DB delete required on upgrade** (two new `Device` columns via EnsureCreated). Not yet pushed at time of writing — awaiting commit approval.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** During each network scan, run one mDNS / DNS-SD discovery pass and fill in a friendly name and model for devices that lack them — chiefly randomized-MAC devices where OUI and reverse-DNS give nothing.

**Architecture:** A pure `MdnsResponseParser` correlates flattened mDNS records (A/PTR/SRV/TXT) to an IP → name/model map. A thin `MdnsProbe` (over the `Makaretu.Dns` library) runs the multicast query, collects answers for a short window, and hands them to the parser. `NetworkScanner` fires the probe in parallel with the ping sweep and attaches results to each `ScannedDevice`; `DeviceTracker` applies them via a pure `MdnsEnrichment` helper. Two new `Device` columns (`MdnsName`, `Model`) store the results.

**Tech Stack:** .NET 10, WinUI 3, EF Core 10 + SQLite (EnsureCreated, no migrations), CommunityToolkit.Mvvm, `Makaretu.Dns` (mDNS), xUnit.

## Global Constraints

Copied from the design spec (`Documents/superpowers/specs/2026-07-07-mdns-enrichment-design.md`) and `CLAUDE.md`:

- **Enrich all devices, fill-blanks only.** Never overwrite a user-curated `FriendlyName`. `MdnsName`/`Model` are authoritative auto-data: set them whenever the scan supplies a non-empty value; never null them out when a device is silent that round.
- **No device-type inference**; no `Printer` enum member. The user picks the device type manually.
- **Dedicated columns:** `MdnsName` and `Model` on `Device`. `DisplayName` becomes `FriendlyName ?? MdnsName ?? Hostname ?? IpAddress`.
- **Best-effort:** the probe never throws into the scan; a failed/empty pass degrades to today's hostname + vendor behaviour.
- **Inline per scan**, ~2s listen window, overlapped with the ping sweep. Hardcoded window constant for v1 (no settings toggle).
- **DB impact:** adding the two columns requires a **one-time local DB delete** on upgrade (EnsureCreated, no migrations). State this in the completion summary.
- **Coding conventions (CLAUDE.md):** no `var`; no single-character names; always curly braces; `string.Empty` not `""`; single exit point (one `return` at the end, value assigned to a local first, with a blank line above it); blank lines around every block and at method boundaries; class member order Fields → Constructor → Properties → Public → Override → Private; backing field directly above its hand-written `SetProperty` property (no `[ObservableProperty]`); property `{`/`get;`/`set;` each on their own line; no underscores except leading `_` on private fields.
- **XAML conventions:** `DevicesPage.xaml` / `AllDevicesPage.xaml` are the canonical reference — blank line after `<?xml?>`, one attribute per line indented 4 spaces, simple assignments → event handlers/Command → value bindings, blank line around every element.
- **slnx:** source `.cs` files are auto-included by the SDK and must NOT be added to `NetworkMonitor.slnx`. Only new root/Documents files (this plan) are added.

---

## File Structure

**New files:**

| File | Responsibility |
|---|---|
| `NetworkMonitor/Services/Scanning/MdnsInfo.cs` | Immutable `(string? Name, string? Model)` result for one endpoint. |
| `NetworkMonitor/Services/Scanning/MdnsResponseParser.cs` | Pure: flattened A/PTR/SRV/TXT records → `IReadOnlyDictionary<string, MdnsInfo>` keyed by IP. Defines the neutral record structs. |
| `NetworkMonitor/Services/Scanning/MdnsEnrichment.cs` | Pure: apply an `MdnsInfo` to a `Device` (fill-blanks semantics for the auto fields). |
| `NetworkMonitor/Services/Scanning/MdnsProbe.cs` | I/O over `Makaretu.Dns`: multicast query, collect answers for a window, flatten, parse. |
| `NetworkMonitor.Tests/MdnsResponseParserTests.cs` | Unit tests for correlation + model extraction. |
| `NetworkMonitor.Tests/MdnsEnrichmentTests.cs` | Unit tests for the fill-blanks helper. |

**Modified files:**

| File | Change |
|---|---|
| `NetworkMonitor/Models/Device.cs` | Add `MdnsName` + `Model` properties; update `DisplayName`; extend `CopyValuesFrom`. |
| `NetworkMonitor.Tests/DeviceTests.cs` | Add `DisplayName` precedence tests for `MdnsName`. |
| `NetworkMonitor/NetworkMonitor.csproj` | Add the `Makaretu.Dns` package reference. |
| `NetworkMonitor/Services/Scanning/NetworkScanner.cs` | Inject `MdnsProbe`; run discovery in parallel; extend `ScannedDevice`; attach `MdnsInfo`. |
| `NetworkMonitor/Services/Scanning/DeviceTracker.cs` | Call `MdnsEnrichment.Apply` in the merge loop. |
| `NetworkMonitor/App.xaml.cs` | Register `MdnsProbe` singleton. |
| `NetworkMonitor/Views/AllDevicesPage.xaml` | Add a read-only `Model` column after `Vendor`. |
| `NetworkMonitor.slnx` | Add this plan file. |

---

## Task 1: Device model — `MdnsName` + `Model` columns

**Files:**
- Modify: `NetworkMonitor/Models/Device.cs`
- Test: `NetworkMonitor.Tests/DeviceTests.cs`

**Interfaces:**
- Produces: `Device.MdnsName` (`string?`), `Device.Model` (`string?`), updated `Device.DisplayName` precedence `FriendlyName ?? MdnsName ?? Hostname ?? IpAddress`.
- Consumes: nothing.

- [ ] **Step 1: Write the failing tests**

Add to `NetworkMonitor.Tests/DeviceTests.cs`:

```csharp
        [Fact]
        public void DisplayNamePrefersFriendlyNameOverMdnsName()
        {
            Device device = new()
            {
                FriendlyName = "Test Laptop",
                MdnsName = "Kitchen HomePod",
                Hostname = "laptop.local",
                IpAddress = "192.168.1.50"
            };

            string displayName = device.DisplayName;

            Assert.Equal("Test Laptop", displayName);
        }

        [Fact]
        public void DisplayNameUsesMdnsNameWhenNoFriendlyName()
        {
            Device device = new()
            {
                FriendlyName = null,
                MdnsName = "Kitchen HomePod",
                Hostname = "laptop.local",
                IpAddress = "192.168.1.50"
            };

            string displayName = device.DisplayName;

            Assert.Equal("Kitchen HomePod", displayName);
        }

        [Fact]
        public void DisplayNameFallsBackToHostnameWhenNoFriendlyOrMdnsName()
        {
            Device device = new()
            {
                FriendlyName = null,
                MdnsName = null,
                Hostname = "laptop.local",
                IpAddress = "192.168.1.50"
            };

            string displayName = device.DisplayName;

            Assert.Equal("laptop.local", displayName);
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test NetworkMonitor.Tests --filter FullyQualifiedName~DeviceTests`
Expected: FAIL — `Device` has no `MdnsName` / `Model` property (does not compile).

- [ ] **Step 3: Add the two properties**

In `Device.cs`, add both properties in the Properties section. Place `MdnsName` immediately after the `FriendlyName` property block and `Model` immediately after `Vendor`. Each raises the relevant change notification.

After the `FriendlyName` property (ends at the `}` on the line before `private string? _vendor;`):

```csharp
        private string? _mdnsName;

        public string? MdnsName
        {
            get => _mdnsName;
            set
            {

                if (SetProperty(ref _mdnsName, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                }

            }
        }
```

After the `Vendor` property block:

```csharp
        private string? _model;

        public string? Model
        {
            get => _model;
            set => SetProperty(ref _model, value);
        }
```

- [ ] **Step 4: Update `DisplayName` and `CopyValuesFrom`**

Change the `DisplayName` expression:

```csharp
        [NotMapped]
        public string DisplayName => FriendlyName ?? MdnsName ?? Hostname ?? IpAddress;
```

In `CopyValuesFrom`, after the `Hostname = other.Hostname;` line add:

```csharp
            MdnsName = other.MdnsName;
```

and after `Vendor = other.Vendor;` add:

```csharp
            Model = other.Model;
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test NetworkMonitor.Tests --filter FullyQualifiedName~DeviceTests`
Expected: PASS (all `DeviceTests`, including the three new ones).

- [ ] **Step 6: Commit**

```bash
git add NetworkMonitor/Models/Device.cs NetworkMonitor.Tests/DeviceTests.cs
git commit -m "Add MdnsName and Model to Device with DisplayName precedence."
```

---

## Task 2: `MdnsInfo` + `MdnsEnrichment` (pure) fill helper

**Files:**
- Create: `NetworkMonitor/Services/Scanning/MdnsInfo.cs`
- Create: `NetworkMonitor/Services/Scanning/MdnsEnrichment.cs`
- Test: `NetworkMonitor.Tests/MdnsEnrichmentTests.cs`

**Interfaces:**
- Produces:
  - `MdnsInfo(string? Name, string? Model)` record.
  - `static void MdnsEnrichment.Apply(Device device, MdnsInfo? info)` — sets `device.MdnsName`/`device.Model` from non-empty values in `info`; never touches `FriendlyName`; does nothing when `info` is null or its fields are empty.
- Consumes: `Device` (Task 1).

- [ ] **Step 1: Create `MdnsInfo`**

`NetworkMonitor/Services/Scanning/MdnsInfo.cs`:

```csharp
namespace NetworkMonitor.Services.Scanning
{
    public record MdnsInfo(string? Name, string? Model);
}
```

- [ ] **Step 2: Write the failing tests**

`NetworkMonitor.Tests/MdnsEnrichmentTests.cs`:

```csharp
using NetworkMonitor.Models;
using NetworkMonitor.Services.Scanning;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class MdnsEnrichmentTests
    {
        [Fact]
        public void FillsNameAndModelOnEmptyDevice()
        {
            Device device = new();

            MdnsEnrichment.Apply(device, new MdnsInfo("Kitchen HomePod", "AudioAccessory5,1"));

            Assert.Equal("Kitchen HomePod", device.MdnsName);
            Assert.Equal("AudioAccessory5,1", device.Model);
        }

        [Fact]
        public void RefreshesStaleMdnsName()
        {
            Device device = new()
            {
                MdnsName = "Old Name"
            };

            MdnsEnrichment.Apply(device, new MdnsInfo("New Name", null));

            Assert.Equal("New Name", device.MdnsName);
        }

        [Fact]
        public void NullInfoLeavesDeviceUnchanged()
        {
            Device device = new()
            {
                MdnsName = "Existing",
                Model = "ExistingModel"
            };

            MdnsEnrichment.Apply(device, null);

            Assert.Equal("Existing", device.MdnsName);
            Assert.Equal("ExistingModel", device.Model);
        }

        [Fact]
        public void EmptyValuesDoNotClobberAndFriendlyNameUntouched()
        {
            Device device = new()
            {
                FriendlyName = "Curated",
                MdnsName = "Existing"
            };

            MdnsEnrichment.Apply(device, new MdnsInfo(string.Empty, null));

            Assert.Equal("Existing", device.MdnsName);
            Assert.Equal("Curated", device.FriendlyName);
        }
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test NetworkMonitor.Tests --filter FullyQualifiedName~MdnsEnrichmentTests`
Expected: FAIL — `MdnsEnrichment` does not exist.

- [ ] **Step 4: Create `MdnsEnrichment`**

`NetworkMonitor/Services/Scanning/MdnsEnrichment.cs`:

```csharp
using NetworkMonitor.Models;

namespace NetworkMonitor.Services.Scanning
{
    public static class MdnsEnrichment
    {
        public static void Apply(Device device, MdnsInfo? info)
        {

            if (info is not null)
            {

                if (!string.IsNullOrWhiteSpace(info.Name))
                {
                    device.MdnsName = info.Name;
                }

                if (!string.IsNullOrWhiteSpace(info.Model))
                {
                    device.Model = info.Model;
                }

            }

        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test NetworkMonitor.Tests --filter FullyQualifiedName~MdnsEnrichmentTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add NetworkMonitor/Services/Scanning/MdnsInfo.cs NetworkMonitor/Services/Scanning/MdnsEnrichment.cs NetworkMonitor.Tests/MdnsEnrichmentTests.cs
git commit -m "Add MdnsInfo and MdnsEnrichment fill-blanks helper."
```

---

## Task 3: `MdnsResponseParser` (pure) + neutral record structs

**Files:**
- Create: `NetworkMonitor/Services/Scanning/MdnsResponseParser.cs`
- Test: `NetworkMonitor.Tests/MdnsResponseParserTests.cs`

**Interfaces:**
- Produces:
  - `readonly record struct MdnsAddressRecord(string Host, string Ip)`
  - `readonly record struct MdnsPointerRecord(string Service, string Instance)`
  - `readonly record struct MdnsServiceRecord(string Instance, string TargetHost)`
  - `readonly record struct MdnsTextRecord(string Name, IReadOnlyList<string> Entries)`
  - `static IReadOnlyDictionary<string, MdnsInfo> MdnsResponseParser.Parse(IReadOnlyList<MdnsAddressRecord>, IReadOnlyList<MdnsPointerRecord>, IReadOnlyList<MdnsServiceRecord>, IReadOnlyList<MdnsTextRecord>)`
- Consumes: `MdnsInfo` (Task 2).

**Correlation model:** A records give `host → ip`. SRV records give `instance → target host`. A PTR record's friendly label is the instance name minus its trailing `.<service>` suffix; that instance resolves to an IP via SRV → host → A. A TXT record carrying a model key (`model` / `md` / `rpmd`) resolves to an IP the same way (its `Name` equals the service instance). Records that cannot be chained all the way to an IP contribute nothing (no phantom entries). Un-correlatable device-info-only TXT records are silently skipped in v1 (model is best-effort per the spec).

- [ ] **Step 1: Write the failing tests**

`NetworkMonitor.Tests/MdnsResponseParserTests.cs`:

```csharp
using System.Collections.Generic;
using NetworkMonitor.Services.Scanning;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class MdnsResponseParserTests
    {
        [Fact]
        public void CorrelatesInstanceNameToIp()
        {
            List<MdnsAddressRecord> addresses = new()
            {
                new MdnsAddressRecord("appletv.local", "192.168.1.20")
            };
            List<MdnsPointerRecord> pointers = new()
            {
                new MdnsPointerRecord("_airplay._tcp.local", "Living Room._airplay._tcp.local")
            };
            List<MdnsServiceRecord> services = new()
            {
                new MdnsServiceRecord("Living Room._airplay._tcp.local", "appletv.local")
            };
            List<MdnsTextRecord> texts = new();

            IReadOnlyDictionary<string, MdnsInfo> result = MdnsResponseParser.Parse(addresses, pointers, services, texts);

            Assert.True(result.ContainsKey("192.168.1.20"));
            Assert.Equal("Living Room", result["192.168.1.20"].Name);
        }

        [Fact]
        public void ExtractsModelFromTextRecord()
        {
            List<MdnsAddressRecord> addresses = new()
            {
                new MdnsAddressRecord("appletv.local", "192.168.1.20")
            };
            List<MdnsPointerRecord> pointers = new();
            List<MdnsServiceRecord> services = new()
            {
                new MdnsServiceRecord("Living Room._airplay._tcp.local", "appletv.local")
            };
            List<MdnsTextRecord> texts = new()
            {
                new MdnsTextRecord("Living Room._airplay._tcp.local", new List<string> { "model=AppleTV5,3" })
            };

            IReadOnlyDictionary<string, MdnsInfo> result = MdnsResponseParser.Parse(addresses, pointers, services, texts);

            Assert.Equal("AppleTV5,3", result["192.168.1.20"].Model);
        }

        [Fact]
        public void UncorrelatedRecordsProduceNoEntries()
        {
            List<MdnsAddressRecord> addresses = new();
            List<MdnsPointerRecord> pointers = new()
            {
                new MdnsPointerRecord("_airplay._tcp.local", "Living Room._airplay._tcp.local")
            };
            List<MdnsServiceRecord> services = new()
            {
                new MdnsServiceRecord("Living Room._airplay._tcp.local", "appletv.local")
            };
            List<MdnsTextRecord> texts = new();

            IReadOnlyDictionary<string, MdnsInfo> result = MdnsResponseParser.Parse(addresses, pointers, services, texts);

            Assert.Empty(result);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test NetworkMonitor.Tests --filter FullyQualifiedName~MdnsResponseParserTests`
Expected: FAIL — parser and record structs do not exist.

- [ ] **Step 3: Write the implementation**

`NetworkMonitor/Services/Scanning/MdnsResponseParser.cs`:

```csharp
namespace NetworkMonitor.Services.Scanning
{
    public readonly record struct MdnsAddressRecord(string Host, string Ip);

    public readonly record struct MdnsPointerRecord(string Service, string Instance);

    public readonly record struct MdnsServiceRecord(string Instance, string TargetHost);

    public readonly record struct MdnsTextRecord(string Name, IReadOnlyList<string> Entries);

    public static class MdnsResponseParser
    {
        private static readonly string[] ModelKeys = { "model", "md", "rpmd" };

        public static IReadOnlyDictionary<string, MdnsInfo> Parse(
            IReadOnlyList<MdnsAddressRecord> addressRecords,
            IReadOnlyList<MdnsPointerRecord> pointerRecords,
            IReadOnlyList<MdnsServiceRecord> serviceRecords,
            IReadOnlyList<MdnsTextRecord> textRecords)
        {
            Dictionary<string, string> hostToIp = new(StringComparer.OrdinalIgnoreCase);

            foreach (MdnsAddressRecord addressRecord in addressRecords)
            {

                if (!string.IsNullOrEmpty(addressRecord.Host) && !string.IsNullOrEmpty(addressRecord.Ip))
                {
                    hostToIp[Trim(addressRecord.Host)] = addressRecord.Ip;
                }

            }

            Dictionary<string, string> instanceToHost = new(StringComparer.OrdinalIgnoreCase);

            foreach (MdnsServiceRecord serviceRecord in serviceRecords)
            {

                if (!string.IsNullOrEmpty(serviceRecord.Instance) && !string.IsNullOrEmpty(serviceRecord.TargetHost))
                {
                    instanceToHost[Trim(serviceRecord.Instance)] = Trim(serviceRecord.TargetHost);
                }

            }

            Dictionary<string, MutableInfo> byIp = new(StringComparer.OrdinalIgnoreCase);

            foreach (MdnsPointerRecord pointerRecord in pointerRecords)
            {
                string instance = Trim(pointerRecord.Instance);
                string friendly = FriendlyLabel(instance, Trim(pointerRecord.Service));

                if (!string.IsNullOrEmpty(friendly)
                    && instanceToHost.TryGetValue(instance, out string? host)
                    && hostToIp.TryGetValue(host, out string? ip))
                {
                    MutableInfo info = GetOrAdd(byIp, ip);

                    if (string.IsNullOrEmpty(info.Name))
                    {
                        info.Name = friendly;
                    }

                }

            }

            foreach (MdnsTextRecord textRecord in textRecords)
            {
                string instance = Trim(textRecord.Name);
                string model = ExtractModel(textRecord.Entries);

                if (!string.IsNullOrEmpty(model)
                    && instanceToHost.TryGetValue(instance, out string? host)
                    && hostToIp.TryGetValue(host, out string? ip))
                {
                    MutableInfo info = GetOrAdd(byIp, ip);

                    if (string.IsNullOrEmpty(info.Model))
                    {
                        info.Model = model;
                    }

                }

            }

            Dictionary<string, MdnsInfo> result = new();

            foreach (KeyValuePair<string, MutableInfo> pair in byIp)
            {
                result[pair.Key] = new MdnsInfo(NullIfEmpty(pair.Value.Name), NullIfEmpty(pair.Value.Model));
            }

            return result;
        }

        private static string Trim(string value)
        {
            string result = value.Trim().TrimEnd('.');

            return result;
        }

        private static string FriendlyLabel(string instance, string service)
        {
            string label = instance;
            string suffix = "." + service;

            if (instance.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                label = instance.Substring(0, instance.Length - suffix.Length);
            }

            string result = label;

            return result;
        }

        private static string ExtractModel(IReadOnlyList<string> entries)
        {
            string result = string.Empty;

            foreach (string entry in entries)
            {
                int separator = entry.IndexOf('=');

                if (separator > 0)
                {
                    string key = entry.Substring(0, separator).Trim().ToLowerInvariant();
                    string value = entry.Substring(separator + 1).Trim();

                    if (value.Length > 0 && Array.IndexOf(ModelKeys, key) >= 0)
                    {
                        result = value;

                        break;
                    }

                }

            }

            return result;
        }

        private static MutableInfo GetOrAdd(Dictionary<string, MutableInfo> byIp, string ip)
        {

            if (!byIp.TryGetValue(ip, out MutableInfo? info))
            {
                info = new MutableInfo();
                byIp[ip] = info;
            }

            MutableInfo result = info;

            return result;
        }

        private static string? NullIfEmpty(string value)
        {
            string? result = string.IsNullOrEmpty(value) ? null : value;

            return result;
        }

        private sealed class MutableInfo
        {
            public string Name
            {
                get;
                set;
            } = string.Empty;

            public string Model
            {
                get;
                set;
            } = string.Empty;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test NetworkMonitor.Tests --filter FullyQualifiedName~MdnsResponseParserTests`
Expected: PASS (all three).

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor/Services/Scanning/MdnsResponseParser.cs NetworkMonitor.Tests/MdnsResponseParserTests.cs
git commit -m "Add MdnsResponseParser correlating mDNS records to IP name/model."
```

---

## Task 4: `Makaretu.Dns` package + `MdnsProbe` (I/O)

**Files:**
- Modify: `NetworkMonitor/NetworkMonitor.csproj`
- Create: `NetworkMonitor/Services/Scanning/MdnsProbe.cs`
- Modify: `NetworkMonitor/App.xaml.cs` (register the singleton)

**Interfaces:**
- Produces: `Task<IReadOnlyDictionary<string, MdnsInfo>> MdnsProbe.DiscoverAsync(TimeSpan window, CancellationToken ct)`.
- Consumes: `MdnsResponseParser.Parse` (Task 3), `MdnsInfo` (Task 2), `Makaretu.Dns` types.

**External assumption (verify at first build, like the TraceEvent note in the local-traffic plan):** the `Makaretu.Dns` multicast API exposes `MulticastService` (with `AnswerReceived` giving `MessageEventArgs.Message`, and `Start()`), `ServiceDiscovery(MulticastService)` with `QueryAllServices()`, and record types `ARecord` (`Name`, `Address`), `PTRRecord` (`Name`, `DomainName`), `SRVRecord` (`Name`, `Target`), `TXTRecord` (`Name`, `Strings`), with `Message.Answers` and `Message.AdditionalRecords` as `IEnumerable<ResourceRecord>`. If the installed package splits multicast into `Makaretu.Dns.Multicast`, add that package too; if type/property names differ, adjust the flatten switch accordingly. The parser (Task 3) is insulated from all of this.

- [ ] **Step 1: Add the package reference**

In `NetworkMonitor/NetworkMonitor.csproj`, add to the main `<ItemGroup>` of package references (after the `QuestPDF` line):

```xml
    <PackageReference Include="Makaretu.Dns" Version="2.0.1" />
```

Then restore and confirm the multicast types resolve:

Run: `dotnet restore NetworkMonitor/NetworkMonitor.csproj`
Expected: restore succeeds. If `MulticastService` / `ServiceDiscovery` are not found in `Makaretu.Dns`, also add `<PackageReference Include="Makaretu.Dns.Multicast" Version="0.27.0" />` and re-restore (verify the current versions on nuget.org).

- [ ] **Step 2: Create `MdnsProbe`**

`NetworkMonitor/Services/Scanning/MdnsProbe.cs`:

```csharp
using Makaretu.Dns;
using NetworkMonitor.Services.Platform;

namespace NetworkMonitor.Services.Scanning
{
    public class MdnsProbe
    {
        public async Task<IReadOnlyDictionary<string, MdnsInfo>> DiscoverAsync(TimeSpan window, CancellationToken ct)
        {
            IReadOnlyDictionary<string, MdnsInfo> result = new Dictionary<string, MdnsInfo>();

            try
            {
                List<Message> messages = new List<Message>();
                object gate = new object();

                using MulticastService multicast = new MulticastService();
                using ServiceDiscovery serviceDiscovery = new ServiceDiscovery(multicast);

                void OnAnswer(object? sender, MessageEventArgs eventArgs)
                {

                    lock (gate)
                    {
                        messages.Add(eventArgs.Message);
                    }

                }

                multicast.AnswerReceived += OnAnswer;
                multicast.Start();
                serviceDiscovery.QueryAllServices();

                try
                {
                    await Task.Delay(window, ct);
                }
                catch (OperationCanceledException)
                {
                }

                multicast.AnswerReceived -= OnAnswer;

                List<Message> snapshot;

                lock (gate)
                {
                    snapshot = new List<Message>(messages);
                }

                result = Flatten(snapshot);
            }
            catch (Exception exception)
            {
                AppLog.Error("MdnsProbe.DiscoverAsync", exception);
            }

            return result;
        }

        private static IReadOnlyDictionary<string, MdnsInfo> Flatten(IReadOnlyList<Message> messages)
        {
            List<MdnsAddressRecord> addresses = new List<MdnsAddressRecord>();
            List<MdnsPointerRecord> pointers = new List<MdnsPointerRecord>();
            List<MdnsServiceRecord> services = new List<MdnsServiceRecord>();
            List<MdnsTextRecord> texts = new List<MdnsTextRecord>();

            foreach (Message message in messages)
            {

                foreach (ResourceRecord record in message.Answers.Concat(message.AdditionalRecords))
                {

                    if (record is ARecord addressRecord)
                    {
                        addresses.Add(new MdnsAddressRecord(addressRecord.Name.ToString(), addressRecord.Address.ToString()));
                    }
                    else if (record is PTRRecord pointerRecord)
                    {
                        pointers.Add(new MdnsPointerRecord(pointerRecord.Name.ToString(), pointerRecord.DomainName.ToString()));
                    }
                    else if (record is SRVRecord serviceRecord)
                    {
                        services.Add(new MdnsServiceRecord(serviceRecord.Name.ToString(), serviceRecord.Target.ToString()));
                    }
                    else if (record is TXTRecord textRecord)
                    {
                        texts.Add(new MdnsTextRecord(textRecord.Name.ToString(), textRecord.Strings));
                    }

                }

            }

            IReadOnlyDictionary<string, MdnsInfo> result = MdnsResponseParser.Parse(addresses, pointers, services, texts);

            return result;
        }
    }
}
```

- [ ] **Step 3: Register the singleton**

In `NetworkMonitor/App.xaml.cs`, in `ConfigureServices`, add immediately after `services.AddSingleton<OuiDatabase>();`:

```csharp
                        services.AddSingleton<MdnsProbe>();
```

(`NetworkMonitor.Services.Scanning` is already imported in `App.xaml.cs`.)

- [ ] **Step 4: Build to verify**

Run: `dotnet build NetworkMonitor/NetworkMonitor.csproj -c Debug -p:Platform=x64`
Expected: Build succeeded. If a Makaretu type/property name does not resolve, apply the fallback from the External assumption note above.

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor/NetworkMonitor.csproj NetworkMonitor/Services/Scanning/MdnsProbe.cs NetworkMonitor/App.xaml.cs
git commit -m "Add Makaretu.Dns MdnsProbe and register it in DI."
```

---

## Task 5: Wire the probe into `NetworkScanner`

**Files:**
- Modify: `NetworkMonitor/Services/Scanning/NetworkScanner.cs`

**Interfaces:**
- Consumes: `MdnsProbe.DiscoverAsync` (Task 4), `MdnsInfo` (Task 2).
- Produces: extended `ScannedDevice(string Ip, string Mac, string? Hostname, string? Vendor, string? MdnsName, string? Model)`.

- [ ] **Step 1: Inject `MdnsProbe` and add the window constant**

Change the class declaration and add the constant next to the existing ones:

```csharp
    public partial class NetworkScanner(OuiDatabase oui, MdnsProbe mdnsProbe)
    {
        private const int MaxParallelDnsLookups = 20;

        private const int PingCancelBufferMs = 2000;

        private const int ArpTimeoutSeconds = 10;

        private const int MdnsListenMs = 2000;
```

- [ ] **Step 2: Run discovery in parallel and attach results**

In `ScanAsync`, start the probe before the ping sweep and await it before projecting devices. Replace the body from the `pingTasks` declaration through the `deviceTasks` projection with:

```csharp
            Task<IReadOnlyDictionary<string, MdnsInfo>> mdnsTask =
                mdnsProbe.DiscoverAsync(TimeSpan.FromMilliseconds(MdnsListenMs), ct);

            IEnumerable<Task<string?>> pingTasks = Enumerable
                .Range(settings.StartHost, settings.EndHost - settings.StartHost + 1)
                .Select(host => PingHostAsync($"{settings.SubnetBase}.{host}", settings.PingTimeoutMs, semaphore, ct));

            IEnumerable<string> respondingIps = (await Task.WhenAll(pingTasks))
                .Where(ip => ip is not null)
                .Select(ip => ip!);

            Dictionary<string, string> arpTable = await GetArpTableAsync(ct);

            IReadOnlyDictionary<string, MdnsInfo> mdnsMap = await mdnsTask;

            List<Task<ScannedDevice>> deviceTasks = respondingIps
                .Where(ip => arpTable.ContainsKey(ip))
                .Select(async ip =>
                {
                    string mac = arpTable[ip];
                    string? hostname = await ResolveHostnameAsync(ip, dnsSemaphore, ct);
                    string? vendor = oui.Lookup(mac);
                    mdnsMap.TryGetValue(ip, out MdnsInfo? mdnsInfo);
                    ScannedDevice scannedDevice = new(ip, mac, hostname, vendor, mdnsInfo?.Name, mdnsInfo?.Model);

                    return scannedDevice;
                })
                .ToList();
```

- [ ] **Step 3: Extend the `ScannedDevice` record**

At the bottom of the file, replace the record:

```csharp
    public record ScannedDevice(string Ip, string Mac, string? Hostname, string? Vendor, string? MdnsName, string? Model);
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build NetworkMonitor/NetworkMonitor.csproj -c Debug -p:Platform=x64`
Expected: Build succeeded. (`DeviceTracker` still constructs/reads `ScannedDevice` by name — the new positional members are additive and consumed in Task 6.)

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor/Services/Scanning/NetworkScanner.cs
git commit -m "Run mDNS discovery in the scan and attach name/model to ScannedDevice."
```

---

## Task 6: Apply enrichment in `DeviceTracker`

**Files:**
- Modify: `NetworkMonitor/Services/Scanning/DeviceTracker.cs`

**Interfaces:**
- Consumes: `MdnsEnrichment.Apply` (Task 2), `MdnsInfo` (Task 2), `ScannedDevice.MdnsName`/`.Model` (Task 5).

- [ ] **Step 1: Call the enrichment helper in the merge loop**

In `DeviceTracker.MergeAsync`, inside the `foreach (ScannedDevice scannedDevice in scanned)` loop, immediately after the line `device.Vendor ??= scannedDevice.Vendor;` add:

```csharp
                MdnsEnrichment.Apply(device, new MdnsInfo(scannedDevice.MdnsName, scannedDevice.Model));
```

(`DeviceTracker` is already in the `NetworkMonitor.Services.Scanning` namespace, so `MdnsEnrichment` and `MdnsInfo` need no extra using.)

- [ ] **Step 2: Build to verify**

Run: `dotnet build NetworkMonitor/NetworkMonitor.csproj -c Debug -p:Platform=x64`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add NetworkMonitor/Services/Scanning/DeviceTracker.cs
git commit -m "Apply mDNS name/model enrichment during device merge."
```

---

## Task 7: Surface `Model` in the device grid + final gate

**Files:**
- Modify: `NetworkMonitor/Views/AllDevicesPage.xaml`
- Modify: `NetworkMonitor.slnx`

**Interfaces:**
- Consumes: `Device.Model` (Task 1).

- [ ] **Step 1: Add the `Model` column**

In `NetworkMonitor/Views/AllDevicesPage.xaml`, immediately after the `Vendor` column block (the `DataGridTextColumn` bound to `Vendor`) and before the `Actions` `DataGridTemplateColumn`, add:

```xml
                <controls:DataGridTextColumn
                    Header="Model"
                    Width="160"
                    Binding="{Binding Model}" />
```

Keep the surrounding blank lines between sibling elements per the XAML convention.

- [ ] **Step 2: Confirm this plan is in the slnx**

This plan file was already registered in `NetworkMonitor.slnx` (inside `<Folder Name="/Documents/Superpowers/Plans/">`) when it was written. Verify the line is present; add it only if missing:

```xml
    <File Path="Documents/superpowers/plans/2026-07-07-mdns-enrichment.md" />
```

- [ ] **Step 3: Full test suite**

Run: `dotnet test NetworkMonitor.Tests`
Expected: all existing tests plus the new `DeviceTests` cases, `MdnsEnrichmentTests`, and `MdnsResponseParserTests` PASS.

- [ ] **Step 4: Release build gate**

Run: `dotnet build NetworkMonitor.slnx -c Release -p:Platform=x64`
Expected: Build succeeded.

- [ ] **Step 5: Manual end-to-end verification**

1. Delete the local DB once (schema changed): close the app, delete `%LOCALAPPDATA%\...\networkmonitor.db` (path from `AppDbContext.DbPath`).
2. Run the app (VS, x64, Debug). Trigger **Scan Network** on the Devices page.
3. On a network with Apple/Google/IoT devices, confirm that a device which previously showed only an IP or bare hostname now shows an mDNS-derived name in the grid, and that the new **Model** column is populated for at least one device (e.g. an Apple TV / HomePod / Chromecast).
4. Confirm a device you have given a `FriendlyName` still shows that curated name (mDNS did not override it).
5. Confirm the scan still completes and no error dialog appears when mDNS returns nothing (e.g. on a wired-only segment).

- [ ] **Step 6: Commit**

```bash
git add NetworkMonitor/Views/AllDevicesPage.xaml NetworkMonitor.slnx
git commit -m "Show Model column in the device grid and register the mDNS plan."
```

---

## Final: completion summary

- [ ] Completion summary must state: **a one-time local DB delete is required on upgrade** (two new `Device` columns `MdnsName` + `Model` via EnsureCreated).
- [ ] Confirm all commits are pushed to **both** remotes (`git push all master` — GitHub master + DevOps mirror).

---

## Self-Review Notes (author checklist, already applied)

- **Spec coverage:** goal/scope §1 (all tasks) · data model §3 `MdnsName`+`Model`+`DisplayName`+`CopyValuesFrom` (Task 1) · `MdnsInfo`/`MdnsResponseParser` §4 (Tasks 2/3) · `MdnsProbe` + `Makaretu.Dns` §4/§6 (Task 4) · scan pipeline §5 `ScannedDevice` + parallel discovery (Task 5) · merge fill semantics §5 (Tasks 2/6) · error handling §7 (Task 4 guard) · Model UI surfacing §3 (Task 7) · tests §8 parser + enrichment + DisplayName precedence (Tasks 1/2/3). All covered.
- **Fill-blanks nuance:** `FriendlyName` is never written by any task; `MdnsName`/`Model` are refreshed from non-empty scan values and never nulled when a device is silent (`MdnsEnrichment.Apply` guards on `IsNullOrWhiteSpace`).
- **Type consistency:** `MdnsInfo(Name, Model)`, `ScannedDevice(..., MdnsName, Model)`, and the four neutral record structs are used with identical names/signatures across Tasks 2–6.
- **External assumption:** the `Makaretu.Dns` multicast API surface (package name/version and record property names) is the one thing to confirm at Task 4 build; the pure parser is fully insulated and unit-tested independently.
- **slnx:** only the plan `.md` is added (Task 7) — source `.cs` files are SDK-globbed, confirmed against the actual `NetworkMonitor.slnx` (lists only docs/config + the two projects).
```

---

## Completion & Verification (2026-07-09)

All 7 tasks are implemented. Final state: **114/114 tests pass**, `dotnet build NetworkMonitor.slnx -c Release -p:Platform=x64` succeeds.

### Deviations from the base plan

1. **Test project needs linked `<Compile>` entries.** `NetworkMonitor.Tests` cannot reference the WinUI project (windows-specific TFM), so it links pure source files individually. `MdnsInfo.cs`, `MdnsEnrichment.cs`, and `MdnsResponseParser.cs` were added as `<Compile Include ...><Link>` entries in `NetworkMonitor.Tests.csproj`. The plan omitted this; without it the new tests do not compile.
2. **Package split.** `Makaretu.Dns` 2.x moves the multicast API into a separate package, so **both** `Makaretu.Dns` 2.0.1 and `Makaretu.Dns.Multicast` 0.27.0 are referenced (the multicast package depends on the 2.0.1 core). The record/property names in the plan's External-assumption note all resolved as written.

### Bugs found during live verification (and fixed)

Verification used a throwaway console harness (`MdnsCheck`) that links the real `MdnsResponseParser` and runs an actual multicast discovery, plus a synthetic advertiser (`MdnsAdvertise`) broadcasting `_airplay._tcp` with `model=AppleTV5,3`. These live runs surfaced two defects the unit tests alone did not:

1. **Opaque service instance names became the display name.** A locked dev iPhone advertised only `_remotepairing._tcp`, whose instance label is a UUID. The parser took that UUID as `MdnsName`, and since `DisplayName = FriendlyName ?? MdnsName ?? Hostname ?? IpAddress`, the GUID *outranked* the good hostname. **Fix:** `MdnsResponseParser.IsOpaqueName` skips GUID-form labels and known infra/pairing service types (`_remotepairing`, `_apple-mobdev`, `_sleep-proxy`, `_rdlink`) when choosing a friendly name.
2. **DNS presentation-format escapes were not decoded.** `Makaretu` returns names in wire/presentation format, so a name with a space came through as `Living\032Room`. Very common for the Apple/Google devices this feature targets. **Fix:** `MdnsResponseParser.Unescape` decodes `\DDD` decimal escapes and `\<char>` escapes on the friendly name.

Three parser tests were added for these: DNS-escape decoding, opaque-GUID skip, and preferring a friendly service (`_airplay`) over an opaque one for the same IP.

### Other change

- **Listen window 2s → 4s** (`NetworkScanner.MdnsListenMs`). Even multi-second live probes were slow to catch some devices (notably iOS); 2s missed too much. The window overlaps the ping sweep, so the bump adds little/no wall-clock cost.

### Live results

- **eWeLink smart plug** — named correctly from a real scan (`eWeLink_1000beb2e9`), no model advertised (blank Model, as expected).
- **Synthetic Apple TV** — `Name='Fake Apple TV'  Model='AppleTV5,3'`, confirming the full PTR→SRV→A + TXT-model pipeline and the unescape fix end-to-end.
- **A second eWeLink with no A record** — correctly produced no phantom row.
- **iPhone (dev device)** — will not self-advertise a friendly name/model on this network: iPhones are not AirPlay receivers, so they only expose `_companion-link` (needs Handoff + other Apple devices) or the opaque `_remotepairing`. This is iOS behaviour, not a code gap. The Model column populates from real Apple receivers (Apple TV / HomePod / Mac) or an actively-advertising iPhone.

### Remaining

- Manual in-app walkthrough after a one-time DB delete (run a scan, confirm a previously-bare device shows an mDNS name and the Model column populates, and a curated `FriendlyName` is not overridden).

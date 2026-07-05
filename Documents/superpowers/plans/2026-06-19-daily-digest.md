# Daily Digest Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an automatically-generated daily digest — a stored 24-hour summary of network activity, viewable in a Reports page and exportable per report to PDF and CSV.

**Architecture:** A pure `DigestSummaryBuilder` computes a `DigestSummary` snapshot from in-memory event/device/traffic data; `DigestGenerator` does the EF I/O and persists a `DigestReport` (snapshot stored as JSON). A `DigestWorker` background service uses a pure `DigestSchedule` helper to catch up missed days on startup and generate daily at 06:00 local. The Reports page renders each report from its stored snapshot; Win2D produces chart PNGs shared by the page and the QuestPDF export.

**Tech Stack:** WinUI 3 (.NET 10, unpackaged), EF Core 10 + SQLite (EnsureCreated, no migrations), CommunityToolkit.Mvvm, Win2D (`Microsoft.Graphics.Win2D`), QuestPDF, xUnit (v3).

## Global Constraints

These apply to **every** task (copied from `CLAUDE.md`):

- No `var` — always explicit types. No single-character identifiers anywhere (including lambdas/pattern variables).
- Always use curly braces on `if/else/for/foreach/while/using` — even single-line bodies.
- Prefer `string.Empty` over `""`.
- No comments unless the WHY is non-obvious. No trailing summary comments after methods.
- **Single exit point** — one `return` at the end of each method; assign computed values to a local first, then `return` that local. Blank line above the `return`.
- **Blank lines around all blocks** — every `if/else/foreach/for/while/switch/try/catch/finally/using` block, including a blank line after a method/ctor opening `{` when the first statement is a block and before the closing `}` when the last statement ends with `}`. Applies at every nesting level.
- **Class member order:** Fields → Constructor → Properties → Public methods → Override methods → Private methods. Injected dependencies/state fields grouped before the constructor.
- **Backing field above its property** (in the Properties section), separated by a blank line. Hand-write properties with `SetProperty(ref _field, value)` (CommunityToolkit.Mvvm `ObservableObject`) — do NOT use `[ObservableProperty]`.
- **Property braces:** `{`, `get;`, `set;` (and `init;`/`private set;`) each on their own line. Expression-bodied (`=>`) properties exempt.
- **No underscores in identifiers** except a single leading underscore on private fields. camelCase locals/params/methods.
- **XAML:** blank line between `<?xml?>` and root; element name on its own line; each attribute on its own line indented 4 spaces; attribute order = simple assignments → event handlers/`Command` → value bindings; `/>` or `>` on the last attribute line; blank line above/below every element and after a container's opening tag / before its closing tag. `DevicesPage.xaml` is the canonical reference.
- **Build:** Visual Studio / `dotnet`, platform **x64** (WinUI 3 has no Any CPU). The app self-elevates and may be running — if a build fails only on copying `NetworkMonitor.exe` (MSB3027/MSB3021 file lock), that is the running app, not a code error.
- **DB:** EF Core `EnsureCreated` + manual `EnsureXxxTableAsync()` methods (no migrations), following `AppDbContext`.
- **QuestPDF** is licensed Community (free under USD 1M revenue) — set `QuestPDF.Settings.License = LicenseType.Community;` once at startup.
- Timestamps are stored **UTC**; the 06:00 generation boundary is **local time** converted to UTC for queries.

---

### Task 1: Data model — `DigestReport`, `DigestSummary`, DB table

**Files:**
- Create: `NetworkMonitor/Models/DigestSummary.cs`
- Create: `NetworkMonitor/Models/DigestReport.cs`
- Modify: `NetworkMonitor/Data/AppDbContext.cs`
- Modify: `NetworkMonitor/App.xaml.cs` (call the new ensure-table method at startup, next to the other `Ensure*` calls)

**Interfaces:**
- Produces: `NetworkMonitor.Models.DigestSummary` with members `Headline:string`, `TotalBytesSent:long`, `TotalBytesReceived:long`, `TopApps:List<TrafficAppSummary>`, `NewDevices:List<NewDeviceSummary>`, `AppearedCount:int`, `DisappearedCount:int`, `OnlineCount:int`, `OfflineCount:int`, `HourlyActivity:List<HourlyActivitySummary>`, `UnknownDevices:List<UnknownDeviceSummary>`.
- Produces nested: `TrafficAppSummary{ ProcessName:string, BytesSent:long, BytesReceived:long }`, `NewDeviceSummary{ DisplayName:string, MacAddress:string, IpAddress:string, Vendor:string, Type:DeviceType, IsKnown:bool, FirstSeen:DateTime }`, `UnknownDeviceSummary{ DisplayName:string, MacAddress:string, IpAddress:string, Vendor:string, Type:DeviceType }`, `HourlyActivitySummary{ Hour:int, Appeared:int, Disappeared:int }`.
- Produces: `DigestReport{ Id:int, PeriodStart:DateTime, PeriodEnd:DateTime, GeneratedAt:DateTime, Headline:string, SummaryJson:string, IsScheduled:bool }`. `IsScheduled` is `true` for daily/catch-up generation and `false` for manual "Generate now" runs, so catch-up can anchor on the last *scheduled* report only.
- Produces: `AppDbContext.DigestReports:DbSet<DigestReport>` and `Task EnsureDigestReportsTableAsync()`.

- [ ] **Step 1: Create `DigestSummary.cs`**

```csharp
using NetworkMonitor.Models;

namespace NetworkMonitor.Models
{
    public class DigestSummary
    {
        public string Headline
        {
            get;
            set;
        } = string.Empty;

        public long TotalBytesSent
        {
            get;
            set;
        }

        public long TotalBytesReceived
        {
            get;
            set;
        }

        public List<TrafficAppSummary> TopApps
        {
            get;
            set;
        } = new();

        public List<NewDeviceSummary> NewDevices
        {
            get;
            set;
        } = new();

        public int AppearedCount
        {
            get;
            set;
        }

        public int DisappearedCount
        {
            get;
            set;
        }

        public int OnlineCount
        {
            get;
            set;
        }

        public int OfflineCount
        {
            get;
            set;
        }

        public List<HourlyActivitySummary> HourlyActivity
        {
            get;
            set;
        } = new();

        public List<UnknownDeviceSummary> UnknownDevices
        {
            get;
            set;
        } = new();
    }

    public class TrafficAppSummary
    {
        public string ProcessName
        {
            get;
            set;
        } = string.Empty;

        public long BytesSent
        {
            get;
            set;
        }

        public long BytesReceived
        {
            get;
            set;
        }
    }

    public class NewDeviceSummary
    {
        public string DisplayName
        {
            get;
            set;
        } = string.Empty;

        public string MacAddress
        {
            get;
            set;
        } = string.Empty;

        public string IpAddress
        {
            get;
            set;
        } = string.Empty;

        public string Vendor
        {
            get;
            set;
        } = string.Empty;

        public DeviceType Type
        {
            get;
            set;
        }

        public bool IsKnown
        {
            get;
            set;
        }

        public DateTime FirstSeen
        {
            get;
            set;
        }
    }

    public class UnknownDeviceSummary
    {
        public string DisplayName
        {
            get;
            set;
        } = string.Empty;

        public string MacAddress
        {
            get;
            set;
        } = string.Empty;

        public string IpAddress
        {
            get;
            set;
        } = string.Empty;

        public string Vendor
        {
            get;
            set;
        } = string.Empty;

        public DeviceType Type
        {
            get;
            set;
        }
    }

    public class HourlyActivitySummary
    {
        public int Hour
        {
            get;
            set;
        }

        public int Appeared
        {
            get;
            set;
        }

        public int Disappeared
        {
            get;
            set;
        }
    }
}
```

- [ ] **Step 2: Create `DigestReport.cs`**

```csharp
namespace NetworkMonitor.Models
{
    public class DigestReport
    {
        public int Id
        {
            get;
            set;
        }

        public DateTime PeriodStart
        {
            get;
            set;
        }

        public DateTime PeriodEnd
        {
            get;
            set;
        }

        public DateTime GeneratedAt
        {
            get;
            set;
        }

        public string Headline
        {
            get;
            set;
        } = string.Empty;

        public string SummaryJson
        {
            get;
            set;
        } = string.Empty;

        public bool IsScheduled
        {
            get;
            set;
        }
    }
}
```

- [ ] **Step 3: Add the `DbSet` and ensure-table method to `AppDbContext.cs`**

Add to the `DbSet` declarations (next to `DeviceEvents`):

```csharp
public DbSet<DigestReport> DigestReports => Set<DigestReport>();
```

Add a method modelled on the existing `EnsureDeviceEventsTableAsync` (use the same `ExecuteSqlRawAsync` raw-SQL style already in the file):

```csharp
public async Task EnsureDigestReportsTableAsync()
{
    string createTableSql = """
                            CREATE TABLE IF NOT EXISTS DigestReports (
                                Id INTEGER NOT NULL CONSTRAINT PK_DigestReports PRIMARY KEY AUTOINCREMENT,
                                PeriodStart TEXT NOT NULL,
                                PeriodEnd TEXT NOT NULL,
                                GeneratedAt TEXT NOT NULL,
                                Headline TEXT NOT NULL,
                                SummaryJson TEXT NOT NULL,
                                IsScheduled INTEGER NOT NULL DEFAULT 0
                            );
                            """;

    await Database.ExecuteSqlRawAsync(createTableSql);
}
```

Confirm `using NetworkMonitor.Models;` is present at the top of `AppDbContext.cs` (it already references model types).

- [ ] **Step 4: Call the ensure-table method at startup**

In `App.xaml.cs`, inside the `await Task.Run(async () => { ... })` DB-init block, add a line next to the other `Ensure*` calls (e.g. after `await db.EnsureDeviceEventsTableAsync();`):

```csharp
await db.EnsureDigestReportsTableAsync();
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build "NetworkMonitor/NetworkMonitor.csproj" -c Debug -p:Platform=x64 --nologo -v m`
Expected: `Build succeeded. 0 Error(s)` (ignore the pre-existing WMC1506/CS0108 warnings; if it fails only on copying `NetworkMonitor.exe`, the app is running — close it).

- [ ] **Step 6: Commit**

```bash
git add NetworkMonitor/Models/DigestSummary.cs NetworkMonitor/Models/DigestReport.cs NetworkMonitor/Data/AppDbContext.cs NetworkMonitor/App.xaml.cs
git commit -m "Add DigestReport entity, DigestSummary snapshot, and DB table."
```

---

### Task 2: `DigestSummaryBuilder` (pure computation) — TDD

**Files:**
- Create: `NetworkMonitor/Services/AppTrafficTotal.cs`
- Create: `NetworkMonitor/Services/DigestSummaryBuilder.cs`
- Modify: `NetworkMonitor.Tests/NetworkMonitor.Tests.csproj` (link the source files under test)
- Test: `NetworkMonitor.Tests/DigestSummaryBuilderTests.cs`

**Interfaces:**
- Consumes: `Device`, `DeviceEvent`, `DeviceEventType`, `DeviceType` (existing models); `DigestSummary` + nested DTOs (Task 1).
- Produces: `AppTrafficTotal{ ProcessName:string, BytesSent:long, BytesReceived:long }` (input row for already-aggregated per-process traffic).
- Produces: `static DigestSummary DigestSummaryBuilder.Build(IReadOnlyList<DeviceEvent> events, IReadOnlyList<Device> devices, IReadOnlyList<AppTrafficTotal> traffic, DateTime startUtc, DateTime endUtc)`. New devices = `devices` whose `FirstSeen` is within `[startUtc, endUtc)`. Unknown present = `devices` with `IsKnown == false`. Online/offline counts from `devices`. Top apps = traffic ordered by `(BytesSent + BytesReceived)` desc, take 10. Hourly buckets indexed by local hour 0–23 of each event's `Timestamp`. Headline format defined in Step 3.

- [ ] **Step 1: Link the source files into the test project**

In `NetworkMonitor.Tests/NetworkMonitor.Tests.csproj`, add to the existing `<ItemGroup>` of `<Compile Include="..\NetworkMonitor\...">` links:

```xml
<Compile Include="..\NetworkMonitor\Models\DeviceEvent.cs">
  <Link>Linked\DeviceEvent.cs</Link>
</Compile>
<Compile Include="..\NetworkMonitor\Models\DeviceEventType.cs">
  <Link>Linked\DeviceEventType.cs</Link>
</Compile>
<Compile Include="..\NetworkMonitor\Models\DigestSummary.cs">
  <Link>Linked\DigestSummary.cs</Link>
</Compile>
<Compile Include="..\NetworkMonitor\Services\AppTrafficTotal.cs">
  <Link>Linked\AppTrafficTotal.cs</Link>
</Compile>
<Compile Include="..\NetworkMonitor\Services\DigestSummaryBuilder.cs">
  <Link>Linked\DigestSummaryBuilder.cs</Link>
</Compile>
```

(`Device.cs` and `DeviceType.cs` are already linked.)

- [ ] **Step 2: Write the failing tests**

Create `NetworkMonitor.Tests/DigestSummaryBuilderTests.cs`:

```csharp
using NetworkMonitor.Models;
using NetworkMonitor.Services;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class DigestSummaryBuilderTests
    {
        private static readonly DateTime WindowStart = new DateTime(2026, 6, 18, 6, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime WindowEnd = new DateTime(2026, 6, 19, 6, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void Build_TopApps_AreOrderedByTotalBytesAndCappedAtTen()
        {
            List<AppTrafficTotal> traffic = new();

            for (int appIndex = 0; appIndex < 12; appIndex++)
            {
                traffic.Add(new AppTrafficTotal
                {
                    ProcessName = $"app{appIndex}",
                    BytesSent = appIndex * 100,
                    BytesReceived = appIndex * 100
                });
            }

            DigestSummary summary = DigestSummaryBuilder.Build(
                new List<DeviceEvent>(), new List<Device>(), traffic, WindowStart, WindowEnd);

            Assert.Equal(10, summary.TopApps.Count);
            Assert.Equal("app11", summary.TopApps[0].ProcessName);
            Assert.Equal("app2", summary.TopApps[9].ProcessName);
        }

        [Fact]
        public void Build_Totals_SumAllTraffic()
        {
            List<AppTrafficTotal> traffic = new()
            {
                new AppTrafficTotal { ProcessName = "a", BytesSent = 10, BytesReceived = 5 },
                new AppTrafficTotal { ProcessName = "b", BytesSent = 20, BytesReceived = 7 }
            };

            DigestSummary summary = DigestSummaryBuilder.Build(
                new List<DeviceEvent>(), new List<Device>(), traffic, WindowStart, WindowEnd);

            Assert.Equal(30, summary.TotalBytesSent);
            Assert.Equal(12, summary.TotalBytesReceived);
        }

        [Fact]
        public void Build_NewDevices_AreThoseFirstSeenInWindow()
        {
            List<Device> devices = new()
            {
                new Device { MacAddress = "AA", FirstSeen = WindowStart.AddHours(1), IsKnown = false },
                new Device { MacAddress = "BB", FirstSeen = WindowStart.AddDays(-5), IsKnown = true }
            };

            DigestSummary summary = DigestSummaryBuilder.Build(
                new List<DeviceEvent>(), devices, new List<AppTrafficTotal>(), WindowStart, WindowEnd);

            Assert.Single(summary.NewDevices);
            Assert.Equal("AA", summary.NewDevices[0].MacAddress);
        }

        [Fact]
        public void Build_UnknownDevices_AreThoseNotKnown()
        {
            List<Device> devices = new()
            {
                new Device { MacAddress = "AA", IsKnown = false, FirstSeen = WindowStart.AddDays(-1) },
                new Device { MacAddress = "BB", IsKnown = true, FirstSeen = WindowStart.AddDays(-1) }
            };

            DigestSummary summary = DigestSummaryBuilder.Build(
                new List<DeviceEvent>(), devices, new List<AppTrafficTotal>(), WindowStart, WindowEnd);

            Assert.Single(summary.UnknownDevices);
            Assert.Equal("AA", summary.UnknownDevices[0].MacAddress);
        }

        [Fact]
        public void Build_ActivityCounts_MatchEventTypes()
        {
            List<DeviceEvent> events = new()
            {
                new DeviceEvent { EventType = DeviceEventType.Appeared, Timestamp = WindowStart.AddHours(2) },
                new DeviceEvent { EventType = DeviceEventType.Appeared, Timestamp = WindowStart.AddHours(2) },
                new DeviceEvent { EventType = DeviceEventType.Disappeared, Timestamp = WindowStart.AddHours(3) }
            };

            DigestSummary summary = DigestSummaryBuilder.Build(
                events, new List<Device>(), new List<AppTrafficTotal>(), WindowStart, WindowEnd);

            Assert.Equal(2, summary.AppearedCount);
            Assert.Equal(1, summary.DisappearedCount);
        }

        [Fact]
        public void Build_OnlineOfflineCounts_ComeFromDevices()
        {
            List<Device> devices = new()
            {
                new Device { MacAddress = "AA", IsOnline = true, FirstSeen = WindowStart.AddDays(-1) },
                new Device { MacAddress = "BB", IsOnline = false, FirstSeen = WindowStart.AddDays(-1) },
                new Device { MacAddress = "CC", IsOnline = true, FirstSeen = WindowStart.AddDays(-1) }
            };

            DigestSummary summary = DigestSummaryBuilder.Build(
                new List<DeviceEvent>(), devices, new List<AppTrafficTotal>(), WindowStart, WindowEnd);

            Assert.Equal(2, summary.OnlineCount);
            Assert.Equal(1, summary.OfflineCount);
        }

        [Fact]
        public void Build_Headline_CallsOutNewUnknownDevices()
        {
            List<Device> devices = new()
            {
                new Device { MacAddress = "AA", FirstSeen = WindowStart.AddHours(1), IsKnown = false }
            };

            DigestSummary summary = DigestSummaryBuilder.Build(
                new List<DeviceEvent>(), devices, new List<AppTrafficTotal>(), WindowStart, WindowEnd);

            Assert.Contains("1 new unknown device", summary.Headline);
        }
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test "NetworkMonitor.Tests/NetworkMonitor.Tests.csproj" --nologo`
Expected: FAIL — `AppTrafficTotal` / `DigestSummaryBuilder` do not exist (compile error).

- [ ] **Step 4: Create `AppTrafficTotal.cs`**

```csharp
namespace NetworkMonitor.Services
{
    public class AppTrafficTotal
    {
        public string ProcessName
        {
            get;
            set;
        } = string.Empty;

        public long BytesSent
        {
            get;
            set;
        }

        public long BytesReceived
        {
            get;
            set;
        }
    }
}
```

- [ ] **Step 5: Implement `DigestSummaryBuilder.cs`**

```csharp
using NetworkMonitor.Models;

namespace NetworkMonitor.Services
{
    public static class DigestSummaryBuilder
    {
        public static DigestSummary Build(
            IReadOnlyList<DeviceEvent> events,
            IReadOnlyList<Device> devices,
            IReadOnlyList<AppTrafficTotal> traffic,
            DateTime startUtc,
            DateTime endUtc)
        {
            DigestSummary summary = new DigestSummary();

            summary.TopApps = traffic
                .OrderByDescending(appTotal => appTotal.BytesSent + appTotal.BytesReceived)
                .Take(10)
                .Select(appTotal => new TrafficAppSummary
                {
                    ProcessName = appTotal.ProcessName,
                    BytesSent = appTotal.BytesSent,
                    BytesReceived = appTotal.BytesReceived
                })
                .ToList();

            summary.TotalBytesSent = traffic.Sum(appTotal => appTotal.BytesSent);
            summary.TotalBytesReceived = traffic.Sum(appTotal => appTotal.BytesReceived);

            summary.NewDevices = devices
                .Where(device => device.FirstSeen >= startUtc && device.FirstSeen < endUtc)
                .OrderBy(device => device.FirstSeen)
                .Select(device => new NewDeviceSummary
                {
                    DisplayName = device.DisplayName,
                    MacAddress = device.MacAddress,
                    IpAddress = device.IpAddress,
                    Vendor = device.Vendor ?? string.Empty,
                    Type = device.Type,
                    IsKnown = device.IsKnown,
                    FirstSeen = device.FirstSeen
                })
                .ToList();

            summary.UnknownDevices = devices
                .Where(device => !device.IsKnown)
                .Select(device => new UnknownDeviceSummary
                {
                    DisplayName = device.DisplayName,
                    MacAddress = device.MacAddress,
                    IpAddress = device.IpAddress,
                    Vendor = device.Vendor ?? string.Empty,
                    Type = device.Type
                })
                .ToList();

            summary.AppearedCount = events.Count(deviceEvent => deviceEvent.EventType == DeviceEventType.Appeared);
            summary.DisappearedCount = events.Count(deviceEvent => deviceEvent.EventType == DeviceEventType.Disappeared);
            summary.OnlineCount = devices.Count(device => device.IsOnline);
            summary.OfflineCount = devices.Count(device => !device.IsOnline);
            summary.HourlyActivity = BuildHourlyActivity(events);
            summary.Headline = BuildHeadline(summary);

            return summary;
        }

        private static List<HourlyActivitySummary> BuildHourlyActivity(IReadOnlyList<DeviceEvent> events)
        {
            List<HourlyActivitySummary> hourly = new();

            for (int hour = 0; hour < 24; hour++)
            {
                hourly.Add(new HourlyActivitySummary { Hour = hour, Appeared = 0, Disappeared = 0 });
            }

            foreach (DeviceEvent deviceEvent in events)
            {
                int localHour = deviceEvent.Timestamp.ToLocalTime().Hour;

                if (deviceEvent.EventType == DeviceEventType.Appeared)
                {
                    hourly[localHour].Appeared++;
                }
                else
                {
                    hourly[localHour].Disappeared++;
                }

            }

            return hourly;
        }

        private static string BuildHeadline(DigestSummary summary)
        {
            int newUnknown = summary.NewDevices.Count(device => !device.IsKnown);
            double totalGb = (summary.TotalBytesSent + summary.TotalBytesReceived) / 1_073_741_824.0;
            string trafficPart = $"{totalGb:0.0} GB traffic";
            string headline;

            if (newUnknown > 0)
            {
                string plural = newUnknown == 1 ? "device" : "devices";
                headline = $"⚠️ {newUnknown} new unknown {plural} · {trafficPart}";
            }
            else
            {
                string plural = summary.NewDevices.Count == 1 ? "device" : "devices";
                headline = $"{summary.NewDevices.Count} new {plural} · {trafficPart}";
            }

            return headline;
        }
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test "NetworkMonitor.Tests/NetworkMonitor.Tests.csproj" --nologo`
Expected: PASS — all 7 new tests pass, plus every pre-existing test (do not assume a fixed count; the baseline is whatever the suite reported before this task — Theories expand to more cases than `[Fact]`/`[Theory]` method count).

- [ ] **Step 7: Commit**

```bash
git add NetworkMonitor/Services/AppTrafficTotal.cs NetworkMonitor/Services/DigestSummaryBuilder.cs NetworkMonitor.Tests/DigestSummaryBuilderTests.cs NetworkMonitor.Tests/NetworkMonitor.Tests.csproj
git commit -m "Add DigestSummaryBuilder with tests."
```

---

### Task 3: `DigestGenerator` (EF orchestration) + DI

**Files:**
- Create: `NetworkMonitor/Services/DigestGenerator.cs`
- Modify: `NetworkMonitor/App.xaml.cs` (register the service)

**Interfaces:**
- Consumes: `IDbContextFactory<AppDbContext>`, `DigestSummaryBuilder.Build(...)`, `AppTrafficTotal`, `DigestReport`.
- Produces: `DigestGenerator.GenerateAsync(DateTime startUtc, DateTime endUtc, bool isScheduled, CancellationToken ct = default) : Task<DigestReport>` — queries the window, builds + serialises the summary, saves (with `IsScheduled = isScheduled`) and returns the `DigestReport`. `event EventHandler<DigestReport>? ReportGenerated`.

- [ ] **Step 1: Create `DigestGenerator.cs`**

```csharp
using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetworkMonitor.Data;
using NetworkMonitor.Models;

namespace NetworkMonitor.Services
{
    public class DigestGenerator(IDbContextFactory<AppDbContext> dbFactory)
    {
        public event EventHandler<DigestReport>? ReportGenerated;

        public async Task<DigestReport> GenerateAsync(DateTime startUtc, DateTime endUtc, bool isScheduled, CancellationToken ct = default)
        {
            await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);

            List<DeviceEvent> events = await db.DeviceEvents
                .Where(deviceEvent => deviceEvent.Timestamp >= startUtc && deviceEvent.Timestamp < endUtc)
                .ToListAsync(ct);

            List<Device> devices = await db.Devices.ToListAsync(ct);
            List<AppTrafficTotal> traffic = await LoadTrafficTotalsAsync(db, startUtc, endUtc, ct);

            DigestSummary summary = DigestSummaryBuilder.Build(events, devices, traffic, startUtc, endUtc);

            DigestReport report = new DigestReport
            {
                PeriodStart = startUtc,
                PeriodEnd = endUtc,
                GeneratedAt = DateTime.UtcNow,
                Headline = summary.Headline,
                SummaryJson = JsonSerializer.Serialize(summary),
                IsScheduled = isScheduled
            };

            db.DigestReports.Add(report);
            await db.SaveChangesAsync(ct);

            ReportGenerated?.Invoke(this, report);

            return report;
        }

        private static async Task<List<AppTrafficTotal>> LoadTrafficTotalsAsync(
            AppDbContext db, DateTime startUtc, DateTime endUtc, CancellationToken ct)
        {
            List<AppTrafficTotal> totals = new();
            long startEpoch = (long)(startUtc - DateTime.UnixEpoch).TotalSeconds;
            long endEpoch = (long)(endUtc - DateTime.UnixEpoch).TotalSeconds;

            await db.Database.OpenConnectionAsync(ct);

            DbConnection connection = db.Database.GetDbConnection();

            await using (DbCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT ProcessName, SUM(BytesSent) AS Sent, SUM(BytesReceived) AS Received
                    FROM TrafficRollups
                    WHERE MinuteEpoch >= $start AND MinuteEpoch < $end
                    GROUP BY ProcessName
                    """;

                DbParameter startParameter = command.CreateParameter();
                startParameter.ParameterName = "$start";
                startParameter.Value = startEpoch;
                command.Parameters.Add(startParameter);

                DbParameter endParameter = command.CreateParameter();
                endParameter.ParameterName = "$end";
                endParameter.Value = endEpoch;
                command.Parameters.Add(endParameter);

                await using (DbDataReader reader = await command.ExecuteReaderAsync(ct))
                {

                    while (await reader.ReadAsync(ct))
                    {
                        totals.Add(new AppTrafficTotal
                        {
                            ProcessName = reader.GetString(0),
                            BytesSent = reader.GetInt64(1),
                            BytesReceived = reader.GetInt64(2)
                        });
                    }

                }

            }

            return totals;
        }
    }
}
```

- [ ] **Step 2: Register `DigestGenerator` in DI**

In `App.xaml.cs`, in the `ConfigureServices` block (next to the other `AddSingleton` service registrations, before the ViewModels):

```csharp
services.AddSingleton<DigestGenerator>();
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build "NetworkMonitor/NetworkMonitor.csproj" -c Debug -p:Platform=x64 --nologo -v m`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add NetworkMonitor/Services/DigestGenerator.cs NetworkMonitor/App.xaml.cs
git commit -m "Add DigestGenerator EF orchestration."
```

---

### Task 4: `DigestSchedule` (pure scheduling math) — TDD

**Files:**
- Create: `NetworkMonitor/Services/DigestSchedule.cs`
- Modify: `NetworkMonitor.Tests/NetworkMonitor.Tests.csproj` (link the file)
- Test: `NetworkMonitor.Tests/DigestScheduleTests.cs`

**Interfaces:**
- Produces: `static List<(DateTime StartUtc, DateTime EndUtc)> DigestSchedule.MissedWindows(DateTime? lastPeriodEndUtc, DateTime nowLocal, int generationHour, int retentionDays)` — returns each missing daily window (each 24h ending at `generationHour` local, converted to UTC) that should already exist, oldest first, bounded so no window starts earlier than `retentionDays` before `nowLocal`. Returns empty when up to date.
- Produces: `static DateTime NextRunLocal(DateTime nowLocal, int generationHour)` — the next local `generationHour:00:00` strictly after `nowLocal`.

- [ ] **Step 1: Link the file into the test project**

In `NetworkMonitor.Tests/NetworkMonitor.Tests.csproj`:

```xml
<Compile Include="..\NetworkMonitor\Services\DigestSchedule.cs">
  <Link>Linked\DigestSchedule.cs</Link>
</Compile>
```

- [ ] **Step 2: Write the failing tests**

Create `NetworkMonitor.Tests/DigestScheduleTests.cs`:

```csharp
using NetworkMonitor.Services;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class DigestScheduleTests
    {
        [Fact]
        public void NextRunLocal_IsTodayAtHour_WhenBeforeIt()
        {
            DateTime now = new DateTime(2026, 6, 19, 5, 0, 0, DateTimeKind.Local);

            DateTime next = DigestSchedule.NextRunLocal(now, 6);

            Assert.Equal(new DateTime(2026, 6, 19, 6, 0, 0, DateTimeKind.Local), next);
        }

        [Fact]
        public void NextRunLocal_IsTomorrowAtHour_WhenAfterIt()
        {
            DateTime now = new DateTime(2026, 6, 19, 7, 0, 0, DateTimeKind.Local);

            DateTime next = DigestSchedule.NextRunLocal(now, 6);

            Assert.Equal(new DateTime(2026, 6, 20, 6, 0, 0, DateTimeKind.Local), next);
        }

        [Fact]
        public void MissedWindows_EmptyWhenNoneDue()
        {
            DateTime now = new DateTime(2026, 6, 19, 5, 0, 0, DateTimeKind.Local);
            DateTime lastEnd = new DateTime(2026, 6, 18, 6, 0, 0, DateTimeKind.Local).ToUniversalTime();

            List<(DateTime StartUtc, DateTime EndUtc)> windows = DigestSchedule.MissedWindows(lastEnd, now, 6, 90);

            Assert.Empty(windows);
        }

        [Fact]
        public void MissedWindows_ReturnsEachMissedDay_Ordered()
        {
            DateTime now = new DateTime(2026, 6, 19, 7, 0, 0, DateTimeKind.Local);
            DateTime lastEnd = new DateTime(2026, 6, 16, 6, 0, 0, DateTimeKind.Local).ToUniversalTime();

            List<(DateTime StartUtc, DateTime EndUtc)> windows = DigestSchedule.MissedWindows(lastEnd, now, 6, 90);

            Assert.Equal(3, windows.Count);
            Assert.True(windows[0].EndUtc < windows[1].EndUtc);
            Assert.Equal(new DateTime(2026, 6, 19, 6, 0, 0, DateTimeKind.Local).ToUniversalTime(), windows[2].EndUtc);
        }

        [Fact]
        public void MissedWindows_FirstRun_GeneratesOnlyMostRecentDay()
        {
            DateTime now = new DateTime(2026, 6, 19, 7, 0, 0, DateTimeKind.Local);

            List<(DateTime StartUtc, DateTime EndUtc)> windows = DigestSchedule.MissedWindows(null, now, 6, 90);

            Assert.Single(windows);
            Assert.Equal(new DateTime(2026, 6, 19, 6, 0, 0, DateTimeKind.Local).ToUniversalTime(), windows[0].EndUtc);
        }

        [Fact]
        public void MissedWindows_BoundedByRetention()
        {
            DateTime now = new DateTime(2026, 6, 19, 7, 0, 0, DateTimeKind.Local);
            DateTime lastEnd = new DateTime(2025, 1, 1, 6, 0, 0, DateTimeKind.Local).ToUniversalTime();

            List<(DateTime StartUtc, DateTime EndUtc)> windows = DigestSchedule.MissedWindows(lastEnd, now, 6, 90);

            Assert.True(windows.Count <= 90);
        }
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test "NetworkMonitor.Tests/NetworkMonitor.Tests.csproj" --nologo`
Expected: FAIL — `DigestSchedule` does not exist.

- [ ] **Step 4: Implement `DigestSchedule.cs`**

```csharp
namespace NetworkMonitor.Services
{
    public static class DigestSchedule
    {
        public static DateTime NextRunLocal(DateTime nowLocal, int generationHour)
        {
            DateTime todayRun = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, generationHour, 0, 0, DateTimeKind.Local);
            DateTime next = nowLocal < todayRun ? todayRun : todayRun.AddDays(1);

            return next;
        }

        public static List<(DateTime StartUtc, DateTime EndUtc)> MissedWindows(
            DateTime? lastPeriodEndUtc, DateTime nowLocal, int generationHour, int retentionDays)
        {
            List<(DateTime StartUtc, DateTime EndUtc)> windows = new();
            DateTime todayRun = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, generationHour, 0, 0, DateTimeKind.Local);
            DateTime mostRecentBoundaryLocal = nowLocal >= todayRun ? todayRun : todayRun.AddDays(-1);
            DateTime earliestBoundaryLocal = mostRecentBoundaryLocal.AddDays(-(retentionDays - 1));
            DateTime cursorEndLocal;

            if (lastPeriodEndUtc is null)
            {
                cursorEndLocal = mostRecentBoundaryLocal;
            }
            else
            {
                DateTime lastEndLocal = lastPeriodEndUtc.Value.ToLocalTime();
                cursorEndLocal = lastEndLocal.AddDays(1);
            }

            if (cursorEndLocal < earliestBoundaryLocal)
            {
                cursorEndLocal = earliestBoundaryLocal;
            }

            while (cursorEndLocal <= mostRecentBoundaryLocal)
            {
                DateTime startUtc = cursorEndLocal.AddDays(-1).ToUniversalTime();
                DateTime endUtc = cursorEndLocal.ToUniversalTime();
                windows.Add((startUtc, endUtc));
                cursorEndLocal = cursorEndLocal.AddDays(1);
            }

            return windows;
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test "NetworkMonitor.Tests/NetworkMonitor.Tests.csproj" --nologo`
Expected: PASS — the 6 new tests pass (plus all earlier tests).

- [ ] **Step 6: Commit**

```bash
git add NetworkMonitor/Services/DigestSchedule.cs NetworkMonitor.Tests/DigestScheduleTests.cs NetworkMonitor.Tests/NetworkMonitor.Tests.csproj
git commit -m "Add DigestSchedule catch-up math with tests."
```

---

### Task 5: `DigestWorker` (BackgroundService) + DI + retention

**Files:**
- Create: `NetworkMonitor/Services/DigestWorker.cs`
- Modify: `NetworkMonitor/App.xaml.cs` (register as singleton + hosted service)

**Interfaces:**
- Consumes: `DigestGenerator.GenerateAsync(...)`, `DigestSchedule.MissedWindows(...)`, `DigestSchedule.NextRunLocal(...)`, `Settings.DigestGenerationHour`, `Settings.DigestPurgeDays` (added in Task 6), `IDbContextFactory<AppDbContext>`.
- Produces: `DigestWorker.GenerateNowAsync(CancellationToken ct = default) : Task<DigestReport>` — manual generation for the trailing 24h ending now.

> Depends on the `Settings` fields added in Task 6. If executing strictly in order, do Task 6 before building this task (the build step here assumes those fields exist).

- [ ] **Step 1: Create `DigestWorker.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NetworkMonitor.Data;
using NetworkMonitor.Models;

namespace NetworkMonitor.Services
{
    public class DigestWorker(
        DigestGenerator generator,
        Settings settings,
        IDbContextFactory<AppDbContext> dbFactory) : BackgroundService
    {
        public async Task<DigestReport> GenerateNowAsync(CancellationToken ct = default)
        {
            DateTime endUtc = DateTime.UtcNow;
            DateTime startUtc = endUtc.AddDays(-1);
            DigestReport report = await generator.GenerateAsync(startUtc, endUtc, isScheduled: false, ct);

            return report;
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {

            try
            {
                await CatchUpAsync(ct);
                await PurgeOldReportsAsync(ct);
            }
            catch (Exception)
            {
            }

            while (!ct.IsCancellationRequested)
            {

                try
                {
                    DateTime nextRunLocal = DigestSchedule.NextRunLocal(DateTime.Now, settings.DigestGenerationHour);
                    TimeSpan delay = nextRunLocal - DateTime.Now;

                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, ct);
                    }

                    await CatchUpAsync(ct);
                    await PurgeOldReportsAsync(ct);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception)
                {
                }

            }

        }

        private async Task CatchUpAsync(CancellationToken ct)
        {
            DateTime? lastEndUtc = await GetLastPeriodEndUtcAsync(ct);
            List<(DateTime StartUtc, DateTime EndUtc)> windows = DigestSchedule.MissedWindows(
                lastEndUtc, DateTime.Now, settings.DigestGenerationHour, settings.DigestPurgeDays);

            foreach ((DateTime StartUtc, DateTime EndUtc) window in windows)
            {
                await generator.GenerateAsync(window.StartUtc, window.EndUtc, isScheduled: true, ct);
            }

        }

        private async Task<DateTime?> GetLastPeriodEndUtcAsync(CancellationToken ct)
        {
            await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
            DateTime? lastEnd = await db.DigestReports
                .Where(report => report.IsScheduled)
                .OrderByDescending(report => report.PeriodEnd)
                .Select(report => (DateTime?)report.PeriodEnd)
                .FirstOrDefaultAsync(ct);

            return lastEnd;
        }

        private async Task PurgeOldReportsAsync(CancellationToken ct)
        {

            if (settings.DigestPurgeDays > 0)
            {
                await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
                DateTime cutoff = DateTime.UtcNow.AddDays(-settings.DigestPurgeDays);
                await db.DigestReports
                    .Where(report => report.GeneratedAt < cutoff)
                    .ExecuteDeleteAsync(ct);
            }

        }
    }
}
```

- [ ] **Step 2: Register `DigestWorker` in DI**

In `App.xaml.cs`, next to the other workers:

```csharp
services.AddSingleton<DigestWorker>();
services.AddHostedService(sp => sp.GetRequiredService<DigestWorker>());
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build "NetworkMonitor/NetworkMonitor.csproj" -c Debug -p:Platform=x64 --nologo -v m`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add NetworkMonitor/Services/DigestWorker.cs NetworkMonitor/App.xaml.cs
git commit -m "Add DigestWorker scheduling, catch-up, and retention."
```

---

### Task 6: Settings — retention, generation hour, notify toggle

**Files:**
- Modify: `NetworkMonitor/Data/Settings.cs`
- Modify: `NetworkMonitor/ViewModels/SettingsViewModel.cs`
- Modify: `NetworkMonitor/Views/SettingsPage.xaml`

**Interfaces:**
- Produces: `Settings.DigestPurgeDays:int = 90`, `Settings.DigestGenerationHour:int = 6`, `Settings.DigestNotify:bool = true`.
- Produces: `SettingsViewModel.DigestPurgeDays:int`, `SettingsViewModel.DigestGenerationHour:int`, `SettingsViewModel.DigestNotify:bool` (saved by the existing `Save` command).

- [ ] **Step 1: Add the three settings to `Settings.cs`**

Add next to the other auto-properties (use the existing `{ get; set; } = value;` block style in the file):

```csharp
public int DigestPurgeDays
{
    get;
    set;
} = 90;

public int DigestGenerationHour
{
    get;
    set;
} = 6;

public bool DigestNotify
{
    get;
    set;
} = true;
```

- [ ] **Step 2: Add backing fields + properties to `SettingsViewModel.cs`**

In the constructor, initialise the backing fields next to the others:

```csharp
_digestPurgeDays = settings.DigestPurgeDays;
_digestGenerationHour = settings.DigestGenerationHour;
_digestNotify = settings.DigestNotify;
```

Add the properties in the Properties section (each backing field directly above its property):

```csharp
private int _digestPurgeDays;

public int DigestPurgeDays
{
    get => _digestPurgeDays;
    set => SetProperty(ref _digestPurgeDays, value);
}

private int _digestGenerationHour;

public int DigestGenerationHour
{
    get => _digestGenerationHour;
    set => SetProperty(ref _digestGenerationHour, value);
}

private bool _digestNotify;

public bool DigestNotify
{
    get => _digestNotify;
    set => SetProperty(ref _digestNotify, value);
}
```

In the `Save` command, persist them next to the others:

```csharp
_settings.DigestPurgeDays = DigestPurgeDays;
_settings.DigestGenerationHour = DigestGenerationHour;
_settings.DigestNotify = DigestNotify;
```

- [ ] **Step 3: Add the UI to the "Other" tab of `SettingsPage.xaml`**

Inside the `OtherPanel` StackPanel (after the existing toasts/startup blocks), add (follow the file's XAML formatting exactly):

```xml
<StackPanel
    Margin="0,24,0,0"
    Spacing="4">

    <TextBlock
        Text="Daily digest generation time (hour, 0–23)" />

    <NumberBox
        Minimum="0"
        Maximum="23"
        SpinButtonPlacementMode="Inline"
        Value="{x:Bind ViewModel.DigestGenerationHour, Mode=TwoWay}" />

</StackPanel>

<StackPanel
    Spacing="4">

    <TextBlock
        Text="Keep digests for (days)" />

    <NumberBox
        Minimum="1"
        Maximum="3650"
        SpinButtonPlacementMode="Inline"
        Value="{x:Bind ViewModel.DigestPurgeDays, Mode=TwoWay}" />

</StackPanel>

<ToggleSwitch
    Header="Notify when a daily digest is ready"
    IsOn="{x:Bind ViewModel.DigestNotify, Mode=TwoWay}" />
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build "NetworkMonitor/NetworkMonitor.csproj" -c Debug -p:Platform=x64 --nologo -v m`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor/Data/Settings.cs NetworkMonitor/ViewModels/SettingsViewModel.cs NetworkMonitor/Views/SettingsPage.xaml
git commit -m "Add daily digest settings (generation hour, retention, notify)."
```

---

### Task 7: `DigestChartRenderer` (Win2D → PNG)

**Files:**
- Create: `NetworkMonitor/Services/DigestChartRenderer.cs`
- Modify: `NetworkMonitor/App.xaml.cs` (register the service)

**Interfaces:**
- Consumes: `DigestSummary`, `DeviceType`, `Microsoft.Graphics.Canvas` (Win2D).
- Produces: `DigestChartRenderer.RenderTrafficChart(DigestSummary):byte[]`, `RenderNewDevicesChart(DigestSummary):byte[]`, `RenderActivityChart(DigestSummary):byte[]`, `RenderUnknownChart(DigestSummary):byte[]` — each returns PNG bytes (840×360 px). All four delegate to a shared `RenderBars(IReadOnlyList<(string Label, double Value)>, string title)` helper for consistency.

- [ ] **Step 1: Create `DigestChartRenderer.cs`**

```csharp
using System.IO;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI;
using NetworkMonitor.Models;

namespace NetworkMonitor.Services
{
    public class DigestChartRenderer
    {
        private const float ChartWidth = 840f;
        private const float ChartHeight = 360f;

        public byte[] RenderTrafficChart(DigestSummary summary)
        {
            List<(string Label, double Value)> bars = summary.TopApps
                .Select(app => (app.ProcessName, (double)(app.BytesSent + app.BytesReceived)))
                .ToList();
            byte[] png = RenderBars(bars, "Top apps by traffic (bytes)");

            return png;
        }

        public byte[] RenderNewDevicesChart(DigestSummary summary)
        {
            List<(string Label, double Value)> bars = summary.NewDevices
                .GroupBy(device => device.Type)
                .Select(group => (group.Key.ToString(), (double)group.Count()))
                .ToList();
            byte[] png = RenderBars(bars, "New devices by type");

            return png;
        }

        public byte[] RenderActivityChart(DigestSummary summary)
        {
            List<(string Label, double Value)> bars = summary.HourlyActivity
                .Select(hour => (hour.Hour.ToString("D2"), (double)(hour.Appeared + hour.Disappeared)))
                .ToList();
            byte[] png = RenderBars(bars, "Device activity by hour (appeared + disappeared)");

            return png;
        }

        public byte[] RenderUnknownChart(DigestSummary summary)
        {
            List<(string Label, double Value)> bars = summary.UnknownDevices
                .GroupBy(device => device.Type)
                .Select(group => (group.Key.ToString(), (double)group.Count()))
                .ToList();
            byte[] png = RenderBars(bars, "Unknown devices by type");

            return png;
        }

        private static byte[] RenderBars(IReadOnlyList<(string Label, double Value)> bars, string title)
        {
            CanvasDevice device = CanvasDevice.GetSharedDevice();
            byte[] result;

            using (CanvasRenderTarget target = new CanvasRenderTarget(device, ChartWidth, ChartHeight, 96f))
            {

                using (CanvasDrawingSession session = target.CreateDrawingSession())
                {
                    session.Clear(Colors.White);

                    CanvasTextFormat titleFormat = new CanvasTextFormat { FontSize = 18f, FontWeight = Windows.UI.Text.FontWeights.SemiBold };
                    session.DrawText(title, 16f, 12f, Color.FromArgb(255, 32, 32, 32), titleFormat);

                    double maxValue = bars.Count == 0 ? 0 : bars.Max(bar => bar.Value);

                    if (maxValue <= 0)
                    {
                        CanvasTextFormat emptyFormat = new CanvasTextFormat { FontSize = 14f };
                        session.DrawText("No data", 16f, 60f, Color.FromArgb(255, 120, 120, 120), emptyFormat);
                    }
                    else
                    {
                        float plotTop = 52f;
                        float plotBottom = ChartHeight - 28f;
                        float plotHeight = plotBottom - plotTop;
                        float slotWidth = (ChartWidth - 32f) / bars.Count;
                        float barWidth = slotWidth * 0.6f;
                        Color barColour = Color.FromArgb(255, 0, 120, 215);
                        CanvasTextFormat labelFormat = new CanvasTextFormat { FontSize = 11f, HorizontalAlignment = CanvasHorizontalAlignment.Center };

                        for (int barIndex = 0; barIndex < bars.Count; barIndex++)
                        {
                            (string Label, double Value) bar = bars[barIndex];
                            float barHeight = (float)(bar.Value / maxValue) * plotHeight;
                            float left = 16f + barIndex * slotWidth + (slotWidth - barWidth) / 2f;
                            float top = plotBottom - barHeight;
                            session.FillRectangle(left, top, barWidth, barHeight, barColour);
                            session.DrawText(bar.Label, 16f + barIndex * slotWidth, plotBottom + 4f, slotWidth, 20f, Color.FromArgb(255, 90, 90, 90), labelFormat);
                        }

                    }

                }

                using (MemoryStream stream = new MemoryStream())
                {
                    target.SaveAsync(stream.AsRandomAccessStream(), CanvasBitmapFileFormat.Png).AsTask().GetAwaiter().GetResult();
                    result = stream.ToArray();
                }

            }

            return result;
        }
    }
}
```

- [ ] **Step 2: Register `DigestChartRenderer` in DI**

In `App.xaml.cs`:

```csharp
services.AddSingleton<DigestChartRenderer>();
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build "NetworkMonitor/NetworkMonitor.csproj" -c Debug -p:Platform=x64 --nologo -v m`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add NetworkMonitor/Services/DigestChartRenderer.cs NetworkMonitor/App.xaml.cs
git commit -m "Add DigestChartRenderer (Win2D charts to PNG)."
```

---

### Task 8: `DigestCsvExporter` (pure) — TDD

**Files:**
- Create: `NetworkMonitor/Services/DigestCsvExporter.cs`
- Modify: `NetworkMonitor.Tests/NetworkMonitor.Tests.csproj` (link the file)
- Test: `NetworkMonitor.Tests/DigestCsvExporterTests.cs`

**Interfaces:**
- Consumes: `DigestSummary`, `TrafficRateFormatter` (existing, already linked) is NOT used here — emit raw byte counts.
- Produces: `static string DigestCsvExporter.BuildCsv(DigestSummary summary)` — returns the full CSV text with labelled section blocks in canonical order (Headline, Traffic top apps, New devices, Activity, Unknown present). Each value quoted with `"` and internal quotes doubled.

- [ ] **Step 1: Link the file into the test project**

```xml
<Compile Include="..\NetworkMonitor\Services\DigestCsvExporter.cs">
  <Link>Linked\DigestCsvExporter.cs</Link>
</Compile>
```

- [ ] **Step 2: Write the failing tests**

Create `NetworkMonitor.Tests/DigestCsvExporterTests.cs`:

```csharp
using NetworkMonitor.Models;
using NetworkMonitor.Services;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class DigestCsvExporterTests
    {
        [Fact]
        public void BuildCsv_IncludesAllSectionHeadersInOrder()
        {
            DigestSummary summary = new DigestSummary { Headline = "test headline" };

            string csv = DigestCsvExporter.BuildCsv(summary);

            int trafficIndex = csv.IndexOf("Top Apps", StringComparison.Ordinal);
            int newDevicesIndex = csv.IndexOf("New Devices", StringComparison.Ordinal);
            int activityIndex = csv.IndexOf("Activity", StringComparison.Ordinal);
            int unknownIndex = csv.IndexOf("Unknown Devices", StringComparison.Ordinal);

            Assert.True(trafficIndex >= 0);
            Assert.True(trafficIndex < newDevicesIndex);
            Assert.True(newDevicesIndex < activityIndex);
            Assert.True(activityIndex < unknownIndex);
        }

        [Fact]
        public void BuildCsv_EscapesQuotesInValues()
        {
            DigestSummary summary = new DigestSummary();
            summary.TopApps.Add(new TrafficAppSummary { ProcessName = "we\"ird", BytesSent = 1, BytesReceived = 2 });

            string csv = DigestCsvExporter.BuildCsv(summary);

            Assert.Contains("\"we\"\"ird\"", csv);
        }
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test "NetworkMonitor.Tests/NetworkMonitor.Tests.csproj" --nologo`
Expected: FAIL — `DigestCsvExporter` does not exist.

- [ ] **Step 4: Implement `DigestCsvExporter.cs`**

```csharp
using System.Text;
using NetworkMonitor.Models;

namespace NetworkMonitor.Services
{
    public static class DigestCsvExporter
    {
        public static string BuildCsv(DigestSummary summary)
        {
            StringBuilder builder = new StringBuilder();

            builder.AppendLine($"Headline,{Quote(summary.Headline)}");
            builder.AppendLine();

            builder.AppendLine("Top Apps");
            builder.AppendLine("Process,BytesSent,BytesReceived");

            foreach (TrafficAppSummary app in summary.TopApps)
            {
                builder.AppendLine($"{Quote(app.ProcessName)},{app.BytesSent},{app.BytesReceived}");
            }

            builder.AppendLine();

            builder.AppendLine("New Devices");
            builder.AppendLine("Name,Mac,Ip,Vendor,Type,Known,FirstSeen");

            foreach (NewDeviceSummary device in summary.NewDevices)
            {
                builder.AppendLine($"{Quote(device.DisplayName)},{Quote(device.MacAddress)},{Quote(device.IpAddress)},{Quote(device.Vendor)},{device.Type},{device.IsKnown},{device.FirstSeen:u}");
            }

            builder.AppendLine();

            builder.AppendLine("Activity");
            builder.AppendLine($"Appeared,{summary.AppearedCount}");
            builder.AppendLine($"Disappeared,{summary.DisappearedCount}");
            builder.AppendLine($"Online,{summary.OnlineCount}");
            builder.AppendLine($"Offline,{summary.OfflineCount}");
            builder.AppendLine();

            builder.AppendLine("Unknown Devices");
            builder.AppendLine("Name,Mac,Ip,Vendor,Type");

            foreach (UnknownDeviceSummary device in summary.UnknownDevices)
            {
                builder.AppendLine($"{Quote(device.DisplayName)},{Quote(device.MacAddress)},{Quote(device.IpAddress)},{Quote(device.Vendor)},{device.Type}");
            }

            string csv = builder.ToString();

            return csv;
        }

        private static string Quote(string value)
        {
            string escaped = value.Replace("\"", "\"\"");
            string quoted = $"\"{escaped}\"";

            return quoted;
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test "NetworkMonitor.Tests/NetworkMonitor.Tests.csproj" --nologo`
Expected: PASS — the 2 new tests pass.

- [ ] **Step 6: Commit**

```bash
git add NetworkMonitor/Services/DigestCsvExporter.cs NetworkMonitor.Tests/DigestCsvExporterTests.cs NetworkMonitor.Tests/NetworkMonitor.Tests.csproj
git commit -m "Add DigestCsvExporter with tests."
```

---

### Task 9: QuestPDF package + `DigestPdfExporter`

**Files:**
- Modify: `NetworkMonitor/NetworkMonitor.csproj` (add QuestPDF)
- Modify: `NetworkMonitor/App.xaml.cs` (set the Community license once at startup; register the exporter)
- Create: `NetworkMonitor/Services/DigestPdfExporter.cs`

**Interfaces:**
- Consumes: `DigestSummary`, `DigestChartRenderer` (Task 7), `QuestPDF`.
- Produces: `DigestPdfExporter.BuildPdf(DigestSummary summary):byte[]` — composes the report (headline, then chart image + table for each section in canonical order) and returns PDF bytes.

- [ ] **Step 1: Add the QuestPDF package**

First find the latest stable version: `dotnet package search QuestPDF --take 1`. Then add it to the package `<ItemGroup>` in `NetworkMonitor.csproj`, pinning the exact version reported (QuestPDF uses CalVer `YYYY.MM.x`; do not wildcard the major/year). Example shape (replace with the actual latest stable):

```xml
<PackageReference Include="QuestPDF" Version="REPLACE_WITH_LATEST_STABLE" />
```

- [ ] **Step 2: Set the Community license at startup**

In `App.xaml.cs` constructor, before `AppHost` is built (after `InitializeComponent();`), add:

```csharp
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
```

- [ ] **Step 3: Create `DigestPdfExporter.cs`**

```csharp
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using NetworkMonitor.Models;

namespace NetworkMonitor.Services
{
    public class DigestPdfExporter(DigestChartRenderer chartRenderer)
    {
        public byte[] BuildPdf(DigestSummary summary)
        {
            byte[] trafficChart = chartRenderer.RenderTrafficChart(summary);
            byte[] newDevicesChart = chartRenderer.RenderNewDevicesChart(summary);
            byte[] activityChart = chartRenderer.RenderActivityChart(summary);
            byte[] unknownChart = chartRenderer.RenderUnknownChart(summary);

            byte[] pdf = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(36);
                    page.Size(PageSizes.A4);
                    page.DefaultTextStyle(style => style.FontSize(11));

                    page.Header().Text(summary.Headline).FontSize(16).SemiBold();

                    page.Content().PaddingTop(12).Column(column =>
                    {
                        column.Spacing(16);

                        column.Item().Image(trafficChart);
                        column.Item().Text("Top apps by traffic").SemiBold();

                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });

                            table.Header(header =>
                            {
                                header.Cell().Text("Process").SemiBold();
                                header.Cell().AlignRight().Text("Sent").SemiBold();
                                header.Cell().AlignRight().Text("Received").SemiBold();
                            });

                            foreach (TrafficAppSummary app in summary.TopApps)
                            {
                                table.Cell().Text(app.ProcessName);
                                table.Cell().AlignRight().Text(app.BytesSent.ToString("N0"));
                                table.Cell().AlignRight().Text(app.BytesReceived.ToString("N0"));
                            }

                        });

                        column.Item().Image(newDevicesChart);
                        column.Item().Text($"New devices ({summary.NewDevices.Count})").SemiBold();

                        column.Item().Image(activityChart);
                        column.Item().Text($"Activity — {summary.AppearedCount} appeared, {summary.DisappearedCount} disappeared, {summary.OnlineCount} online, {summary.OfflineCount} offline").SemiBold();

                        column.Item().Image(unknownChart);
                        column.Item().Text($"Unknown devices present ({summary.UnknownDevices.Count})").SemiBold();
                    });

                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.Span("Umnatha Network Monitor — generated ");
                        text.Span(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                    });
                });
            }).GeneratePdf();

            return pdf;
        }
    }
}
```

- [ ] **Step 4: Register `DigestPdfExporter` in DI**

In `App.xaml.cs`:

```csharp
services.AddSingleton<DigestPdfExporter>();
```

- [ ] **Step 5: Restore + build to verify**

Run: `dotnet restore "NetworkMonitor/NetworkMonitor.csproj"` then `dotnet build "NetworkMonitor/NetworkMonitor.csproj" -c Debug -p:Platform=x64 --nologo -v m`
Expected: `Build succeeded. 0 Error(s)`. (The pinned QuestPDF version must be the latest stable from Step 1; the `LicenseType.Community`, `Document.Create(...).GeneratePdf()`, `.Image(byte[])`, and `.Table(...)` APIs used here are stable across recent QuestPDF releases.)

- [ ] **Step 6: Commit**

```bash
git add NetworkMonitor/NetworkMonitor.csproj NetworkMonitor/App.xaml.cs NetworkMonitor/Services/DigestPdfExporter.cs
git commit -m "Add QuestPDF and DigestPdfExporter."
```

---

### Task 10: `ReportsViewModel` + `ReportsPage` + nav item

**Files:**
- Create: `NetworkMonitor/Views/Controls/DigestReportView.xaml`
- Create: `NetworkMonitor/Views/Controls/DigestReportView.xaml.cs`
- Create: `NetworkMonitor/ViewModels/ReportsViewModel.cs`
- Create: `NetworkMonitor/Views/ReportsPage.xaml`
- Create: `NetworkMonitor/Views/ReportsPage.xaml.cs`
- Modify: `NetworkMonitor/App.xaml.cs` (register the ViewModel)
- Modify: `NetworkMonitor/MainWindow.xaml` (add NavigationView item)
- Modify: `NetworkMonitor/MainWindow.xaml.cs` (route to `ReportsPage`)

**UI shape:** the page is a `SelectorBar` (the same in-page tab idiom as `SettingsPage`) over two panels whose `Visibility` is toggled in code-behind:
- **"Daily Digest"** tab — renders the **latest** report via a shared `DigestReportView`; toolbar = `Generate now`, `Export PDF`, `Export CSV` (all act on the latest report).
- **"History"** tab — master/detail: a `ListView` of all reports (newest first) on the left, the selected report rendered via a second `DigestReportView` on the right; toolbar = `Export PDF`, `Export CSV`, `Delete` (all act on the selected history report).

The headline-banner + four-chart render is factored into a reusable `DigestReportView` UserControl (a `DigestSummary` dependency property) so both tabs share one implementation and the chart-to-bitmap code lives in exactly one place.

**Interfaces:**
- Produces: `DigestReportView` UserControl exposing a `DigestSummary? Summary` dependency property; on change it renders the headline + four chart `Image`s from the snapshot (resolves `DigestChartRenderer` from `App.AppHost.Services`).
- Consumes: `IDbContextFactory<AppDbContext>`, `DigestWorker.GenerateNowAsync(...)`, `DigestPdfExporter.BuildPdf(...)`, `DigestCsvExporter.BuildCsv(...)`, `DigestReport`, `DigestSummary`.
- Produces: `ReportsViewModel` with `Reports:ObservableCollection<DigestReport>`, `LatestReport:DigestReport?`, `LatestSummary:DigestSummary?`, `SelectedHistoryReport:DigestReport?`, `SelectedHistorySummary:DigestSummary?`, `LoadAsync():Task`, `GenerateNowCommand`, `DeleteCommand`, and `byte[] BuildPdf(DigestSummary? summary)` / `string BuildCsv(DigestSummary? summary)` for the page's save handlers.

- [ ] **Step 1: Create the shared `DigestReportView` control**

Create `NetworkMonitor/Views/Controls/DigestReportView.xaml`:

```xml
<?xml version="1.0" encoding="utf-8"?>

<UserControl
    x:Class="NetworkMonitor.Views.Controls.DigestReportView"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <StackPanel
        Spacing="12">

        <TextBlock
            x:Name="HeadlineText"
            FontSize="18"
            FontWeight="SemiBold"
            TextWrapping="Wrap" />

        <Image
            x:Name="TrafficChartImage"
            Stretch="Uniform" />

        <Image
            x:Name="NewDevicesChartImage"
            Stretch="Uniform" />

        <Image
            x:Name="ActivityChartImage"
            Stretch="Uniform" />

        <Image
            x:Name="UnknownChartImage"
            Stretch="Uniform" />

    </StackPanel>

</UserControl>
```

Create `NetworkMonitor/Views/Controls/DigestReportView.xaml.cs`:

```csharp
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using NetworkMonitor.Models;
using NetworkMonitor.Services;
using Windows.Storage.Streams;

namespace NetworkMonitor.Views.Controls
{
    public sealed partial class DigestReportView : UserControl
    {
        public static readonly DependencyProperty SummaryProperty = DependencyProperty.Register(
            nameof(Summary),
            typeof(DigestSummary),
            typeof(DigestReportView),
            new PropertyMetadata(null, OnSummaryChanged));

        private readonly DigestChartRenderer _chartRenderer;

        public DigestReportView()
        {
            _chartRenderer = App.AppHost.Services.GetRequiredService<DigestChartRenderer>();
            InitializeComponent();
        }

        public DigestSummary? Summary
        {
            get => (DigestSummary?)GetValue(SummaryProperty);
            set => SetValue(SummaryProperty, value);
        }

        private static void OnSummaryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {

            if (sender is DigestReportView view)
            {
                view.Render();
            }

        }

        private void Render()
        {
            DigestSummary? summary = Summary;

            if (summary is null)
            {
                HeadlineText.Text = string.Empty;
                TrafficChartImage.Source = null;
                NewDevicesChartImage.Source = null;
                ActivityChartImage.Source = null;
                UnknownChartImage.Source = null;
            }
            else
            {
                HeadlineText.Text = summary.Headline;
                TrafficChartImage.Source = ToBitmap(_chartRenderer.RenderTrafficChart(summary));
                NewDevicesChartImage.Source = ToBitmap(_chartRenderer.RenderNewDevicesChart(summary));
                ActivityChartImage.Source = ToBitmap(_chartRenderer.RenderActivityChart(summary));
                UnknownChartImage.Source = ToBitmap(_chartRenderer.RenderUnknownChart(summary));
            }

        }

        private static BitmapImage ToBitmap(byte[] png)
        {
            BitmapImage image = new BitmapImage();

            using (InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream())
            {
                stream.WriteAsync(png.AsBuffer()).AsTask().GetAwaiter().GetResult();
                stream.Seek(0);
                image.SetSource(stream);
            }

            return image;
        }
    }
}
```

- [ ] **Step 2: Create `ReportsViewModel.cs`**

```csharp
using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Dispatching;
using NetworkMonitor.Data;
using NetworkMonitor.Models;
using NetworkMonitor.Services;

namespace NetworkMonitor.ViewModels
{
    public partial class ReportsViewModel : ObservableObject
    {
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly DigestWorker _digestWorker;
        private readonly DigestPdfExporter _pdfExporter;

        public ReportsViewModel(
            IDbContextFactory<AppDbContext> dbFactory,
            DigestWorker digestWorker,
            DigestPdfExporter pdfExporter)
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _dbFactory = dbFactory;
            _digestWorker = digestWorker;
            _pdfExporter = pdfExporter;
        }

        private ObservableCollection<DigestReport> _reports = new();

        public ObservableCollection<DigestReport> Reports
        {
            get => _reports;
            set => SetProperty(ref _reports, value);
        }

        private DigestReport? _latestReport;

        public DigestReport? LatestReport
        {
            get => _latestReport;
            set
            {

                if (SetProperty(ref _latestReport, value))
                {
                    LatestSummary = Deserialize(value);
                }

            }
        }

        private DigestSummary? _latestSummary;

        public DigestSummary? LatestSummary
        {
            get => _latestSummary;
            set => SetProperty(ref _latestSummary, value);
        }

        private DigestReport? _selectedHistoryReport;

        public DigestReport? SelectedHistoryReport
        {
            get => _selectedHistoryReport;
            set
            {

                if (SetProperty(ref _selectedHistoryReport, value))
                {
                    SelectedHistorySummary = Deserialize(value);
                }

            }
        }

        private DigestSummary? _selectedHistorySummary;

        public DigestSummary? SelectedHistorySummary
        {
            get => _selectedHistorySummary;
            set => SetProperty(ref _selectedHistorySummary, value);
        }

        public async Task LoadAsync()
        {
            await using AppDbContext db = await _dbFactory.CreateDbContextAsync();
            List<DigestReport> reports = await db.DigestReports
                .OrderByDescending(report => report.PeriodEnd)
                .ToListAsync();

            _dispatcherQueue.TryEnqueue(() =>
            {
                Reports = new ObservableCollection<DigestReport>(reports);
                DigestReport? newest = Reports.Count > 0 ? Reports[0] : null;
                LatestReport = newest;
                SelectedHistoryReport = newest;
            });
        }

        public byte[] BuildPdf(DigestSummary? summary)
        {
            byte[] pdf = summary is null ? Array.Empty<byte>() : _pdfExporter.BuildPdf(summary);

            return pdf;
        }

        public string BuildCsv(DigestSummary? summary)
        {
            string csv = summary is null ? string.Empty : DigestCsvExporter.BuildCsv(summary);

            return csv;
        }

        [RelayCommand]
        private async Task GenerateNowAsync()
        {
            await _digestWorker.GenerateNowAsync();
            await LoadAsync();
        }

        [RelayCommand]
        private async Task DeleteAsync()
        {

            if (SelectedHistoryReport is not null)
            {
                await using AppDbContext db = await _dbFactory.CreateDbContextAsync();
                await db.DigestReports
                    .Where(report => report.Id == SelectedHistoryReport.Id)
                    .ExecuteDeleteAsync();
                await LoadAsync();
            }

        }

        private static DigestSummary? Deserialize(DigestReport? report)
        {
            DigestSummary? summary = report is null ? null : JsonSerializer.Deserialize<DigestSummary>(report.SummaryJson);

            return summary;
        }
    }
}
```

- [ ] **Step 3: Create `ReportsPage.xaml`**

```xml
<?xml version="1.0" encoding="utf-8"?>

<Page
    x:Class="NetworkMonitor.Views.ReportsPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:controls="using:NetworkMonitor.Views.Controls"
    Background="{ThemeResource ApplicationPageBackgroundThemeBrush}">

    <Grid
        RowDefinitions="Auto,*">

        <SelectorBar
            Grid.Row="0"
            x:Name="TabBar"
            FontSize="13"
            Margin="24,12,24,0"
            SelectionChanged="TabBarSelectionChanged">

            <SelectorBarItem
                Tag="Digest"
                Text="Daily Digest" />

            <SelectorBarItem
                Tag="History"
                Text="History" />

        </SelectorBar>

        <Grid
            Grid.Row="1"
            x:Name="DigestPanel"
            RowDefinitions="Auto,*">

            <StackPanel
                Grid.Row="0"
                Margin="16,8,16,0"
                Orientation="Horizontal"
                Spacing="8">

                <Button
                    Content="Generate now"
                    Command="{x:Bind ViewModel.GenerateNowCommand}" />

                <Button
                    Content="Export PDF"
                    Click="ExportDigestPdfClick" />

                <Button
                    Content="Export CSV"
                    Click="ExportDigestCsvClick" />

            </StackPanel>

            <ScrollViewer
                Grid.Row="1"
                Padding="16">

                <controls:DigestReportView
                    Summary="{x:Bind ViewModel.LatestSummary, Mode=OneWay}" />

            </ScrollViewer>

        </Grid>

        <Grid
            Grid.Row="1"
            x:Name="HistoryPanel"
            ColumnDefinitions="280,*"
            Visibility="Collapsed">

            <ListView
                Grid.Column="0"
                x:Name="HistoryList"
                ItemsSource="{x:Bind ViewModel.Reports, Mode=OneWay}"
                SelectedItem="{x:Bind ViewModel.SelectedHistoryReport, Mode=TwoWay}">

                <ListView.ItemTemplate>

                    <DataTemplate
                        x:DataType="models:DigestReport"
                        xmlns:models="using:NetworkMonitor.Models">

                        <StackPanel
                            Padding="4"
                            Spacing="2">

                            <TextBlock
                                FontWeight="SemiBold"
                                Text="{x:Bind PeriodEnd}" />

                            <TextBlock
                                FontSize="12"
                                Opacity="0.7"
                                TextWrapping="Wrap"
                                Text="{x:Bind Headline}" />

                        </StackPanel>

                    </DataTemplate>

                </ListView.ItemTemplate>

            </ListView>

            <Grid
                Grid.Column="1"
                RowDefinitions="Auto,*">

                <StackPanel
                    Grid.Row="0"
                    Margin="16,8,16,0"
                    Orientation="Horizontal"
                    Spacing="8">

                    <Button
                        Content="Export PDF"
                        Click="ExportHistoryPdfClick" />

                    <Button
                        Content="Export CSV"
                        Click="ExportHistoryCsvClick" />

                    <Button
                        Content="Delete"
                        Command="{x:Bind ViewModel.DeleteCommand}" />

                </StackPanel>

                <ScrollViewer
                    Grid.Row="1"
                    Padding="16">

                    <controls:DigestReportView
                        Summary="{x:Bind ViewModel.SelectedHistorySummary, Mode=OneWay}" />

                </ScrollViewer>

            </Grid>

        </Grid>

    </Grid>

</Page>
```

- [ ] **Step 4: Create `ReportsPage.xaml.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NetworkMonitor.ViewModels;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace NetworkMonitor.Views
{
    public sealed partial class ReportsPage : Page
    {
        public ReportsPage()
        {
            ViewModel = App.AppHost.Services.GetRequiredService<ReportsViewModel>();
            InitializeComponent();
            TabBar.SelectedItem = TabBar.Items[0];
            Loaded += ReportsPageLoaded;
        }

        public ReportsViewModel ViewModel
        {
            get;
        }

        private async void ReportsPageLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs args)
        {
            await ViewModel.LoadAsync();
        }

        private void TabBarSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {

            if (sender.SelectedItem is not null)
            {
                string selectedTag = (string)sender.SelectedItem.Tag;
                DigestPanel.Visibility = selectedTag == "Digest" ? Visibility.Visible : Visibility.Collapsed;
                HistoryPanel.Visibility = selectedTag == "History" ? Visibility.Visible : Visibility.Collapsed;
            }

        }

        private async void ExportDigestPdfClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs args)
        {
            await SaveBytesAsync(ViewModel.BuildPdf(ViewModel.LatestSummary), ".pdf", "PDF document");
        }

        private async void ExportDigestCsvClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs args)
        {
            byte[] csvBytes = System.Text.Encoding.UTF8.GetBytes(ViewModel.BuildCsv(ViewModel.LatestSummary));
            await SaveBytesAsync(csvBytes, ".csv", "CSV file");
        }

        private async void ExportHistoryPdfClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs args)
        {
            await SaveBytesAsync(ViewModel.BuildPdf(ViewModel.SelectedHistorySummary), ".pdf", "PDF document");
        }

        private async void ExportHistoryCsvClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs args)
        {
            byte[] csvBytes = System.Text.Encoding.UTF8.GetBytes(ViewModel.BuildCsv(ViewModel.SelectedHistorySummary));
            await SaveBytesAsync(csvBytes, ".csv", "CSV file");
        }

        private async Task SaveBytesAsync(byte[] data, string extension, string description)
        {

            if (data.Length > 0)
            {
                FileSavePicker picker = new FileSavePicker();
                picker.SuggestedFileName = $"Umnatha Digest {DateTime.Now:yyyy-MM-dd}";
                picker.FileTypeChoices.Add(description, new List<string> { extension });
                InitializeWithWindow.Initialize(picker, MainWindow.Current is null ? IntPtr.Zero : WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Current));
                StorageFile file = await picker.PickSaveFileAsync();

                if (file is not null)
                {
                    await FileIO.WriteBytesAsync(file, data);
                }

            }

        }
    }
}
```

- [ ] **Step 5: Register the ViewModel in DI**

In `App.xaml.cs` (next to the other ViewModels):

```csharp
services.AddTransient<ReportsViewModel>();
```

- [ ] **Step 6: Add the NavigationView item + route**

In `MainWindow.xaml`, add a `NavigationViewItem` (follow the existing items' formatting, with the appropriate icon) with `Tag="reports"` and content "Reports".

In `MainWindow.xaml.cs` `NavViewSelectionChanged`, add to the `switch` on the tag:

```csharp
"reports" => typeof(ReportsPage),
```

- [ ] **Step 7: Build to verify**

Run: `dotnet build "NetworkMonitor/NetworkMonitor.csproj" -c Debug -p:Platform=x64 --nologo -v m`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 8: Commit**

```bash
git add NetworkMonitor/Views/Controls/DigestReportView.xaml NetworkMonitor/Views/Controls/DigestReportView.xaml.cs NetworkMonitor/ViewModels/ReportsViewModel.cs NetworkMonitor/Views/ReportsPage.xaml NetworkMonitor/Views/ReportsPage.xaml.cs NetworkMonitor/App.xaml.cs NetworkMonitor/MainWindow.xaml NetworkMonitor/MainWindow.xaml.cs
git commit -m "Add Reports page (Daily Digest + History tabs) with PDF/CSV export."
```

---

### Task 11: Digest-ready toast notification

**Files:**
- Modify: `NetworkMonitor/MainWindow.xaml.cs` (subscribe to `DigestGenerator.ReportGenerated`, show a toast gated by `Settings.DigestNotify`)

**Interfaces:**
- Consumes: `DigestGenerator.ReportGenerated` event, `Settings.DigestNotify`, `DigestReport.Headline`, the existing toast helper pattern in `MainWindow`.

- [ ] **Step 1: Inject `DigestGenerator` + `Settings` access and subscribe**

`MainWindow` already receives `Settings` in its constructor. Resolve `DigestGenerator` from `App.AppHost.Services` in the constructor and subscribe:

```csharp
DigestGenerator digestGenerator = App.AppHost.Services.GetRequiredService<DigestGenerator>();
digestGenerator.ReportGenerated += OnDigestReportGenerated;
```

Add the handler (modelled on the existing `ShowNotification` toast XML, marshalled onto the UI thread):

```csharp
private void OnDigestReportGenerated(object? sender, DigestReport report)
{

    if (_settings.DigestNotify)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            XmlDocument toastXml = new XmlDocument();
            toastXml.LoadXml("<toast><visual><binding template=\"ToastGeneric\"><text id=\"1\"/><text id=\"2\"/></binding></visual><audio silent=\"true\"/></toast>");
            XmlNodeList textNodes = toastXml.GetElementsByTagName("text");
            textNodes[0].InnerText = "Daily digest ready";
            textNodes[1].InnerText = report.Headline;
            ToastNotification toastNotification = new ToastNotification(toastXml);
            toastNotification.ExpirationTime = DateTimeOffset.Now.AddMinutes(10);
            ToastNotificationManager.CreateToastNotifier(App.Aumid).Show(toastNotification);
        });
    }

}
```

Add `using Microsoft.Extensions.DependencyInjection;` if not present.

- [ ] **Step 2: Build to verify**

Run: `dotnet build "NetworkMonitor/NetworkMonitor.csproj" -c Debug -p:Platform=x64 --nologo -v m`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add NetworkMonitor/MainWindow.xaml.cs
git commit -m "Add digest-ready toast notification."
```

---

### Task 12: End-to-end verification

**Files:** none (manual verification + run all tests).

- [ ] **Step 1: Run all unit tests**

Run: `dotnet test "NetworkMonitor.Tests/NetworkMonitor.Tests.csproj" --nologo`
Expected: PASS — every pre-existing test plus the new builder/schedule/csv tests (no regressions; compare against the baseline count captured before Task 2, don't assume a fixed number).

- [ ] **Step 2: Launch the app and verify the Reports feature**

- Open the app (close any running instance first so the build can copy the exe).
- Navigate to **Reports** → **Daily Digest** tab. Click **Generate now** → the latest report renders with the headline and four charts.
- On the **Daily Digest** tab, click **Export PDF** → save → open the PDF; confirm headline, four charts, and the traffic table render in order (Traffic, New devices, Activity, Unknown).
- Click **Export CSV** → save → open; confirm the labelled section blocks in canonical order.
- Switch to the **History** tab → the report list shows newest-first; selecting a row renders that report on the right. Confirm **Export PDF**/**Export CSV** export the selected report.
- In **Settings → Other**, change the generation hour / retention / notify toggle and **Save**; confirm no errors.
- On the **History** tab, click **Delete** on a report → it disappears from the list.

- [ ] **Step 3: Final commit (if any fix-ups were needed)**

```bash
git add -A
git commit -m "Finalise daily digest feature."
```

---

## Notes on conventions for the implementer

- Match the surrounding code's `SetProperty` property style and the strict blank-line rules in **Global Constraints** — they are enforced in this codebase.
- When adding the NavigationView item and Settings UI, copy the exact XAML formatting from neighbouring elements (`DevicesPage.xaml` is the canonical reference).
- Do not bump the app `<Version>` — that happens only when building the installer.

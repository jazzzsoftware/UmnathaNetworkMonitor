# Speed Test Feature Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Periodically measure internet speed (download/upload/latency/jitter) against Cloudflare's free endpoints, store results, and surface them on a new Speed Test tab under the Traffic menu with a trend chart, history grid, manual run, CSV export, and completion notifications.

**Architecture:** A `SpeedTestService` performs one Cloudflare measurement via `HttpClient`. A `SpeedTestWorker` `BackgroundService` runs it hourly (gated by `SpeedTestEnabled`) plus on demand, saves `SpeedTestResult` rows, and raises `SpeedTestCompleted`. The "Traffic" nav item becomes a `TrafficHostPage` with **Traffic** and **Speed Test** tabs (mirroring `DevicesHostPage`); the Speed Test tab shows a new lightweight `SpeedTrendChart`, a latest tile, a Run Speed Test button, a history grid, and CSV export. Purge folds into the existing `ScanWorker` traffic purge.

**Tech Stack:** WinUI 3 (Windows App SDK), .NET 10, CommunityToolkit.Mvvm, CommunityToolkit.WinUI DataGrid, EF Core 10 + SQLite (`EnsureCreated`, no migrations), Microsoft.Extensions.Hosting `BackgroundService`, xUnit for tests.

## Global Constraints

- **Platform x64** — WinUI 3 does not support Any CPU. Build/run as x64.
- **No `var`** — explicit types everywhere, including pattern-match and lambda variables.
- **No single-character identifiers**; **no underscores** except a private field's leading `_`.
- **Always curly braces** on every `if/else/for/foreach/while/using`.
- **Single exit point** — one `return` per method, at the end; assign computed values to a local first, then return it. Blank line above the `return`.
- **Blank lines around every block** — including immediately after a method/ctor opening `{` when the first statement is a block, and before the closing `}` when the last statement ends with `}`. Applies at every nesting level.
- **`string.Empty`** not `""`.
- **No comments** unless the WHY is non-obvious; no trailing summary comments.
- **Member order**: Fields → Constructor → Properties → Public methods → Override methods → Private methods.
- **ViewModel properties**: hand-written `SetProperty(ref _field, value)` (CommunityToolkit `ObservableObject`); **never** `[ObservableProperty]`. A property's backing field sits directly above the property (blank line between) in the Properties section. `{`, `get;`, `set;` each on their own line.
- **XAML formatting**: blank line after `<?xml?>`; element name on its own line; each attribute on its own line indented 4 spaces; attribute order = simple assignments, then event/`Command`, then value-bindings; blank line above/below every element; `>` or ` />` on the last attribute line. `DevicesPage.xaml`/`TrafficPage.xaml` are the references.
- **EF**: `EnsureCreated`, **never** create migrations. A new table is added — **the local DB must be deleted once** (`%LocalAppData%` → `networkmonitor.db`) for the `SpeedTestResults` table to be created.
- **CSV file name** convention: `Umnatha Network Monitor SpeedTests yyyy-MM-dd HH-mm.csv`.
- **Toasts**: classic `ToastNotificationManager` only (the app runs elevated; App SDK notifications silently fail).
- **Tests** run via Visual Studio Test Explorer / NCrunch (x64) or `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj`.

---

## File Structure

**New files:**
- `NetworkMonitor/Models/SpeedTestResult.cs` — EF entity.
- `NetworkMonitor/Models/SpeedChartPoint.cs` — chart point record (timestamp + Mbps).
- `NetworkMonitor/Services/SpeedTest/SpeedTestMath.cs` — pure throughput/latency/jitter math.
- `NetworkMonitor/Services/SpeedTest/SpeedTestMessage.cs` — notification message formatter.
- `NetworkMonitor/Services/SpeedTest/SpeedTestService.cs` — Cloudflare measurement.
- `NetworkMonitor/Services/SpeedTest/SpeedTestWorker.cs` — background loop + `RunNowAsync` + event.
- `NetworkMonitor/Services/SpeedTest/SpeedTestCompletedEventArgs.cs` — event args record.
- `NetworkMonitor/Services/Csv/SpeedTestCsvExporter.cs` — CSV export.
- `NetworkMonitor/ViewModels/SpeedTestViewModel.cs` — page VM.
- `NetworkMonitor/Views/Controls/SpeedTrendChart.xaml` (+ `.xaml.cs`) — trend chart.
- `NetworkMonitor/Views/SpeedTestPage.xaml` (+ `.xaml.cs`) — Speed Test tab.
- `NetworkMonitor/Views/TrafficHostPage.xaml` (+ `.xaml.cs`) — Traffic/Speed Test host.
- `NetworkMonitor.Tests/SpeedTestMathTests.cs`, `SpeedTestMessageTests.cs`, `SpeedTestCsvExporterTests.cs`.

**Modified files:**
- `NetworkMonitor/Data/AppDbContext.cs` — `DbSet` + index.
- `NetworkMonitor/Data/Settings.cs` — `SpeedTestEnabled`.
- `NetworkMonitor/Services/Scanning/ScanWorker.cs` — purge fold-in.
- `NetworkMonitor/App.xaml.cs` — DI registration.
- `NetworkMonitor/MainWindow.xaml.cs` — nav rewire + completion notification.
- `NetworkMonitor/ViewModels/SettingsViewModel.cs` + `NetworkMonitor/Views/SettingsPage.xaml` — toggle.
- `NetworkMonitor.slnx` — register this plan + new spec docs already added.

> WinUI SDK-style project auto-includes new `.cs` and `.xaml` (Page) files via default globs; no `.csproj` edit is expected. Source files are **not** listed in `.slnx` (only `Documents/` and `Installer/` are), so no `.slnx` change is needed for source.

---

## Task 1: SpeedTestResult model + EF wiring

**Files:**
- Create: `NetworkMonitor/Models/SpeedTestResult.cs`
- Modify: `NetworkMonitor/Data/AppDbContext.cs`

**Interfaces:**
- Produces: `SpeedTestResult` entity with `int Id`, `DateTime Timestamp`, `double DownloadMbps`, `double UploadMbps`, `double LatencyMs`, `double JitterMs`, `string Server`, `bool Success`, `string? Error`; `AppDbContext.SpeedTestResults` `DbSet`.

- [ ] **Step 1: Create the model**

`NetworkMonitor/Models/SpeedTestResult.cs`:

```csharp
namespace NetworkMonitor.Models
{
    public class SpeedTestResult
    {
        public int Id
        {
            get;
            set;
        }

        public DateTime Timestamp
        {
            get;
            set;
        }

        public double DownloadMbps
        {
            get;
            set;
        }

        public double UploadMbps
        {
            get;
            set;
        }

        public double LatencyMs
        {
            get;
            set;
        }

        public double JitterMs
        {
            get;
            set;
        }

        public string Server
        {
            get;
            set;
        } = string.Empty;

        public bool Success
        {
            get;
            set;
        }

        public string? Error
        {
            get;
            set;
        }
    }
}
```

- [ ] **Step 2: Add the DbSet and index**

In `NetworkMonitor/Data/AppDbContext.cs`, add the `DbSet` after `DigestReports` (line 13):

```csharp
        public DbSet<SpeedTestResult> SpeedTestResults => Set<SpeedTestResult>();
```

And inside `OnModelCreating`, after the `TrafficRollup` index block, add:

```csharp
            modelBuilder.Entity<SpeedTestResult>()
                .HasIndex(result => result.Timestamp);
```

- [ ] **Step 3: Build to verify**

Run (Visual Studio, x64) a build of `NetworkMonitor`. Expected: builds with no errors.

- [ ] **Step 4: Commit**

```bash
git add NetworkMonitor/Models/SpeedTestResult.cs NetworkMonitor/Data/AppDbContext.cs
git commit -m "Add SpeedTestResult entity and DbSet."
```

> **DB delete required** before first run after this task: delete `%LocalAppData%\NetworkMonitor\networkmonitor.db` (and `-wal`/`-shm`) so `EnsureCreated` rebuilds with `SpeedTestResults`.

---

## Task 2: Speed math helpers (TDD)

**Files:**
- Create: `NetworkMonitor/Services/SpeedTest/SpeedTestMath.cs`
- Test: `NetworkMonitor.Tests/SpeedTestMathTests.cs`

**Interfaces:**
- Produces: `static class SpeedTestMath` with `double ToMbps(long bytes, TimeSpan elapsed)`, `double Mean(IReadOnlyList<double> samples)`, `double Jitter(IReadOnlyList<double> samples)`.

- [ ] **Step 1: Write the failing test**

`NetworkMonitor.Tests/SpeedTestMathTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using NetworkMonitor.Services.SpeedTest;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class SpeedTestMathTests
    {
        [Fact]
        public void ToMbpsConvertsBytesPerSecondToMegabitsPerSecond()
        {
            double mbps = SpeedTestMath.ToMbps(1_000_000, TimeSpan.FromSeconds(1));

            Assert.Equal(8.0, mbps, 3);
        }

        [Fact]
        public void ToMbpsReturnsZeroForZeroElapsed()
        {
            double mbps = SpeedTestMath.ToMbps(1_000_000, TimeSpan.Zero);

            Assert.Equal(0.0, mbps);
        }

        [Fact]
        public void MeanAveragesSamples()
        {
            List<double> samples = [10.0, 20.0, 30.0];

            double mean = SpeedTestMath.Mean(samples);

            Assert.Equal(20.0, mean, 3);
        }

        [Fact]
        public void MeanReturnsZeroForEmpty()
        {
            List<double> samples = [];

            double mean = SpeedTestMath.Mean(samples);

            Assert.Equal(0.0, mean);
        }

        [Fact]
        public void JitterAveragesConsecutiveDifferences()
        {
            List<double> samples = [10.0, 14.0, 12.0];

            double jitter = SpeedTestMath.Jitter(samples);

            Assert.Equal(3.0, jitter, 3);
        }

        [Fact]
        public void JitterReturnsZeroForSingleSample()
        {
            List<double> samples = [10.0];

            double jitter = SpeedTestMath.Jitter(samples);

            Assert.Equal(0.0, jitter);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter SpeedTestMathTests`
Expected: FAIL — `SpeedTestMath` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

`NetworkMonitor/Services/SpeedTest/SpeedTestMath.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace NetworkMonitor.Services.SpeedTest
{
    public static class SpeedTestMath
    {
        public static double ToMbps(long bytes, TimeSpan elapsed)
        {
            double seconds = elapsed.TotalSeconds;
            double mbps = 0.0;

            if (seconds > 0.0)
            {
                mbps = bytes * 8.0 / seconds / 1_000_000.0;
            }

            return mbps;
        }

        public static double Mean(IReadOnlyList<double> samples)
        {
            double mean = 0.0;

            if (samples.Count > 0)
            {
                double total = 0.0;

                foreach (double sample in samples)
                {
                    total += sample;
                }

                mean = total / samples.Count;
            }

            return mean;
        }

        public static double Jitter(IReadOnlyList<double> samples)
        {
            double jitter = 0.0;

            if (samples.Count > 1)
            {
                double total = 0.0;

                for (int index = 1; index < samples.Count; index++)
                {
                    total += Math.Abs(samples[index] - samples[index - 1]);
                }

                jitter = total / (samples.Count - 1);
            }

            return jitter;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter SpeedTestMathTests`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor/Services/SpeedTest/SpeedTestMath.cs NetworkMonitor.Tests/SpeedTestMathTests.cs
git commit -m "Add SpeedTestMath throughput/latency/jitter helpers."
```

---

## Task 3: Notification message formatter (TDD)

**Files:**
- Create: `NetworkMonitor/Services/SpeedTest/SpeedTestMessage.cs`
- Test: `NetworkMonitor.Tests/SpeedTestMessageTests.cs`

**Interfaces:**
- Consumes: `SpeedTestResult` (Task 1).
- Produces: `static class SpeedTestMessage` with `string Format(SpeedTestResult result)`.

- [ ] **Step 1: Write the failing test**

`NetworkMonitor.Tests/SpeedTestMessageTests.cs`:

```csharp
using NetworkMonitor.Models;
using NetworkMonitor.Services.SpeedTest;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class SpeedTestMessageTests
    {
        [Fact]
        public void FormatProducesSuccessSummary()
        {
            SpeedTestResult result = new SpeedTestResult
            {
                DownloadMbps = 245.04,
                UploadMbps = 18.0,
                LatencyMs = 12.4,
                Success = true
            };

            string message = SpeedTestMessage.Format(result);

            Assert.Equal("Speed test: 245.0 ↓ / 18.0 ↑ Mbps · 12 ms", message);
        }

        [Fact]
        public void FormatProducesFailureSummary()
        {
            SpeedTestResult result = new SpeedTestResult
            {
                Success = false,
                Error = "Name resolution failed"
            };

            string message = SpeedTestMessage.Format(result);

            Assert.Equal("Speed test failed: Name resolution failed", message);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter SpeedTestMessageTests`
Expected: FAIL — `SpeedTestMessage` does not exist.

- [ ] **Step 3: Write minimal implementation**

`NetworkMonitor/Services/SpeedTest/SpeedTestMessage.cs`:

```csharp
using NetworkMonitor.Models;

namespace NetworkMonitor.Services.SpeedTest
{
    public static class SpeedTestMessage
    {
        public static string Format(SpeedTestResult result)
        {
            string message;

            if (result.Success)
            {
                message = $"Speed test: {result.DownloadMbps:0.0} ↓ / {result.UploadMbps:0.0} ↑ Mbps · {result.LatencyMs:0} ms";
            }
            else
            {
                message = $"Speed test failed: {result.Error}";
            }

            return message;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter SpeedTestMessageTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor/Services/SpeedTest/SpeedTestMessage.cs NetworkMonitor.Tests/SpeedTestMessageTests.cs
git commit -m "Add SpeedTestMessage notification formatter."
```

---

## Task 4: SpeedTestService (Cloudflare) + DI

**Files:**
- Create: `NetworkMonitor/Services/SpeedTest/SpeedTestService.cs`
- Modify: `NetworkMonitor/App.xaml.cs`

**Interfaces:**
- Consumes: `SpeedTestMath` (Task 2), `SpeedTestResult` (Task 1).
- Produces: `class SpeedTestService(HttpClient httpClient)` with `Task<SpeedTestResult> RunAsync(CancellationToken ct = default)`. Never throws — failures return a result with `Success = false`.

- [ ] **Step 1: Create the service**

`NetworkMonitor/Services/SpeedTest/SpeedTestService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NetworkMonitor.Models;

namespace NetworkMonitor.Services.SpeedTest
{
    public class SpeedTestService(HttpClient httpClient)
    {
        private const string DownloadUrl = "https://speed.cloudflare.com/__down?bytes=";
        private const string UploadUrl = "https://speed.cloudflare.com/__up";
        private const long DownloadBytes = 25_000_000;
        private const long UploadBytes = 10_000_000;
        private const int LatencySamples = 6;

        public async Task<SpeedTestResult> RunAsync(CancellationToken ct = default)
        {
            SpeedTestResult result;

            try
            {
                List<double> latencies = new();
                string server = string.Empty;

                for (int index = 0; index < LatencySamples; index++)
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();

                    using HttpResponseMessage response = await httpClient.GetAsync(
                        DownloadUrl + "0", HttpCompletionOption.ResponseHeadersRead, ct);

                    response.EnsureSuccessStatusCode();
                    await response.Content.ReadAsByteArrayAsync(ct);
                    stopwatch.Stop();
                    latencies.Add(stopwatch.Elapsed.TotalMilliseconds);

                    if (server.Length == 0 && response.Headers.TryGetValues("cf-meta-colo", out IEnumerable<string>? coloValues))
                    {

                        foreach (string colo in coloValues)
                        {
                            server = colo;

                            break;
                        }

                    }

                }

                double downloadMbps = await MeasureDownloadAsync(ct);
                double uploadMbps = await MeasureUploadAsync(ct);

                result = new SpeedTestResult
                {
                    Timestamp = DateTime.UtcNow,
                    DownloadMbps = downloadMbps,
                    UploadMbps = uploadMbps,
                    LatencyMs = SpeedTestMath.Mean(latencies),
                    JitterMs = SpeedTestMath.Jitter(latencies),
                    Server = server,
                    Success = true
                };
            }
            catch (Exception exception)
            {
                result = new SpeedTestResult
                {
                    Timestamp = DateTime.UtcNow,
                    Success = false,
                    Error = exception.Message
                };
            }

            return result;
        }

        private async Task<double> MeasureDownloadAsync(CancellationToken ct)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            long total = 0;

            using HttpResponseMessage response = await httpClient.GetAsync(
                DownloadUrl + DownloadBytes, HttpCompletionOption.ResponseHeadersRead, ct);

            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
            byte[] buffer = new byte[81920];
            int read = await stream.ReadAsync(buffer, ct);

            while (read > 0)
            {
                total += read;
                read = await stream.ReadAsync(buffer, ct);
            }

            stopwatch.Stop();
            double mbps = SpeedTestMath.ToMbps(total, stopwatch.Elapsed);

            return mbps;
        }

        private async Task<double> MeasureUploadAsync(CancellationToken ct)
        {
            byte[] payload = new byte[UploadBytes];
            Stopwatch stopwatch = Stopwatch.StartNew();

            using ByteArrayContent content = new ByteArrayContent(payload);
            using HttpResponseMessage response = await httpClient.PostAsync(UploadUrl, content, ct);

            response.EnsureSuccessStatusCode();
            stopwatch.Stop();
            double mbps = SpeedTestMath.ToMbps(payload.LongLength, stopwatch.Elapsed);

            return mbps;
        }
    }
}
```

- [ ] **Step 2: Register in DI**

In `NetworkMonitor/App.xaml.cs`, add `using NetworkMonitor.Services.SpeedTest;` to the usings, then in `ConfigureServices` after the `InAppNotificationService` registration (line 106) add:

```csharp
                        services.AddSingleton<SpeedTestService>(serviceProvider =>
                        {
                            HttpClient httpClient = new HttpClient
                            {
                                Timeout = TimeSpan.FromSeconds(60)
                            };

                            return new SpeedTestService(httpClient);
                        });
```

- [ ] **Step 3: Build to verify**

Build `NetworkMonitor` (x64). Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add NetworkMonitor/Services/SpeedTest/SpeedTestService.cs NetworkMonitor/App.xaml.cs
git commit -m "Add Cloudflare SpeedTestService and register it."
```

---

## Task 5: SpeedTestWorker + event args + DI

**Files:**
- Create: `NetworkMonitor/Services/SpeedTest/SpeedTestCompletedEventArgs.cs`
- Create: `NetworkMonitor/Services/SpeedTest/SpeedTestWorker.cs`
- Modify: `NetworkMonitor/App.xaml.cs`

**Interfaces:**
- Consumes: `SpeedTestService` (Task 4), `Settings` (Task 6 adds `SpeedTestEnabled` — Task 6 must precede a successful build; see note), `AppDbContext.SpeedTestResults` (Task 1).
- Produces: `record SpeedTestCompletedEventArgs(SpeedTestResult Result)`; `class SpeedTestWorker : BackgroundService` with `event EventHandler<SpeedTestCompletedEventArgs>? SpeedTestCompleted` and `Task RunNowAsync(CancellationToken ct = default)`.

> **Ordering note:** `SpeedTestWorker.ExecuteAsync` reads `settings.SpeedTestEnabled`, added in Task 6. Do **Task 6 before building Task 5**, or add the `SpeedTestEnabled` property first. The steps below assume Task 6's property exists.

- [ ] **Step 1: Create the event args**

`NetworkMonitor/Services/SpeedTest/SpeedTestCompletedEventArgs.cs`:

```csharp
using NetworkMonitor.Models;

namespace NetworkMonitor.Services.SpeedTest
{
    public record SpeedTestCompletedEventArgs(SpeedTestResult Result);
}
```

- [ ] **Step 2: Create the worker**

`NetworkMonitor/Services/SpeedTest/SpeedTestWorker.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NetworkMonitor.Data;
using NetworkMonitor.Models;
using NetworkMonitor.Services.Platform;

namespace NetworkMonitor.Services.SpeedTest
{
    public class SpeedTestWorker(
        SpeedTestService service,
        Settings settings,
        IDbContextFactory<AppDbContext> dbFactory) : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
        private readonly SemaphoreSlim _runGate = new(1, 1);

        public event EventHandler<SpeedTestCompletedEventArgs>? SpeedTestCompleted;

        public async Task RunNowAsync(CancellationToken ct = default)
        {
            await RunTestAsync(ct);
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {

            while (!ct.IsCancellationRequested)
            {

                try
                {
                    await Task.Delay(Interval, ct);

                    if (settings.SpeedTestEnabled)
                    {
                        await RunTestAsync(ct);
                    }

                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    AppLog.Error("SpeedTestWorker.Execute", exception);
                }

            }

        }

        public override void Dispose()
        {
            _runGate.Dispose();

            base.Dispose();
        }

        private async Task RunTestAsync(CancellationToken ct)
        {
            await _runGate.WaitAsync(ct);

            try
            {
                SpeedTestResult result = await service.RunAsync(ct);

                await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
                db.SpeedTestResults.Add(result);
                await db.SaveChangesAsync(ct);

                SpeedTestCompleted?.Invoke(this, new SpeedTestCompletedEventArgs(result));
                AppLog.Info($"Speed test completed: {result.DownloadMbps:0.0} down / {result.UploadMbps:0.0} up Mbps, success={result.Success}.");
            }
            finally
            {
                _runGate.Release();
            }

        }
    }
}
```

- [ ] **Step 3: Register in DI**

In `NetworkMonitor/App.xaml.cs`, after the `SpeedTestService` registration (Task 4), add:

```csharp
                        services.AddSingleton<SpeedTestWorker>();
                        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<SpeedTestWorker>());
```

- [ ] **Step 4: Build to verify**

Build `NetworkMonitor` (x64). Expected: no errors (requires Task 6's `SpeedTestEnabled`).

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor/Services/SpeedTest/SpeedTestWorker.cs NetworkMonitor/Services/SpeedTest/SpeedTestCompletedEventArgs.cs NetworkMonitor/App.xaml.cs
git commit -m "Add SpeedTestWorker background loop and manual run."
```

---

## Task 6: Settings flag + purge fold-in

**Files:**
- Modify: `NetworkMonitor/Data/Settings.cs`
- Modify: `NetworkMonitor/Services/Scanning/ScanWorker.cs`

**Interfaces:**
- Produces: `Settings.SpeedTestEnabled` (`bool`, default `true`).
- Consumes: `AppDbContext.SpeedTestResults` (Task 1).

- [ ] **Step 1: Add the setting**

In `NetworkMonitor/Data/Settings.cs`, after the `EnableLogging` property (line 147), add:

```csharp
        public bool SpeedTestEnabled
        {
            get;
            set;
        } = true;
```

- [ ] **Step 2: Fold speed-test purge into the traffic purge**

In `NetworkMonitor/Services/Scanning/ScanWorker.cs`, inside `PurgeOldHistoryAsync`, within the existing `if (settings.TrafficPurgeDays > 0)` block, after the `TrafficRollups` raw-SQL delete (line 124), add:

```csharp
                await db.SpeedTestResults
                    .Where(result => result.Timestamp < trafficCutoff)
                    .ExecuteDeleteAsync(ct);
```

- [ ] **Step 3: Build to verify**

Build `NetworkMonitor` (x64). Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add NetworkMonitor/Data/Settings.cs NetworkMonitor/Services/Scanning/ScanWorker.cs
git commit -m "Add SpeedTestEnabled setting and fold speed-test purge into traffic purge."
```

---

## Task 7: SpeedTestCsvExporter (TDD)

**Files:**
- Create: `NetworkMonitor/Services/Csv/SpeedTestCsvExporter.cs`
- Test: `NetworkMonitor.Tests/SpeedTestCsvExporterTests.cs`

**Interfaces:**
- Consumes: `SpeedTestResult` (Task 1), `CsvField.Escape` (existing).
- Produces: `static class SpeedTestCsvExporter` with `string ToCsv(IEnumerable<SpeedTestResult> results)`.

- [ ] **Step 1: Write the failing test**

`NetworkMonitor.Tests/SpeedTestCsvExporterTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using NetworkMonitor.Models;
using NetworkMonitor.Services.Csv;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class SpeedTestCsvExporterTests
    {
        [Fact]
        public void ToCsvWritesHeaderRow()
        {
            List<SpeedTestResult> results = [];

            string csv = SpeedTestCsvExporter.ToCsv(results);

            string firstLine = csv.Split('\n')[0].TrimEnd('\r');

            Assert.Equal("Timestamp,Download (Mbps),Upload (Mbps),Latency (ms),Jitter (ms),Server,Success", firstLine);
        }

        [Fact]
        public void ToCsvWritesResultRow()
        {
            List<SpeedTestResult> results =
            [
                new SpeedTestResult
                {
                    Timestamp = new DateTime(2026, 6, 28, 10, 0, 0, DateTimeKind.Utc),
                    DownloadMbps = 245.0,
                    UploadMbps = 18.0,
                    LatencyMs = 12.0,
                    JitterMs = 3.0,
                    Server = "JNB",
                    Success = true
                }
            ];

            string csv = SpeedTestCsvExporter.ToCsv(results);

            Assert.Contains("245.0,18.0,12,3,JNB,Yes", csv);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter SpeedTestCsvExporterTests`
Expected: FAIL — `SpeedTestCsvExporter` does not exist.

- [ ] **Step 3: Write minimal implementation**

`NetworkMonitor/Services/Csv/SpeedTestCsvExporter.cs`:

```csharp
using System.Collections.Generic;
using System.Text;
using NetworkMonitor.Models;

namespace NetworkMonitor.Services.Csv
{
    public static class SpeedTestCsvExporter
    {
        public static string ToCsv(IEnumerable<SpeedTestResult> results)
        {
            StringBuilder builder = new();
            builder.AppendLine("Timestamp,Download (Mbps),Upload (Mbps),Latency (ms),Jitter (ms),Server,Success");

            foreach (SpeedTestResult result in results)
            {
                string timestamp = result.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                string successLabel = result.Success ? "Yes" : "No";

                string line = string.Join(",",
                    CsvField.Escape(timestamp),
                    CsvField.Escape(result.DownloadMbps.ToString("0.0")),
                    CsvField.Escape(result.UploadMbps.ToString("0.0")),
                    CsvField.Escape(result.LatencyMs.ToString("0")),
                    CsvField.Escape(result.JitterMs.ToString("0")),
                    CsvField.Escape(result.Server),
                    CsvField.Escape(successLabel));

                builder.AppendLine(line);
            }

            string csv = builder.ToString();

            return csv;
        }
    }
}
```

> The CSV `Timestamp` uses local time and a UTC test input. The `2026-06-28 10:00:00` UTC value renders in local time; assert only on the numeric columns (`Assert.Contains("245.0,18.0,12,3,JNB,Yes", csv)`) to stay timezone-independent.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj --filter SpeedTestCsvExporterTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor/Services/Csv/SpeedTestCsvExporter.cs NetworkMonitor.Tests/SpeedTestCsvExporterTests.cs
git commit -m "Add SpeedTestCsvExporter."
```

---

## Task 8: SpeedChartPoint + SpeedTestViewModel + DI

**Files:**
- Create: `NetworkMonitor/Models/SpeedChartPoint.cs`
- Create: `NetworkMonitor/ViewModels/SpeedTestViewModel.cs`
- Modify: `NetworkMonitor/App.xaml.cs`

**Interfaces:**
- Consumes: `SpeedTestWorker` (Task 5), `AppDbContext.SpeedTestResults` (Task 1), `Settings.TrafficPurgeDays` (existing).
- Produces: `record SpeedChartPoint(DateTime Timestamp, double DownloadMbps, double UploadMbps)`; `SpeedTestViewModel` with `ObservableCollection<SpeedTestResult> History`, `SpeedTestResult? Latest`, `IReadOnlyList<SpeedChartPoint> ChartPoints`, `bool IsRunning`, `IAsyncRelayCommand RunNowCommand`, `Task LoadAsync()`.

- [ ] **Step 1: Create the chart point record**

`NetworkMonitor/Models/SpeedChartPoint.cs`:

```csharp
namespace NetworkMonitor.Models
{
    public record SpeedChartPoint(DateTime Timestamp, double DownloadMbps, double UploadMbps);
}
```

- [ ] **Step 2: Create the ViewModel**

`NetworkMonitor/ViewModels/SpeedTestViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Dispatching;
using NetworkMonitor.Data;
using NetworkMonitor.Models;
using NetworkMonitor.Services.SpeedTest;

namespace NetworkMonitor.ViewModels
{
    public partial class SpeedTestViewModel : ObservableObject
    {
        private readonly SpeedTestWorker _worker;
        private readonly Settings _settings;
        private readonly IDbContextFactory<AppDbContext> _dbFactory;
        private readonly DispatcherQueue _dispatcherQueue;

        public SpeedTestViewModel(SpeedTestWorker worker, Settings settings, IDbContextFactory<AppDbContext> dbFactory)
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _worker = worker;
            _settings = settings;
            _dbFactory = dbFactory;
            RunNowCommand = new AsyncRelayCommand(RunNowAsync);
            _worker.SpeedTestCompleted += OnSpeedTestCompleted;
        }

        public ObservableCollection<SpeedTestResult> History
        {
            get;
        } = new();

        public IAsyncRelayCommand RunNowCommand
        {
            get;
        }

        private SpeedTestResult? _latest;

        public SpeedTestResult? Latest
        {
            get => _latest;
            set => SetProperty(ref _latest, value);
        }

        private IReadOnlyList<SpeedChartPoint> _chartPoints = [];

        public IReadOnlyList<SpeedChartPoint> ChartPoints
        {
            get => _chartPoints;
            set => SetProperty(ref _chartPoints, value);
        }

        private bool _isRunning;

        public bool IsRunning
        {
            get => _isRunning;
            set => SetProperty(ref _isRunning, value);
        }

        public async Task LoadAsync()
        {
            await using AppDbContext db = await _dbFactory.CreateDbContextAsync();
            DateTime cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, _settings.TrafficPurgeDays));

            List<SpeedTestResult> rows = await db.SpeedTestResults
                .Where(result => result.Timestamp >= cutoff)
                .OrderBy(result => result.Timestamp)
                .ToListAsync();

            List<SpeedChartPoint> points = rows
                .Where(result => result.Success)
                .Select(result => new SpeedChartPoint(result.Timestamp, result.DownloadMbps, result.UploadMbps))
                .ToList();

            History.Clear();

            foreach (SpeedTestResult row in Enumerable.Reverse(rows))
            {
                History.Add(row);
            }

            ChartPoints = points;
            Latest = rows.Count > 0 ? rows[^1] : null;
        }

        private async Task RunNowAsync()
        {
            IsRunning = true;

            try
            {
                await _worker.RunNowAsync();
            }
            finally
            {
                IsRunning = false;
            }

        }

        private void OnSpeedTestCompleted(object? sender, SpeedTestCompletedEventArgs args)
        {
            _dispatcherQueue.TryEnqueue(() => _ = LoadAsync());
        }
    }
}
```

> `Latest` and `ChartPoints` use inline ternary/conditional only inside assignments to locals/properties, not in a `return` — compliant with the single-exit rule. `History` is a get-only `ObservableCollection`, so it does not need a `SetProperty` backing field.

- [ ] **Step 3: Register in DI**

In `NetworkMonitor/App.xaml.cs`, after `services.AddSingleton<TrafficViewModel>();` (line 118), add:

```csharp
                        services.AddSingleton<SpeedTestViewModel>();
```

- [ ] **Step 4: Build to verify**

Build `NetworkMonitor` (x64). Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor/Models/SpeedChartPoint.cs NetworkMonitor/ViewModels/SpeedTestViewModel.cs NetworkMonitor/App.xaml.cs
git commit -m "Add SpeedTestViewModel and SpeedChartPoint."
```

---

## Task 9: SpeedTrendChart control

**Files:**
- Create: `NetworkMonitor/Views/Controls/SpeedTrendChart.xaml`
- Create: `NetworkMonitor/Views/Controls/SpeedTrendChart.xaml.cs`

**Interfaces:**
- Consumes: `SpeedChartPoint` (Task 8).
- Produces: `SpeedTrendChart` `UserControl` with a `IReadOnlyList<SpeedChartPoint> ChartPoints` dependency property.

This is a static (non-scrolling) area+line chart using XAML `Polyline`/`Polygon` shapes inside a `Canvas`. Download series uses blue `#1976D2`, upload uses purple `#AB47BC`, matching the traffic chart. A hover tooltip shows the nearest point's Mbps and time.

- [ ] **Step 1: Create the XAML**

`NetworkMonitor/Views/Controls/SpeedTrendChart.xaml`:

```xml
<?xml version="1.0" encoding="utf-8"?>

<UserControl
    x:Class="NetworkMonitor.Views.Controls.SpeedTrendChart"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Grid
        SizeChanged="OnSizeChanged">

        <Canvas
            x:Name="PlotCanvas" />

        <Border
            x:Name="HoverPanel"
            HorizontalAlignment="Left"
            VerticalAlignment="Top"
            Background="{ThemeResource SolidBackgroundFillColorTertiaryBrush}"
            BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
            BorderThickness="1"
            CornerRadius="4"
            Padding="8,5"
            Visibility="Collapsed">

            <StackPanel
                Spacing="2">

                <TextBlock
                    x:Name="HoverTimeLabel"
                    FontSize="11"
                    Opacity="0.7" />

                <TextBlock
                    x:Name="HoverDownLabel"
                    FontSize="12"
                    Foreground="#1976D2" />

                <TextBlock
                    x:Name="HoverUpLabel"
                    FontSize="12"
                    Foreground="#AB47BC" />

            </StackPanel>

        </Border>

        <Rectangle
            x:Name="InputLayer"
            Fill="Transparent"
            PointerMoved="OnPointerMoved"
            PointerExited="OnPointerExited" />

    </Grid>

</UserControl>
```

- [ ] **Step 2: Create the code-behind**

`NetworkMonitor/Views/Controls/SpeedTrendChart.xaml.cs`:

```csharp
using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NetworkMonitor.Models;
using Windows.Foundation;
using Windows.UI;

namespace NetworkMonitor.Views.Controls
{
    public sealed partial class SpeedTrendChart : UserControl
    {
        private static readonly Color DownloadColor = Color.FromArgb(0xFF, 0x19, 0x76, 0xD2);
        private static readonly Color UploadColor = Color.FromArgb(0xFF, 0xAB, 0x47, 0xBC);

        public static readonly DependencyProperty ChartPointsProperty =
            DependencyProperty.Register(
                nameof(ChartPoints),
                typeof(IReadOnlyList<SpeedChartPoint>),
                typeof(SpeedTrendChart),
                new PropertyMetadata(null, OnChartPointsChanged));

        public SpeedTrendChart()
        {
            InitializeComponent();
        }

        public IReadOnlyList<SpeedChartPoint>? ChartPoints
        {
            get => (IReadOnlyList<SpeedChartPoint>?)GetValue(ChartPointsProperty);
            set => SetValue(ChartPointsProperty, value);
        }

        private static void OnChartPointsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            SpeedTrendChart chart = (SpeedTrendChart)sender;

            chart.Redraw();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs args)
        {
            Redraw();
        }

        private void Redraw()
        {
            PlotCanvas.Children.Clear();

            IReadOnlyList<SpeedChartPoint>? points = ChartPoints;
            double width = PlotCanvas.ActualWidth;
            double height = PlotCanvas.ActualHeight;

            if (points is not null && points.Count >= 2 && width > 0 && height > 0)
            {
                double maxValue = 1.0;

                foreach (SpeedChartPoint point in points)
                {
                    maxValue = Math.Max(maxValue, Math.Max(point.DownloadMbps, point.UploadMbps));
                }

                DrawSeries(points, maxValue, width, height, true, DownloadColor);
                DrawSeries(points, maxValue, width, height, false, UploadColor);
            }

        }

        private void DrawSeries(IReadOnlyList<SpeedChartPoint> points, double maxValue, double width, double height, bool isDownload, Color color)
        {
            double usableHeight = height * 0.9;
            double minEpoch = (points[0].Timestamp - DateTime.UnixEpoch).TotalSeconds;
            double maxEpoch = (points[points.Count - 1].Timestamp - DateTime.UnixEpoch).TotalSeconds;
            double span = maxEpoch - minEpoch;

            if (span <= 0)
            {
                span = 1.0;
            }

            PointCollection linePoints = new PointCollection();
            PointCollection areaPoints = new PointCollection();
            areaPoints.Add(new Point(0, height));

            foreach (SpeedChartPoint point in points)
            {
                double epoch = (point.Timestamp - DateTime.UnixEpoch).TotalSeconds;
                double value = isDownload ? point.DownloadMbps : point.UploadMbps;
                double xValue = (epoch - minEpoch) / span * width;
                double yValue = height - value / maxValue * usableHeight;
                linePoints.Add(new Point(xValue, yValue));
                areaPoints.Add(new Point(xValue, yValue));
            }

            areaPoints.Add(new Point(width, height));

            Color fillColor = Color.FromArgb(0x33, color.R, color.G, color.B);

            Polygon area = new Polygon
            {
                Points = areaPoints,
                Fill = new SolidColorBrush(fillColor)
            };

            Polyline line = new Polyline
            {
                Points = linePoints,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 1.5
            };

            PlotCanvas.Children.Add(area);
            PlotCanvas.Children.Add(line);
        }

        private void OnPointerMoved(object sender, PointerRoutedEventArgs args)
        {
            IReadOnlyList<SpeedChartPoint>? points = ChartPoints;

            if (points is not null && points.Count >= 2 && PlotCanvas.ActualWidth > 0)
            {
                Point position = args.GetCurrentPoint(InputLayer).Position;
                double width = PlotCanvas.ActualWidth;
                double minEpoch = (points[0].Timestamp - DateTime.UnixEpoch).TotalSeconds;
                double maxEpoch = (points[points.Count - 1].Timestamp - DateTime.UnixEpoch).TotalSeconds;
                double span = maxEpoch - minEpoch;

                if (span <= 0)
                {
                    span = 1.0;
                }

                double targetEpoch = minEpoch + position.X / width * span;
                int nearest = 0;
                double bestDistance = double.MaxValue;

                for (int index = 0; index < points.Count; index++)
                {
                    double epoch = (points[index].Timestamp - DateTime.UnixEpoch).TotalSeconds;
                    double distance = Math.Abs(epoch - targetEpoch);

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        nearest = index;
                    }

                }

                SpeedChartPoint hovered = points[nearest];
                HoverTimeLabel.Text = hovered.Timestamp.ToLocalTime().ToString("dd MMM HH:mm");
                HoverDownLabel.Text = $"{hovered.DownloadMbps:0.0} Mbps down";
                HoverUpLabel.Text = $"{hovered.UploadMbps:0.0} Mbps up";
                HoverPanel.Visibility = Visibility.Visible;

                double panelLeft = position.X + 14;

                if (panelLeft + 130 > width)
                {
                    panelLeft = position.X - 144;
                }

                HoverPanel.Margin = new Thickness(Math.Max(0, panelLeft), 8, 0, 0);
            }

        }

        private void OnPointerExited(object sender, PointerRoutedEventArgs args)
        {
            HoverPanel.Visibility = Visibility.Collapsed;
        }
    }
}
```

> Single-exit compliant: guard logic is expressed as a positive `if` wrapping the body (no early `return`). `void` methods have no `return` statement. Maintain this style throughout.

- [ ] **Step 3: Build to verify**

Build `NetworkMonitor` (x64). Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add NetworkMonitor/Views/Controls/SpeedTrendChart.xaml NetworkMonitor/Views/Controls/SpeedTrendChart.xaml.cs
git commit -m "Add SpeedTrendChart control."
```

---

## Task 10: SpeedTestPage (tab content)

**Files:**
- Create: `NetworkMonitor/Views/SpeedTestPage.xaml`
- Create: `NetworkMonitor/Views/SpeedTestPage.xaml.cs`

**Interfaces:**
- Consumes: `SpeedTestViewModel` (Task 8), `SpeedTrendChart` (Task 9), `SpeedTestCsvExporter` (Task 7), `Win32FileSaveDialog` + `ShellLauncher` + `MainWindow.Current` (existing).
- Produces: `SpeedTestPage` with `public SpeedTestViewModel ViewModel { get; }`.

- [ ] **Step 1: Create the XAML**

`NetworkMonitor/Views/SpeedTestPage.xaml`:

```xml
<?xml version="1.0" encoding="utf-8"?>

<Page
    x:Class="NetworkMonitor.Views.SpeedTestPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:controls="using:CommunityToolkit.WinUI.UI.Controls"
    xmlns:chart="using:NetworkMonitor.Views.Controls"
    xmlns:models="using:NetworkMonitor.Models"
    Background="{ThemeResource ApplicationPageBackgroundThemeBrush}">

    <Grid
        RowDefinitions="Auto,Auto,*,Auto"
        Padding="16,12,16,12">

        <Border
            Grid.Row="0"
            Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
            BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
            BorderThickness="1"
            CornerRadius="6"
            Padding="12"
            Margin="0,0,0,10">

            <Grid>

                <chart:SpeedTrendChart
                    x:Name="TrendChart"
                    Height="120"
                    ChartPoints="{x:Bind ViewModel.ChartPoints, Mode=OneWay}" />

            </Grid>

        </Border>

        <Grid
            Grid.Row="1"
            ColumnDefinitions="*,Auto,Auto"
            Margin="0,0,0,10">

            <StackPanel
                Grid.Column="0"
                Orientation="Horizontal"
                Spacing="20"
                VerticalAlignment="Center">

                <StackPanel
                    Spacing="1">

                    <TextBlock
                        FontSize="11"
                        Opacity="0.55"
                        Text="Download" />

                    <TextBlock
                        FontSize="20"
                        FontWeight="SemiBold"
                        Foreground="#1976D2"
                        Text="{x:Bind ViewModel.Latest.DownloadMbps, Mode=OneWay}" />

                </StackPanel>

                <StackPanel
                    Spacing="1">

                    <TextBlock
                        FontSize="11"
                        Opacity="0.55"
                        Text="Upload" />

                    <TextBlock
                        FontSize="20"
                        FontWeight="SemiBold"
                        Foreground="#AB47BC"
                        Text="{x:Bind ViewModel.Latest.UploadMbps, Mode=OneWay}" />

                </StackPanel>

                <StackPanel
                    Spacing="1">

                    <TextBlock
                        FontSize="11"
                        Opacity="0.55"
                        Text="Latency / Jitter (ms)" />

                    <TextBlock
                        FontSize="20"
                        FontWeight="SemiBold"
                        Text="{x:Bind ViewModel.Latest.LatencyMs, Mode=OneWay}" />

                </StackPanel>

            </StackPanel>

            <ProgressRing
                Grid.Column="1"
                Width="24"
                Height="24"
                Margin="0,0,12,0"
                VerticalAlignment="Center"
                IsActive="{x:Bind ViewModel.IsRunning, Mode=OneWay}" />

            <Button
                Grid.Column="2"
                Content="Run Speed Test"
                VerticalAlignment="Center"
                Style="{StaticResource AccentButtonStyle}"
                Command="{x:Bind ViewModel.RunNowCommand}" />

        </Grid>

        <controls:DataGrid
            Grid.Row="2"
            x:Name="HistoryGrid"
            AutoGenerateColumns="False"
            IsReadOnly="True"
            GridLinesVisibility="Horizontal"
            SelectionMode="Single"
            BorderThickness="1"
            BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
            ItemsSource="{x:Bind ViewModel.History, Mode=OneWay}">

            <controls:DataGrid.Columns>

                <controls:DataGridTextColumn
                    Header="Time"
                    Width="*"
                    Binding="{Binding Timestamp}" />

                <controls:DataGridTextColumn
                    Header="Download (Mbps)"
                    Width="140"
                    Binding="{Binding DownloadMbps}" />

                <controls:DataGridTextColumn
                    Header="Upload (Mbps)"
                    Width="140"
                    Binding="{Binding UploadMbps}" />

                <controls:DataGridTextColumn
                    Header="Latency (ms)"
                    Width="110"
                    Binding="{Binding LatencyMs}" />

                <controls:DataGridTextColumn
                    Header="Jitter (ms)"
                    Width="100"
                    Binding="{Binding JitterMs}" />

                <controls:DataGridTextColumn
                    Header="Server"
                    Width="90"
                    Binding="{Binding Server}" />

            </controls:DataGrid.Columns>

        </controls:DataGrid>

        <Grid
            Grid.Row="3"
            ColumnDefinitions="*,Auto"
            Margin="0,8,0,0">

            <TextBlock
                Grid.Column="0"
                x:Name="StatusText"
                Opacity="0.65"
                FontSize="12"
                VerticalAlignment="Center" />

            <Button
                Grid.Column="1"
                Content="Export CSV"
                Click="ExportCsvClick" />

        </Grid>

    </Grid>

</Page>
```

> `DataGridTextColumn` with `{Binding ...}` is used (not `{x:Bind}`) because DataGrid cell templates resolve against the row item at runtime, matching the existing grids. `Timestamp`, `DownloadMbps`, etc. display with default formatting; that is acceptable for v1. The latest-tile uses `x:Bind ViewModel.Latest.Xxx` which is null-tolerant under `Mode=OneWay` (shows blank until the first result).

- [ ] **Step 2: Create the code-behind**

`NetworkMonitor/Views/SpeedTestPage.xaml.cs`:

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NetworkMonitor.Services.Csv;
using NetworkMonitor.Services.Platform;
using NetworkMonitor.ViewModels;

namespace NetworkMonitor.Views
{
    public sealed partial class SpeedTestPage : Page
    {
        public SpeedTestPage()
        {
            ViewModel = App.AppHost.Services.GetRequiredService<SpeedTestViewModel>();
            InitializeComponent();
        }

        public SpeedTestViewModel ViewModel
        {
            get;
        }

        protected override void OnNavigatedTo(NavigationEventArgs args)
        {
            base.OnNavigatedTo(args);

            _ = ViewModel.LoadAsync();
        }

        private async void ExportCsvClick(object sender, RoutedEventArgs args)
        {

            if (ViewModel.History.Count == 0)
            {
                StatusText.Text = "No speed test results to export.";
            }
            else
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Current);
                string suggestedFileName = $"Umnatha Network Monitor SpeedTests {DateTime.Now:yyyy-MM-dd HH-mm}";
                string? path = Win32FileSaveDialog.PickSavePath(hwnd, suggestedFileName, "CSV File", ".csv", "Export Speed Tests");

                if (path is not null)
                {
                    string csv = SpeedTestCsvExporter.ToCsv(ViewModel.History);
                    await File.WriteAllTextAsync(path, csv);
                    ShellLauncher.Open(path);
                    StatusText.Text = $"Exported {ViewModel.History.Count} result{(ViewModel.History.Count == 1 ? string.Empty : "s")} to {Path.GetFileName(path)}";
                }

            }

        }
    }
}
```

> Confirm `ShellLauncher` is the helper used by `DeviceHistoryPage` (it is — `NetworkMonitor.Services.Platform.ShellLauncher`). If the namespace differs, match `DeviceHistoryPage.xaml.cs`'s `using` for it.

- [ ] **Step 3: Build to verify**

Build `NetworkMonitor` (x64). Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add NetworkMonitor/Views/SpeedTestPage.xaml NetworkMonitor/Views/SpeedTestPage.xaml.cs
git commit -m "Add SpeedTestPage with chart, latest tile, run button, history grid, CSV export."
```

---

## Task 11: TrafficHostPage + nav rewire

**Files:**
- Create: `NetworkMonitor/Views/TrafficHostPage.xaml`
- Create: `NetworkMonitor/Views/TrafficHostPage.xaml.cs`
- Modify: `NetworkMonitor/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `TrafficPage` (existing), `SpeedTestPage` (Task 10).
- Produces: `TrafficHostPage` navigated to by the `traffic` nav tag.

- [ ] **Step 1: Create the host XAML** (mirrors `DevicesHostPage.xaml`)

`NetworkMonitor/Views/TrafficHostPage.xaml`:

```xml
<?xml version="1.0" encoding="utf-8"?>

<Page
    x:Class="NetworkMonitor.Views.TrafficHostPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
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
                Tag="Traffic"
                Text="Traffic" />

            <SelectorBarItem
                Tag="SpeedTest"
                Text="Speed Test" />

        </SelectorBar>

        <Frame
            Grid.Row="1"
            x:Name="TrafficFrame"
            Margin="0,8,0,0" />

        <Frame
            Grid.Row="1"
            x:Name="SpeedTestFrame"
            Visibility="Collapsed"
            Margin="0,8,0,0" />

    </Grid>

</Page>
```

- [ ] **Step 2: Create the host code-behind** (mirrors `DevicesHostPage.xaml.cs`)

`NetworkMonitor/Views/TrafficHostPage.xaml.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NetworkMonitor.Views
{
    public sealed partial class TrafficHostPage : Page
    {
        public TrafficHostPage()
        {
            InitializeComponent();
            TrafficFrame.Navigate(typeof(TrafficPage));
            TabBar.SelectedItem = TabBar.Items[0];
        }

        private void TabBarSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {

            if (sender.SelectedItem is not null)
            {
                string selectedTag = (string)sender.SelectedItem.Tag;

                if (selectedTag == "SpeedTest" && SpeedTestFrame.Content is null)
                {
                    SpeedTestFrame.Navigate(typeof(SpeedTestPage));
                }

                TrafficFrame.Visibility = selectedTag == "Traffic" ? Visibility.Visible : Visibility.Collapsed;
                SpeedTestFrame.Visibility = selectedTag == "SpeedTest" ? Visibility.Visible : Visibility.Collapsed;
            }

        }
    }
}
```

- [ ] **Step 3: Rewire the nav to the host page**

In `NetworkMonitor/MainWindow.xaml.cs`:

In `NavViewLoaded` (around line 230), change:

```csharp
            ContentFrame.Navigate(typeof(TrafficPage));
```

to:

```csharp
            ContentFrame.Navigate(typeof(TrafficHostPage));
```

And in the `NavViewSelectionChanged` switch (around line 245), change:

```csharp
                    "traffic" => typeof(TrafficPage),
```

to:

```csharp
                    "traffic" => typeof(TrafficHostPage),
```

- [ ] **Step 4: Build and manual-verify**

Build `NetworkMonitor` (x64) and run. Expected: the **Traffic** nav item shows a host with **Traffic** and **Speed Test** tabs; **Traffic** tab is the existing page; **Speed Test** tab loads the new page (empty until a test runs).

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor/Views/TrafficHostPage.xaml NetworkMonitor/Views/TrafficHostPage.xaml.cs NetworkMonitor/MainWindow.xaml.cs
git commit -m "Add TrafficHostPage with Traffic and Speed Test tabs."
```

---

## Task 12: Completion notification in MainWindow

**Files:**
- Modify: `NetworkMonitor/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `SpeedTestWorker.SpeedTestCompleted` (Task 5), `SpeedTestMessage.Format` (Task 3), `InAppNotificationService` (existing field), `Settings.ShowToasts` (existing).

- [ ] **Step 1: Inject SpeedTestWorker and subscribe**

In `NetworkMonitor/MainWindow.xaml.cs`, add a field next to the other injected services:

```csharp
        private readonly SpeedTestWorker _speedTestWorker;
```

Add `SpeedTestWorker speedTestWorker` to the constructor signature (line 33) and assign it; then subscribe (next to where it currently subscribes scan/digest events):

```csharp
            _speedTestWorker = speedTestWorker;
            _speedTestWorker.SpeedTestCompleted += OnSpeedTestCompleted;
```

Add `using NetworkMonitor.Services.SpeedTest;` and `using NetworkMonitor.Models;` if not already present, plus the toast XML usings already used by the digest handler (`Windows.Data.Xml.Dom`, `Windows.UI.Notifications`).

- [ ] **Step 2: Add the handler** (mirrors the digest-ready handler around line 295)

```csharp
        private void OnSpeedTestCompleted(object? sender, SpeedTestCompletedEventArgs args)
        {
            string message = SpeedTestMessage.Format(args.Result);

            _dispatcherQueue.TryEnqueue(() =>
            {
                _notificationService.Show(message);

                if (_settings.ShowToasts)
                {
                    Windows.Data.Xml.Dom.XmlDocument toastXml = new Windows.Data.Xml.Dom.XmlDocument();
                    toastXml.LoadXml("<toast><visual><binding template=\"ToastGeneric\"><text id=\"1\"/><text id=\"2\"/></binding></visual><audio silent=\"true\"/></toast>");
                    Windows.Data.Xml.Dom.XmlNodeList textNodes = toastXml.GetElementsByTagName("text");
                    textNodes[0].InnerText = "Speed test complete";
                    textNodes[1].InnerText = message;
                    Windows.UI.Notifications.ToastNotification toastNotification = new Windows.UI.Notifications.ToastNotification(toastXml);
                    toastNotification.ExpirationTime = DateTimeOffset.Now.AddMinutes(10);
                    Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier(App.Aumid).Show(toastNotification);
                }

            });
        }
```

> Match the exact type names/usings already used by the digest handler in this file (it may already import `Windows.Data.Xml.Dom` and `Windows.UI.Notifications` so the fully-qualified names can be shortened to match surrounding code).

- [ ] **Step 3: Build and manual-verify**

Build (x64), run, open Speed Test tab, click **Run Speed Test**. Expected: after completion, an in-app banner appears; an OS toast appears only if **Show Toasts** is enabled in Settings.

- [ ] **Step 4: Commit**

```bash
git add NetworkMonitor/MainWindow.xaml.cs
git commit -m "Notify on speed test completion via in-app banner and toast."
```

---

## Task 13: Settings toggle for SpeedTestEnabled

**Files:**
- Modify: `NetworkMonitor/ViewModels/SettingsViewModel.cs`
- Modify: `NetworkMonitor/Views/SettingsPage.xaml`

**Interfaces:**
- Consumes: `Settings.SpeedTestEnabled` (Task 6).
- Produces: `SettingsViewModel.SpeedTestEnabled` bound to a `ToggleSwitch`.

- [ ] **Step 1: Add the VM property**

In `NetworkMonitor/ViewModels/SettingsViewModel.cs`:

In the constructor, after `_enableLogging = settings.EnableLogging;` (line 46), add:

```csharp
            _speedTestEnabled = settings.SpeedTestEnabled;
```

In the Properties section, after the `EnableLogging` property (line 210), add:

```csharp
        private bool _speedTestEnabled;

        public bool SpeedTestEnabled
        {
            get => _speedTestEnabled;
            set => SetProperty(ref _speedTestEnabled, value);
        }
```

In `PersistAll`, after `_settings.EnableLogging = EnableLogging;` (line 229), add:

```csharp
            _settings.SpeedTestEnabled = SpeedTestEnabled;
```

- [ ] **Step 2: Add the toggle to the page**

In `NetworkMonitor/Views/SettingsPage.xaml`, find an existing `ToggleSwitch` (e.g. the Show Toasts one) and add a sibling, following the file's exact element formatting, near the other notification/scan toggles:

```xml
        <ToggleSwitch
            Header="Run periodic speed test (hourly)"
            IsOn="{x:Bind ViewModel.SpeedTestEnabled, Mode=TwoWay}" />
```

> Place it inside the same container/`StackPanel` the other toggles live in, preserving the blank-line-around-element rule. Match the `Header` style of neighbouring toggles.

- [ ] **Step 3: Build and manual-verify**

Build (x64), run, open Settings. Expected: a "Run periodic speed test (hourly)" toggle appears, defaults **on**, and toggling it shows the "Settings saved" banner. With it **off**, the hourly background run is skipped (manual button still works).

- [ ] **Step 4: Commit**

```bash
git add NetworkMonitor/ViewModels/SettingsViewModel.cs NetworkMonitor/Views/SettingsPage.xaml
git commit -m "Add SpeedTestEnabled toggle to Settings."
```

---

## Task 14: Full verification pass

**Files:** none (verification only).

- [ ] **Step 1: Run all tests**

Run: `dotnet test NetworkMonitor.Tests/NetworkMonitor.Tests.csproj`
Expected: all tests pass, including the new `SpeedTestMathTests`, `SpeedTestMessageTests`, `SpeedTestCsvExporterTests`.

- [ ] **Step 2: Delete the DB and run the app**

Delete `%LocalAppData%\NetworkMonitor\networkmonitor.db` (and `-wal`/`-shm`). Build (x64) and run.

- [ ] **Step 3: Manual smoke checklist**

  - Traffic nav shows **Traffic** + **Speed Test** tabs.
  - **Run Speed Test** produces a result; latest tile + history grid + chart update.
  - In-app banner appears; OS toast appears iff Show Toasts is on.
  - **Export CSV** writes `Umnatha Network Monitor SpeedTests …​.csv` and opens it.
  - Settings toggle defaults on; turning it off skips background runs.
  - Hover over the chart shows the Mbps/time tooltip.

- [ ] **Step 4: Final commit (if any cleanup)**

```bash
git add -A
git commit -m "Finalise speed test feature."
```

---

## Self-Review Notes

- **Spec coverage:** Measurement (T4), data model (T1), worker (T5), purge fold-in (T6), settings flag (T6/T13), host+tab UI (T11), trend chart (T9), latest tile/run/history/grid (T10), completion notification (T12), CSV export (T7/T10) — all mapped.
- **DB delete:** stated (Global Constraints, T1, T14).
- **Type consistency:** `SpeedTestResult`, `SpeedChartPoint`, `SpeedTestCompletedEventArgs`, `RunNowAsync`, `SpeedTestCompleted`, `ChartPoints`, `IsRunning`, `RunNowCommand` used consistently across tasks.
- **Single-exit:** enforced everywhere, including view code — guards are expressed as positive `if` wrappers, not early `return`s.

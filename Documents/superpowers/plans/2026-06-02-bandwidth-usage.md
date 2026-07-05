# Bandwidth Usage Page Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Bandwidth page that captures per-app internet traffic via ETW, persists it to SQLite, and displays it as a GlassWire-style filled area chart with a per-app breakdown table.

**Architecture:** `BandwidthCollector` (BackgroundService) opens a kernel ETW session and accumulates TCP/UDP byte counts per PID in memory. On each scan tick, `BandwidthTracker` drains the accumulator, resolves PIDs to process names, and writes `BandwidthEntry` rows to the database. `BandwidthViewModel` queries and buckets those rows for the chart and table; `BandwidthPage` renders everything.

**Tech Stack:** .NET 10, WinUI 3, EF Core 10 + SQLite, CommunityToolkit.Mvvm, CommunityToolkit.WinUI.UI.Controls.DataGrid, Microsoft.Diagnostics.Tracing.TraceEvent (NuGet)

---

## File Map

| File | Action |
|---|---|
| `NetworkMonitor.csproj` | Add NuGet: `Microsoft.Diagnostics.Tracing.TraceEvent` |
| `NetworkMonitor/app.manifest` | Create/update — add `requireAdministrator` |
| `NetworkMonitor/Models/BandwidthEntry.cs` | Create |
| `NetworkMonitor/Data/AppDbContext.cs` | Add `BandwidthEntries` DbSet + index + `EnsureBandwidthEntriesTableAsync()` |
| `NetworkMonitor/Services/ScanWorker.cs` | Add bandwidth purge to `PurgeOldHistoryAsync` |
| `NetworkMonitor/Services/BandwidthCollector.cs` | Create — ETW session + accumulator |
| `NetworkMonitor/Services/BandwidthTracker.cs` | Create — drain on scan tick + write to DB |
| `NetworkMonitor/Models/ChartPoint.cs` | Create — record |
| `NetworkMonitor/Models/BandwidthAppRow.cs` | Create — record |
| `NetworkMonitor/ViewModels/BandwidthViewModel.cs` | Create |
| `NetworkMonitor/Views/Controls/BandwidthAreaChart.xaml` | Create — UserControl XAML |
| `NetworkMonitor/Views/Controls/BandwidthAreaChart.xaml.cs` | Create — path geometry generation |
| `NetworkMonitor/Views/BandwidthPage.xaml` | Create |
| `NetworkMonitor/Views/BandwidthPage.xaml.cs` | Create |
| `NetworkMonitor/MainWindow.xaml` | Add Bandwidth nav item as first entry |
| `NetworkMonitor/MainWindow.xaml.cs` | Add `bandwidth` route + `NavView_Loaded` default |
| `NetworkMonitor/App.xaml.cs` | Register services + ViewModel |

---

## Task 1: NuGet Package + App Manifest

**Files:**
- Modify: `NetworkMonitor/NetworkMonitor.csproj`
- Create/Modify: `NetworkMonitor/app.manifest`

- [ ] **Step 1: Add TraceEvent NuGet package**

Run in `NetworkMonitor/` directory:
```
dotnet add package Microsoft.Diagnostics.Tracing.TraceEvent
```

Expected: Package added to `NetworkMonitor.csproj`.

- [ ] **Step 2: Check for existing app.manifest**

Look in `NetworkMonitor/` for `app.manifest`. If it exists, open it. If not, check the `.csproj` for `<ApplicationManifest>` — create or update accordingly.

- [ ] **Step 3: Ensure manifest requires administrator**

The `app.manifest` must contain (create the full file if missing):

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
    <assemblyIdentity
        version="1.0.0.0"
        name="NetworkMonitor.app"/>
    <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
        <security>
            <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
                <requestedExecutionLevel
                    level="requireAdministrator"
                    uiAccess="false" />
            </requestedPrivileges>
        </security>
    </trustInfo>
    <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
        <application>
            <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}"/>
        </application>
    </compatibility>
</assembly>
```

If a manifest already exists, merge in the `requestedExecutionLevel` element — do not replace any existing content.

- [ ] **Step 4: Ensure .csproj references the manifest**

In `NetworkMonitor.csproj`, confirm there is a `<PropertyGroup>` entry:
```xml
<ApplicationManifest>app.manifest</ApplicationManifest>
```

Add it if missing.

- [ ] **Step 5: Build to verify**

Build the solution (x64). Expected: no errors. ETW package reference resolves.

- [ ] **Step 6: Commit**

```bash
git add NetworkMonitor/NetworkMonitor.csproj NetworkMonitor/app.manifest
git commit -m "Add TraceEvent NuGet and require administrator manifest"
```

---

## Task 2: Data Model + DB Schema + Purge

**Files:**
- Create: `NetworkMonitor/NetworkMonitor/Models/BandwidthEntry.cs`
- Modify: `NetworkMonitor/NetworkMonitor/Data/AppDbContext.cs`
- Modify: `NetworkMonitor/NetworkMonitor/Services/ScanWorker.cs`
- Modify: `NetworkMonitor/NetworkMonitor/App.xaml.cs`

- [ ] **Step 1: Create BandwidthEntry model**

Create `NetworkMonitor/NetworkMonitor/Models/BandwidthEntry.cs`:

```csharp
namespace NetworkMonitor.Models
{
    public class BandwidthEntry
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

- [ ] **Step 2: Add DbSet and index to AppDbContext**

In `AppDbContext.cs`, add the DbSet property after the existing ones:

```csharp
public DbSet<BandwidthEntry> BandwidthEntries => Set<BandwidthEntry>();
```

Add to `OnModelCreating` after the existing index definitions:

```csharp
modelBuilder.Entity<BandwidthEntry>()
    .HasIndex(entry => new { entry.Timestamp, entry.ProcessName });
```

- [ ] **Step 3: Add EnsureBandwidthEntriesTableAsync to AppDbContext**

Add this method to `AppDbContext.cs` after `EnsureDeviceEventsTableAsync`:

```csharp
public async Task EnsureBandwidthEntriesTableAsync()
{
    await Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS BandwidthEntries (
            Id          INTEGER PRIMARY KEY AUTOINCREMENT,
            Timestamp   TEXT    NOT NULL,
            ProcessName TEXT    NOT NULL,
            BytesSent   INTEGER NOT NULL,
            BytesReceived INTEGER NOT NULL
        )
        """);

    await Database.ExecuteSqlRawAsync("""
        CREATE INDEX IF NOT EXISTS IX_BandwidthEntries_Timestamp_ProcessName
        ON BandwidthEntries (Timestamp, ProcessName)
        """);
}
```

- [ ] **Step 4: Call EnsureBandwidthEntriesTableAsync in App.xaml.cs**

In `App.xaml.cs` `OnLaunched`, add after the existing `EnsureDeviceEventsTableAsync` call:

```csharp
await db.EnsureBandwidthEntriesTableAsync();
```

- [ ] **Step 5: Add bandwidth purge to ScanWorker**

In `ScanWorker.PurgeOldHistoryAsync`, add after the existing `ExecuteDeleteAsync` calls:

```csharp
await db.BandwidthEntries
    .Where(entry => entry.Timestamp < cutoff)
    .ExecuteDeleteAsync(ct);
```

- [ ] **Step 6: Build to verify**

Build the solution. Expected: no errors.

- [ ] **Step 7: Commit**

```bash
git add NetworkMonitor/NetworkMonitor/Models/BandwidthEntry.cs \
        NetworkMonitor/NetworkMonitor/Data/AppDbContext.cs \
        NetworkMonitor/NetworkMonitor/Services/ScanWorker.cs \
        NetworkMonitor/NetworkMonitor/App.xaml.cs
git commit -m "Add BandwidthEntry model, DB schema, and purge"
```

---

## Task 3: BandwidthCollector Service

**Files:**
- Create: `NetworkMonitor/NetworkMonitor/Services/BandwidthCollector.cs`

- [ ] **Step 1: Create BandwidthCollector**

Create `NetworkMonitor/NetworkMonitor/Services/BandwidthCollector.cs`:

```csharp
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Hosting;

namespace NetworkMonitor.Services
{
    public class BandwidthCollector : BackgroundService
    {
        private const string SessionName = "NetworkMonitorBandwidth";
        private readonly long[][] _counters = new long[65536][];
        private TraceEventSession? _session;

        public Dictionary<int, (long Sent, long Received)> DrainAndReset()
        {
            Dictionary<int, (long Sent, long Received)> snapshot = new();

            for (int index = 0; index < _counters.Length; index++)
            {
                long[]? entry = _counters[index];

                if (entry is null)
                {
                    continue;
                }

                long sent = Interlocked.Exchange(ref entry[0], 0);
                long received = Interlocked.Exchange(ref entry[1], 0);

                if (sent > 0 || received > 0)
                {
                    snapshot[index] = (sent, received);
                }
            }

            return snapshot;
        }

        protected override Task ExecuteAsync(CancellationToken ct)
        {
            _session = new TraceEventSession(SessionName);
            _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

            _session.Source.Kernel.TcpIpSend += args => AddBytes(args.ProcessID, args.size, sent: true);
            _session.Source.Kernel.TcpIpRecv += args => AddBytes(args.ProcessID, args.size, sent: false);
            _session.Source.Kernel.UdpIpSend += args => AddBytes(args.ProcessID, args.size, sent: true);
            _session.Source.Kernel.UdpIpRecv += args => AddBytes(args.ProcessID, args.size, sent: false);

            ct.Register(() => _session.Stop());

            return Task.Run(() => _session.Source.Process(), CancellationToken.None);
        }

        public override void Dispose()
        {
            _session?.Dispose();
            base.Dispose();
        }

        private void AddBytes(int pid, int bytes, bool sent)
        {
            if (pid < 0 || pid >= _counters.Length || bytes <= 0)
            {
                return;
            }

            if (_counters[pid] is null)
            {
                Interlocked.CompareExchange(ref _counters[pid], new long[2], null);
            }

            int slot = sent ? 0 : 1;
            Interlocked.Add(ref _counters[pid][slot], bytes);
        }
    }
}
```

> **Note:** PIDs on Windows are always 0–65535 (process handles are multiples of 4, max ~16 million, but PID values themselves fit in 16 bits for all practical purposes). The fixed array avoids dictionary allocation in the hot path. `Interlocked.CompareExchange` ensures the slot is initialised exactly once even under concurrency.

- [ ] **Step 2: Build to verify**

Build the solution. Expected: no errors. `TraceEventSession` and `KernelTraceEventParser` resolve from the NuGet package.

- [ ] **Step 3: Commit**

```bash
git add NetworkMonitor/NetworkMonitor/Services/BandwidthCollector.cs
git commit -m "Add BandwidthCollector ETW service"
```

---

## Task 4: BandwidthTracker Service

**Files:**
- Create: `NetworkMonitor/NetworkMonitor/Services/BandwidthTracker.cs`

- [ ] **Step 1: Create BandwidthTracker**

Create `NetworkMonitor/NetworkMonitor/Services/BandwidthTracker.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NetworkMonitor.Data;
using NetworkMonitor.Models;
using System.Diagnostics;

namespace NetworkMonitor.Services
{
    public class BandwidthTracker(
        BandwidthCollector collector,
        ScanWorker scanWorker,
        IDbContextFactory<AppDbContext> dbFactory) : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken ct)
        {
            scanWorker.ScanCompleted += OnScanCompleted;

            return Task.CompletedTask;
        }

        public override Task StopAsync(CancellationToken ct)
        {
            scanWorker.ScanCompleted -= OnScanCompleted;

            return base.StopAsync(ct);
        }

        private async void OnScanCompleted(object? sender, ScanCompletedEventArgs args)
        {
            Dictionary<int, (long Sent, long Received)> snapshot = collector.DrainAndReset();
            DateTime timestamp = DateTime.UtcNow;
            List<BandwidthEntry> entries = new();

            foreach (KeyValuePair<int, (long Sent, long Received)> kvp in snapshot)
            {
                try
                {
                    string processName = Process.GetProcessById(kvp.Key).ProcessName;
                    entries.Add(new BandwidthEntry
                    {
                        Timestamp = timestamp,
                        ProcessName = processName,
                        BytesSent = kvp.Value.Sent,
                        BytesReceived = kvp.Value.Received
                    });
                }
                catch (ArgumentException)
                {
                }
            }

            if (entries.Count == 0)
            {
                return;
            }

            await using AppDbContext db = await dbFactory.CreateDbContextAsync();
            db.BandwidthEntries.AddRange(entries);
            await db.SaveChangesAsync();
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Build the solution. Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add NetworkMonitor/NetworkMonitor/Services/BandwidthTracker.cs
git commit -m "Add BandwidthTracker service"
```

---

## Task 5: DI Registration

**Files:**
- Modify: `NetworkMonitor/NetworkMonitor/App.xaml.cs`

- [ ] **Step 1: Register BandwidthCollector**

In `App.xaml.cs`, inside `ConfigureServices`, add after the `ScanWorker` registrations:

```csharp
services.AddSingleton<BandwidthCollector>();
services.AddHostedService(sp => sp.GetRequiredService<BandwidthCollector>());
services.AddSingleton<BandwidthTracker>();
services.AddHostedService(sp => sp.GetRequiredService<BandwidthTracker>());
```

- [ ] **Step 2: Register BandwidthViewModel**

Add after the existing transient ViewModel registrations:

```csharp
services.AddTransient<BandwidthViewModel>();
```

- [ ] **Step 3: Add using directives**

Add to the using block in `App.xaml.cs` if not already present:

```csharp
using NetworkMonitor.ViewModels;
```

- [ ] **Step 4: Build to verify**

Build. Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor/NetworkMonitor/App.xaml.cs
git commit -m "Register bandwidth services and ViewModel in DI"
```

---

## Task 6: Supporting Types + BandwidthViewModel

**Files:**
- Create: `NetworkMonitor/NetworkMonitor/Models/ChartPoint.cs`
- Create: `NetworkMonitor/NetworkMonitor/Models/BandwidthAppRow.cs`
- Create: `NetworkMonitor/NetworkMonitor/ViewModels/BandwidthViewModel.cs`

- [ ] **Step 1: Create ChartPoint record**

Create `NetworkMonitor/NetworkMonitor/Models/ChartPoint.cs`:

```csharp
namespace NetworkMonitor.Models
{
    public record ChartPoint(DateTime BucketStart, long BytesSent, long BytesReceived);
}
```

- [ ] **Step 2: Create BandwidthAppRow record**

Create `NetworkMonitor/NetworkMonitor/Models/BandwidthAppRow.cs`:

```csharp
namespace NetworkMonitor.Models
{
    public record BandwidthAppRow(string? ProcessName, long BytesSent, long BytesReceived)
    {
        public long TotalBytes => BytesSent + BytesReceived;
        public bool IsAllApps => ProcessName is null;
        public string DisplayName => ProcessName ?? "All Apps";
    }
}
```

- [ ] **Step 3: Create BandwidthViewModel**

Create `NetworkMonitor/NetworkMonitor/ViewModels/BandwidthViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Dispatching;
using NetworkMonitor.Data;
using NetworkMonitor.Models;

namespace NetworkMonitor.ViewModels
{
    public partial class BandwidthViewModel : ObservableObject
    {
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        [ObservableProperty]
        private double _timeRangeHours = 24;

        [ObservableProperty]
        private string? _selectedApp;

        [ObservableProperty]
        private ObservableCollection<ChartPoint> _chartPoints = [];

        [ObservableProperty]
        private ObservableCollection<BandwidthAppRow> _apps = [];

        [ObservableProperty]
        private string _statusText = string.Empty;

        public BandwidthViewModel(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _dbFactory = dbFactory;
        }

        public async Task LoadAsync()
        {
            DateTime cutoff = DateTime.UtcNow.AddHours(-TimeRangeHours);
            TimeSpan bucketSize = BucketSizeFor(TimeRangeHours);

            await using AppDbContext db = await _dbFactory.CreateDbContextAsync();

            List<BandwidthEntry> allEntries = await db.BandwidthEntries
                .Where(entry => entry.Timestamp >= cutoff)
                .ToListAsync();

            IEnumerable<BandwidthEntry> chartSource = SelectedApp is null
                ? allEntries
                : allEntries.Where(entry => entry.ProcessName == SelectedApp);

            List<ChartPoint> chartPoints = chartSource
                .GroupBy(entry => BucketStart(entry.Timestamp, cutoff, bucketSize))
                .OrderBy(group => group.Key)
                .Select(group => new ChartPoint(
                    group.Key,
                    group.Sum(entry => entry.BytesSent),
                    group.Sum(entry => entry.BytesReceived)))
                .ToList();

            List<BandwidthAppRow> perAppRows = allEntries
                .GroupBy(entry => entry.ProcessName)
                .Select(group => new BandwidthAppRow(
                    group.Key,
                    group.Sum(entry => entry.BytesSent),
                    group.Sum(entry => entry.BytesReceived)))
                .OrderByDescending(row => row.TotalBytes)
                .ToList();

            long totalSent = perAppRows.Sum(row => row.BytesSent);
            long totalReceived = perAppRows.Sum(row => row.BytesReceived);
            BandwidthAppRow allAppsRow = new BandwidthAppRow(null, totalSent, totalReceived);

            List<BandwidthAppRow> displayRows = new List<BandwidthAppRow> { allAppsRow };
            displayRows.AddRange(perAppRows);

            string statusText = $"{perAppRows.Count} app{(perAppRows.Count == 1 ? string.Empty : "s")} · {FormatBytes(allAppsRow.TotalBytes)} total";

            _dispatcherQueue.TryEnqueue(() =>
            {
                ChartPoints = new ObservableCollection<ChartPoint>(chartPoints);
                Apps = new ObservableCollection<BandwidthAppRow>(displayRows);
                StatusText = statusText;
            });
        }

        partial void OnTimeRangeHoursChanged(double value)
        {
            _ = LoadAsync();
        }

        partial void OnSelectedAppChanged(string? value)
        {
            _ = LoadAsync();
        }

        public static TimeSpan BucketSizeFor(double hours)
        {
            TimeSpan result;

            if (hours <= 1)
            {
                result = TimeSpan.FromMinutes(5);
            }
            else if (hours <= 6)
            {
                result = TimeSpan.FromMinutes(30);
            }
            else if (hours <= 24)
            {
                result = TimeSpan.FromHours(1);
            }
            else if (hours <= 168)
            {
                result = TimeSpan.FromHours(6);
            }
            else
            {
                result = TimeSpan.FromDays(1);
            }

            return result;
        }

        public static string FormatBytes(long bytes)
        {
            string result;

            if (bytes >= 1_073_741_824L)
            {
                result = $"{bytes / 1_073_741_824.0:F1} GB";
            }
            else if (bytes >= 1_048_576L)
            {
                result = $"{bytes / 1_048_576.0:F1} MB";
            }
            else if (bytes >= 1_024L)
            {
                result = $"{bytes / 1_024.0:F1} KB";
            }
            else
            {
                result = $"{bytes} B";
            }

            return result;
        }

        private static DateTime BucketStart(DateTime timestamp, DateTime cutoff, TimeSpan bucketSize)
        {
            long ticksFromCutoff = (timestamp - cutoff).Ticks;
            long bucketIndex = ticksFromCutoff / bucketSize.Ticks;

            return cutoff + TimeSpan.FromTicks(bucketIndex * bucketSize.Ticks);
        }
    }
}
```

- [ ] **Step 4: Build to verify**

Build the solution. Expected: no errors. Source generators produce `OnTimeRangeHoursChanged` and `OnSelectedAppChanged` partial method hooks.

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor/NetworkMonitor/Models/ChartPoint.cs \
        NetworkMonitor/NetworkMonitor/Models/BandwidthAppRow.cs \
        NetworkMonitor/NetworkMonitor/ViewModels/BandwidthViewModel.cs
git commit -m "Add BandwidthViewModel and supporting types"
```

---

## Task 7: BandwidthAreaChart Control

**Files:**
- Create: `NetworkMonitor/NetworkMonitor/Views/Controls/BandwidthAreaChart.xaml`
- Create: `NetworkMonitor/NetworkMonitor/Views/Controls/BandwidthAreaChart.xaml.cs`

The control takes a list of `ChartPoint` values and renders two filled smooth-curve areas: blue (received) behind purple (sent). When data is empty it shows a flat baseline.

- [ ] **Step 1: Create the XAML**

Create `NetworkMonitor/NetworkMonitor/Views/Controls/BandwidthAreaChart.xaml`:

```xml
<?xml version="1.0" encoding="utf-8"?>

<UserControl
    x:Class="NetworkMonitor.Views.Controls.BandwidthAreaChart"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Canvas
        x:Name="ChartCanvas"
        SizeChanged="ChartCanvas_SizeChanged">

        <Canvas.Resources>
            <LinearGradientBrush
                x:Key="ReceivedFillBrush"
                StartPoint="0,0"
                EndPoint="0,1">
                <GradientStop
                    Color="#CC1976D2"
                    Offset="0" />
                <GradientStop
                    Color="#001976D2"
                    Offset="1" />
            </LinearGradientBrush>
            <LinearGradientBrush
                x:Key="SentFillBrush"
                StartPoint="0,0"
                EndPoint="0,1">
                <GradientStop
                    Color="#CCAB47BC"
                    Offset="0" />
                <GradientStop
                    Color="#00AB47BC"
                    Offset="1" />
            </LinearGradientBrush>
        </Canvas.Resources>

        <Path
            x:Name="ReceivedPath"
            Fill="{StaticResource ReceivedFillBrush}"
            Stroke="#1976D2"
            StrokeThickness="1.5" />

        <Path
            x:Name="SentPath"
            Fill="{StaticResource SentFillBrush}"
            Stroke="#AB47BC"
            StrokeThickness="1.5" />

    </Canvas>

</UserControl>
```

- [ ] **Step 2: Create the code-behind**

Create `NetworkMonitor/NetworkMonitor/Views/Controls/BandwidthAreaChart.xaml.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NetworkMonitor.Models;
using Windows.Foundation;

namespace NetworkMonitor.Views.Controls
{
    public sealed partial class BandwidthAreaChart : UserControl
    {
        public static readonly DependencyProperty ChartPointsProperty =
            DependencyProperty.Register(
                nameof(ChartPoints),
                typeof(IReadOnlyList<ChartPoint>),
                typeof(BandwidthAreaChart),
                new PropertyMetadata(null, OnChartPointsChanged));

        public IReadOnlyList<ChartPoint>? ChartPoints
        {
            get => (IReadOnlyList<ChartPoint>?)GetValue(ChartPointsProperty);
            set => SetValue(ChartPointsProperty, value);
        }

        public BandwidthAreaChart()
        {
            InitializeComponent();
        }

        private static void OnChartPointsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            BandwidthAreaChart chart = (BandwidthAreaChart)sender;
            chart.Redraw();
        }

        private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs args)
        {
            Redraw();
        }

        private void Redraw()
        {
            double width = ChartCanvas.ActualWidth;
            double height = ChartCanvas.ActualHeight;

            if (width <= 0 || height <= 0)
            {
                return;
            }

            IReadOnlyList<ChartPoint> points = ChartPoints ?? [];

            if (points.Count == 0)
            {
                ReceivedPath.Data = null;
                SentPath.Data = null;

                return;
            }

            long maxReceived = points.Max(point => point.BytesReceived);
            long maxSent = points.Max(point => point.BytesSent);
            long maxValue = Math.Max(maxReceived, Math.Max(maxSent, 1));

            ReceivedPath.Data = BuildAreaGeometry(points, width, height, maxValue, received: true);
            SentPath.Data = BuildAreaGeometry(points, width, height, maxValue, received: false);
        }

        private static Geometry BuildAreaGeometry(
            IReadOnlyList<ChartPoint> points,
            double width,
            double height,
            long maxValue,
            bool received)
        {
            double usableHeight = height * 0.90;
            int count = points.Count;

            Point[] pts = new Point[count];

            for (int index = 0; index < count; index++)
            {
                double xValue = count == 1 ? width / 2 : index * width / (count - 1);
                long bytes = received ? points[index].BytesReceived : points[index].BytesSent;
                double yValue = height - bytes * usableHeight / maxValue;
                pts[index] = new Point(xValue, yValue);
            }

            PathGeometry geometry = new PathGeometry();
            PathFigure figure = new PathFigure
            {
                StartPoint = pts[0],
                IsClosed = true,
                IsFilled = true
            };

            for (int index = 0; index < count - 1; index++)
            {
                double segmentWidth = pts[index + 1].X - pts[index].X;
                double cp1X = pts[index].X + segmentWidth / 3;
                double cp2X = pts[index + 1].X - segmentWidth / 3;

                BezierSegment segment = new BezierSegment
                {
                    Point1 = new Point(cp1X, pts[index].Y),
                    Point2 = new Point(cp2X, pts[index + 1].Y),
                    Point3 = pts[index + 1]
                };

                figure.Segments.Add(segment);
            }

            figure.Segments.Add(new LineSegment { Point = new Point(pts[count - 1].X, height) });
            figure.Segments.Add(new LineSegment { Point = new Point(pts[0].X, height) });
            geometry.Figures.Add(figure);

            return geometry;
        }
    }
}
```

- [ ] **Step 3: Build to verify**

Build the solution. Expected: no errors. The `UserControl` compiles and the dependency property registers correctly.

- [ ] **Step 4: Commit**

```bash
git add NetworkMonitor/NetworkMonitor/Views/Controls/BandwidthAreaChart.xaml \
        NetworkMonitor/NetworkMonitor/Views/Controls/BandwidthAreaChart.xaml.cs
git commit -m "Add BandwidthAreaChart custom control"
```

---

## Task 8: BandwidthPage

**Files:**
- Create: `NetworkMonitor/NetworkMonitor/Views/BandwidthPage.xaml`
- Create: `NetworkMonitor/NetworkMonitor/Views/BandwidthPage.xaml.cs`

- [ ] **Step 1: Create BandwidthPage.xaml**

Create `NetworkMonitor/NetworkMonitor/Views/BandwidthPage.xaml`:

```xml
<?xml version="1.0" encoding="utf-8"?>

<Page
    x:Class="NetworkMonitor.Views.BandwidthPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:controls="using:CommunityToolkit.WinUI.UI.Controls"
    xmlns:chart="using:NetworkMonitor.Views.Controls"
    xmlns:models="using:NetworkMonitor.Models"
    Background="{ThemeResource ApplicationPageBackgroundThemeBrush}">

    <Grid
        RowDefinitions="Auto,Auto,*,Auto"
        Padding="16,12,16,12">

        <!-- Header row: title + time range buttons -->
        <Grid
            Grid.Row="0"
            ColumnDefinitions="*,Auto"
            Margin="0,0,0,4">
            <StackPanel
                Grid.Column="0"
                Spacing="2">
                <TextBlock
                    Text="Bandwidth"
                    Style="{StaticResource TitleTextBlockStyle}" />
                <TextBlock
                    Text="{x:Bind ViewModel.StatusText, Mode=OneWay}"
                    FontSize="12"
                    Opacity="0.55" />
            </StackPanel>
            <StackPanel
                Grid.Column="1"
                Orientation="Horizontal"
                Spacing="4"
                VerticalAlignment="Center">
                <Button
                    x:Name="Range30dButton"
                    Content="30d"
                    Click="RangeButton_Click"
                    Tag="720"
                    Padding="10,5" />
                <Button
                    x:Name="Range7dButton"
                    Content="7d"
                    Click="RangeButton_Click"
                    Tag="168"
                    Padding="10,5" />
                <Button
                    x:Name="Range24hButton"
                    Content="24h"
                    Click="RangeButton_Click"
                    Tag="24"
                    Padding="10,5"
                    Style="{StaticResource AccentButtonStyle}" />
                <Button
                    x:Name="Range6hButton"
                    Content="6h"
                    Click="RangeButton_Click"
                    Tag="6"
                    Padding="10,5" />
                <Button
                    x:Name="Range1hButton"
                    Content="1h"
                    Click="RangeButton_Click"
                    Tag="1"
                    Padding="10,5" />
            </StackPanel>
        </Grid>

        <!-- Chart area -->
        <Border
            Grid.Row="1"
            Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
            BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
            BorderThickness="1"
            CornerRadius="6"
            Padding="12"
            Margin="0,0,0,10">
            <StackPanel
                Spacing="6">
                <!-- Chart legend -->
                <Grid
                    ColumnDefinitions="*,Auto">
                    <TextBlock
                        Grid.Column="0"
                        x:Name="ChartLabel"
                        FontSize="11"
                        Opacity="0.55" />
                    <StackPanel
                        Grid.Column="1"
                        Orientation="Horizontal"
                        Spacing="12">
                        <StackPanel
                            Orientation="Horizontal"
                            Spacing="4">
                            <Rectangle
                                Width="10"
                                Height="10"
                                Fill="#1976D2"
                                RadiusX="2"
                                RadiusY="2" />
                            <TextBlock
                                Text="Received"
                                FontSize="11"
                                Opacity="0.7" />
                        </StackPanel>
                        <StackPanel
                            Orientation="Horizontal"
                            Spacing="4">
                            <Rectangle
                                Width="10"
                                Height="10"
                                Fill="#AB47BC"
                                RadiusX="2"
                                RadiusY="2" />
                            <TextBlock
                                Text="Sent"
                                FontSize="11"
                                Opacity="0.7" />
                        </StackPanel>
                    </StackPanel>
                </Grid>
                <!-- Area chart -->
                <chart:BandwidthAreaChart
                    x:Name="AreaChart"
                    Height="120"
                    ChartPoints="{x:Bind ViewModel.ChartPoints, Mode=OneWay}" />
                <!-- Time axis labels -->
                <Grid
                    x:Name="TimeLabelsGrid"
                    ColumnDefinitions="*,*,*,*,*">
                    <TextBlock
                        Grid.Column="0"
                        x:Name="TimeLabel0"
                        FontSize="10"
                        Opacity="0.45"
                        HorizontalAlignment="Left" />
                    <TextBlock
                        Grid.Column="1"
                        x:Name="TimeLabel1"
                        FontSize="10"
                        Opacity="0.45"
                        HorizontalAlignment="Center" />
                    <TextBlock
                        Grid.Column="2"
                        x:Name="TimeLabel2"
                        FontSize="10"
                        Opacity="0.45"
                        HorizontalAlignment="Center" />
                    <TextBlock
                        Grid.Column="3"
                        x:Name="TimeLabel3"
                        FontSize="10"
                        Opacity="0.45"
                        HorizontalAlignment="Center" />
                    <TextBlock
                        Grid.Column="4"
                        FontSize="10"
                        Opacity="0.45"
                        HorizontalAlignment="Right"
                        Text="now" />
                </Grid>
            </StackPanel>
        </Border>

        <!-- App table -->
        <controls:DataGrid
            Grid.Row="2"
            x:Name="AppGrid"
            ItemsSource="{x:Bind ViewModel.Apps, Mode=OneWay}"
            AutoGenerateColumns="False"
            IsReadOnly="True"
            GridLinesVisibility="Horizontal"
            SelectionMode="Single"
            SelectionChanged="AppGrid_SelectionChanged"
            BorderThickness="1"
            BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}">
            <controls:DataGrid.Columns>
                <controls:DataGridTemplateColumn
                    Header="Application"
                    Width="*">
                    <controls:DataGridTemplateColumn.CellTemplate>
                        <DataTemplate
                            x:DataType="models:BandwidthAppRow">
                            <TextBlock
                                Text="{x:Bind DisplayName}"
                                VerticalAlignment="Center"
                                Padding="8,0,0,0"
                                FontWeight="{x:Bind IsAllApps, Converter={StaticResource BoolToFontWeightConverter}}" />
                        </DataTemplate>
                    </controls:DataGridTemplateColumn.CellTemplate>
                </controls:DataGridTemplateColumn>
                <controls:DataGridTemplateColumn
                    Header="Sent"
                    Width="110">
                    <controls:DataGridTemplateColumn.CellTemplate>
                        <DataTemplate
                            x:DataType="models:BandwidthAppRow">
                            <TextBlock
                                Text="{x:Bind BytesSent, Converter={StaticResource BytesConverter}}"
                                Foreground="#AB47BC"
                                VerticalAlignment="Center"
                                HorizontalAlignment="Right"
                                Padding="0,0,8,0" />
                        </DataTemplate>
                    </controls:DataGridTemplateColumn.CellTemplate>
                </controls:DataGridTemplateColumn>
                <controls:DataGridTemplateColumn
                    Header="Received"
                    Width="110">
                    <controls:DataGridTemplateColumn.CellTemplate>
                        <DataTemplate
                            x:DataType="models:BandwidthAppRow">
                            <TextBlock
                                Text="{x:Bind BytesReceived, Converter={StaticResource BytesConverter}}"
                                Foreground="#1976D2"
                                VerticalAlignment="Center"
                                HorizontalAlignment="Right"
                                Padding="0,0,8,0" />
                        </DataTemplate>
                    </controls:DataGridTemplateColumn.CellTemplate>
                </controls:DataGridTemplateColumn>
                <controls:DataGridTemplateColumn
                    Header="Total"
                    Width="110">
                    <controls:DataGridTemplateColumn.CellTemplate>
                        <DataTemplate
                            x:DataType="models:BandwidthAppRow">
                            <TextBlock
                                Text="{x:Bind TotalBytes, Converter={StaticResource BytesConverter}}"
                                VerticalAlignment="Center"
                                HorizontalAlignment="Right"
                                Padding="0,0,8,0"
                                FontWeight="{x:Bind IsAllApps, Converter={StaticResource BoolToFontWeightConverter}}" />
                        </DataTemplate>
                    </controls:DataGridTemplateColumn.CellTemplate>
                </controls:DataGridTemplateColumn>
            </controls:DataGrid.Columns>
        </controls:DataGrid>

        <!-- Status bar -->
        <TextBlock
            Grid.Row="3"
            Text="{x:Bind ViewModel.StatusText, Mode=OneWay}"
            Margin="0,8,0,0"
            Opacity="0.65"
            FontSize="12" />

    </Grid>
</Page>
```

- [ ] **Step 2: Create BandwidthPage.xaml.cs**

Create `NetworkMonitor/NetworkMonitor/Views/BandwidthPage.xaml.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using NetworkMonitor.Models;
using NetworkMonitor.ViewModels;

namespace NetworkMonitor.Views
{
    public sealed partial class BandwidthPage : Page
    {
        public BandwidthViewModel ViewModel
        {
            get;
        }

        public BandwidthPage()
        {
            ViewModel = App.AppHost.Services.GetRequiredService<BandwidthViewModel>();
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs args)
        {
            base.OnNavigatedTo(args);
            UpdateChartLabel();
            UpdateTimeLabels();
            await ViewModel.LoadAsync();
        }

        private void RangeButton_Click(object sender, RoutedEventArgs args)
        {
            if (sender is Button button && double.TryParse(button.Tag?.ToString(), out double hours))
            {
                ViewModel.TimeRangeHours = hours;
                UpdateRangeButtonStyles(button);
                UpdateChartLabel();
                UpdateTimeLabels();
            }
        }

        private void UpdateRangeButtonStyles(Button activeButton)
        {
            Button[] allButtons = [Range30dButton, Range7dButton, Range24hButton, Range6hButton, Range1hButton];

            foreach (Button button in allButtons)
            {
                button.Style = button == activeButton
                    ? (Style)Application.Current.Resources["AccentButtonStyle"]
                    : null;
            }
        }

        private void UpdateChartLabel()
        {
            string appPart = ViewModel.SelectedApp ?? "All Apps";
            string rangePart = ViewModel.TimeRangeHours switch
            {
                1 => "last hour",
                6 => "last 6 hours",
                24 => "last 24 hours",
                168 => "last 7 days",
                _ => "last 30 days"
            };

            ChartLabel.Text = $"{appPart} — {rangePart}";
        }

        private void UpdateTimeLabels()
        {
            DateTime now = DateTime.Now;
            double hours = ViewModel.TimeRangeHours;
            DateTime start = now.AddHours(-hours);

            string format = hours <= 24 ? "HH:mm" : "dd MMM";

            TimeLabel0.Text = start.ToString(format);
            TimeLabel1.Text = now.AddHours(-hours * 0.75).ToString(format);
            TimeLabel2.Text = now.AddHours(-hours * 0.5).ToString(format);
            TimeLabel3.Text = now.AddHours(-hours * 0.25).ToString(format);
        }

        private void AppGrid_SelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            if (AppGrid.SelectedItem is BandwidthAppRow row)
            {
                ViewModel.SelectedApp = row.IsAllApps ? null : row.ProcessName;
                UpdateChartLabel();
            }
        }
    }
}
```

- [ ] **Step 3: Add BytesConverter and BoolToFontWeightConverter**

The XAML references two converters: `BytesConverter` and `BoolToFontWeightConverter`. Check `App.xaml` for existing converter registrations to see how they are declared. Add both converters:

Create `NetworkMonitor/NetworkMonitor/Converters/BytesConverter.cs`:

```csharp
using Microsoft.UI.Xaml.Data;
using NetworkMonitor.ViewModels;

namespace NetworkMonitor.Converters
{
    public class BytesConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            long bytes = value is long longValue ? longValue : 0;

            return BandwidthViewModel.FormatBytes(bytes);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
```

Create `NetworkMonitor/NetworkMonitor/Converters/BoolToFontWeightConverter.cs`:

```csharp
using Microsoft.UI.Xaml.Data;
using Windows.UI.Text;

namespace NetworkMonitor.Converters
{
    public class BoolToFontWeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isBold = value is bool boolValue && boolValue;

            return isBold ? FontWeights.SemiBold : FontWeights.Normal;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
```

Register both converters in `App.xaml` inside `<Application.Resources><ResourceDictionary>`, following the existing converter registration pattern:

```xml
<converters:BytesConverter x:Key="BytesConverter" />
<converters:BoolToFontWeightConverter x:Key="BoolToFontWeightConverter" />
```

Also add the `xmlns:converters` namespace declaration to the `<Application>` element if not already present:
```
xmlns:converters="using:NetworkMonitor.Converters"
```

- [ ] **Step 4: Build to verify**

Build the solution. Expected: no errors. The XAML compiles without binding or converter errors.

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor/NetworkMonitor/Views/BandwidthPage.xaml \
        NetworkMonitor/NetworkMonitor/Views/BandwidthPage.xaml.cs \
        NetworkMonitor/NetworkMonitor/Converters/BytesConverter.cs \
        NetworkMonitor/NetworkMonitor/Converters/BoolToFontWeightConverter.cs \
        NetworkMonitor/NetworkMonitor/App.xaml
git commit -m "Add BandwidthPage view and converters"
```

---

## Task 9: Navigation Wiring

**Files:**
- Modify: `NetworkMonitor/NetworkMonitor/MainWindow.xaml`
- Modify: `NetworkMonitor/NetworkMonitor/MainWindow.xaml.cs`

- [ ] **Step 1: Add Bandwidth as first nav item in MainWindow.xaml**

In `MainWindow.xaml`, insert as the **first child** of `<NavigationView.MenuItems>`, before the existing `Devices` item:

```xml
<NavigationViewItem
    Content="Bandwidth"
    Tag="bandwidth">
    <NavigationViewItem.Icon>
        <FontIcon
            Glyph="&#xE9F5;" />
    </NavigationViewItem.Icon>
</NavigationViewItem>
```

> Glyph `&#xE9F5;` is the "SpeedHigh" icon from Segoe MDL2 Assets, suitable for bandwidth/speed.

- [ ] **Step 2: Add bandwidth route in MainWindow.xaml.cs**

In `NavView_SelectionChanged`, add to the `switch` expression in `MainWindow.xaml.cs`:

```csharp
"bandwidth" => typeof(BandwidthPage),
```

Add the `using NetworkMonitor.Views;` directive if not already present (it should already be there).

- [ ] **Step 3: Update NavView_Loaded default**

`NavView_Loaded` currently navigates to `DevicesPage` at index 0. Since `Bandwidth` is now index 0, update it:

```csharp
private void NavView_Loaded(object sender, RoutedEventArgs args)
{
    NavView.SelectedItem = NavView.MenuItems[0];
    ContentFrame.Navigate(typeof(BandwidthPage));
}
```

- [ ] **Step 4: Build and run**

Build and run the app (x64, as Administrator — required for ETW). Expected:
- App launches with Bandwidth as the first nav item and the default page.
- The page shows "0 apps" until a scan completes.
- After the first scan tick, bandwidth entries appear in the table.
- Clicking a time range button updates the chart label.
- Clicking an app row filters the chart label to that app.

- [ ] **Step 5: Commit**

```bash
git add NetworkMonitor/NetworkMonitor/MainWindow.xaml \
        NetworkMonitor/NetworkMonitor/MainWindow.xaml.cs
git commit -m "Wire Bandwidth page into navigation as first entry"
```

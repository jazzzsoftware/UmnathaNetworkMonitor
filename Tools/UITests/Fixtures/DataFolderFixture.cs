using System.Text.Json;
using NetworkMonitor.Core.Charting;
using NetworkMonitor.Core.Traffic;

namespace NetworkMonitor.UITests.Fixtures
{
    // A throwaway %TEMP%\umnatha-uitests\<timestamp>\ folder holding a seeded networkmonitor.db
    // and a known settings.json. FolderPath is what AppUnderTest.LaunchLocalBuild/
    // LaunchInstalledBuild passes as UMNATHA_DATA_FOLDER, so the driven app never sees the
    // operator's real data.
    public sealed class DataFolderFixture
    {
        private const string RootFolderName = "umnatha-uitests";
        private const string DatabaseFileName = "networkmonitor.db";
        private const string SettingsFileName = "settings.json";

        private static readonly JsonSerializerOptions SettingsWriteOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private DataFolderFixture(string folderPath, SeedCounts counts)
        {
            FolderPath = folderPath;
            Counts = counts;
        }

        public string FolderPath
        {
            get;
        }

        public SeedCounts Counts
        {
            get;
        }

        public static async Task<DataFolderFixture> CreateAsync()
        {
            DateTime nowUtc = DateTime.UtcNow;
            string timestamp = nowUtc.ToString("yyyyMMdd-HHmmss-fff");
            string folderPath = Path.Combine(Path.GetTempPath(), RootFolderName, timestamp);

            Directory.CreateDirectory(folderPath);

            string dbPath = Path.Combine(folderPath, DatabaseFileName);
            SeedCounts counts = await SeedDatabase.BuildAsync(dbPath, nowUtc);

            string settingsPath = Path.Combine(folderPath, SettingsFileName);
            string settingsJson = JsonSerializer.Serialize(new FixtureSettings(), SettingsWriteOptions);

            await File.WriteAllTextAsync(settingsPath, settingsJson);

            DataFolderFixture fixture = new DataFolderFixture(folderPath, counts);

            return fixture;
        }

        // Mirrors NetworkMonitor.Services.Data.Settings by property name only — that type is
        // net10.0-windows/UseWinUI and cannot be referenced from this console host, the same
        // reason AppDbContext and friends are linked as source rather than project-referenced.
        // Every property here is deliberately explicit so the seeded environment is known and
        // reproducible; anything left off deserialises to Settings' own field-initializer
        // default, which is fine — JsonSerializer.Deserialize<Settings> tolerates missing
        // members and only a corrupt file falls back further, per App.xaml.cs.
        private sealed class FixtureSettings
        {
            public string SubnetBase
            {
                get;
                set;
            } = "192.168.50";

            public bool AutoDetectSubnet
            {
                get;
                set;
            } = false;

            public int StartHost
            {
                get;
                set;
            } = 1;

            public int EndHost
            {
                get;
                set;
            } = 254;

            public int IntervalMinutes
            {
                get;
                set;
            } = 5;

            public bool ShowToasts
            {
                get;
                set;
            } = false;

            public int HistoryPurgeDays
            {
                get;
                set;
            } = 30;

            public double InternetTimeRangeHours
            {
                get;
                set;
            } = 1.0;

            public double LocalTimeRangeHours
            {
                get;
                set;
            } = 1.0;

            // Pinned for Task 9, which was written against it and reads as nonsense without it.
            // It is the app's own default, but leaving it to the default made two assertions
            // depend on a value the fixture never stated: it is the bucket width the 5-minute
            // window uses (InternetViewModel.BucketSizeFor), so it sets both how many buckets that
            // window has and — through ChartDrawRange.FromBucketSeconds, which reads bucket width
            // alone — which range token the chart reports drawing. At anything above 1 the
            // 5-minute window reports itself as "1h", indistinguishable from the real 1-hour
            // window. That ambiguity is the chart summary's, not this fixture's, and is recorded
            // as amendment A on Task 9 in the plan.
            public int TrafficIntervalSeconds
            {
                get;
                set;
            } = 1;

            // Pinned for the same reason: LocalPage opens on whichever lens Settings last held,
            // and TrafficPhase asserts the By-app lens is what it finds before toggling to
            // By-device.
            public LocalLens LocalLens
            {
                get;
                set;
            } = LocalLens.ByApp;

            public bool DevicesOnlineOnly
            {
                get;
                set;
            } = false;

            public int TrafficPurgeDays
            {
                get;
                set;
            } = 7;

            public string ChartSchemeId
            {
                get;
                set;
            } = ChartSchemeCatalog.DefaultSchemeId;

            public bool SpeedTestEnabled
            {
                get;
                set;
            } = false;

            public bool AutoCheckForUpdates
            {
                get;
                set;
            } = false;

            public bool ShowMiniGraph
            {
                get;
                set;
            } = true;

            public bool EnableLogging
            {
                get;
                set;
            } = true;
        }
    }
}

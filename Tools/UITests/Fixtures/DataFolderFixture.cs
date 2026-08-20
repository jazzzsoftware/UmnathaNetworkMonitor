using System.Text.Json;
using NetworkMonitor.Core.Charting;

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

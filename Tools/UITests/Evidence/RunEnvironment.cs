using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;
using NetworkMonitor.Core.Charting;
using NetworkMonitor.Services.Data;
using NetworkMonitor.UITests.Runner;

namespace NetworkMonitor.UITests.Evidence
{
    public sealed class RunEnvironment
    {
        private const int EffectiveDpiType = 0;
        private const uint MonitorDefaultToPrimary = 1;

        private RunEnvironment(
            string appVersionBefore,
            string osBuild,
            double primaryMonitorDpiScale,
            string theme,
            string chartColourScheme,
            bool isElevated)
        {
            AppVersionBefore = appVersionBefore;
            OsBuild = osBuild;
            PrimaryMonitorDpiScale = primaryMonitorDpiScale;
            Theme = theme;
            ChartColourScheme = chartColourScheme;
            IsElevated = isElevated;
        }

        public string AppVersionBefore
        {
            get;
        }

        // Read() only ever sees "now", so it can only populate the before-run snapshot; the
        // caller sets this once the run (and any update-lifecycle phase) has finished, the same
        // post-construction-mutable pattern StepResult uses for ScreenshotPath/TreeDumpPath.
        public string AppVersionAfter
        {
            get;
            set;
        } = string.Empty;

        public string OsBuild
        {
            get;
        }

        public double PrimaryMonitorDpiScale
        {
            get;
        }

        public string Theme
        {
            get;
        }

        public string ChartColourScheme
        {
            get;
        }

        public bool IsElevated
        {
            get;
        }

        public static RunEnvironment Read()
        {
            string appVersionBefore = Preflight.ReadInstalledVersion();
            string osBuild = ReadOsBuild();
            double primaryMonitorDpiScale = ReadPrimaryMonitorDpiScale();
            string theme = ReadTheme();
            string chartColourScheme = ReadChartColourScheme();
            bool isElevated = ReadIsElevated();

            RunEnvironment environment = new RunEnvironment(
                appVersionBefore,
                osBuild,
                primaryMonitorDpiScale,
                theme,
                chartColourScheme,
                isElevated);

            return environment;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(Point point, uint flags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitorHandle, int dpiType, out uint dpiX, out uint dpiY);

        private static string ReadOsBuild()
        {
            string buildNumber = string.Empty;

            using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {

                if (key is not null)
                {
                    buildNumber = key.GetValue("CurrentBuildNumber") as string ?? string.Empty;
                }

            }

            string osBuild = buildNumber.Length > 0
                ? $"{Environment.OSVersion.VersionString} (build {buildNumber})"
                : Environment.OSVersion.VersionString;

            return osBuild;
        }

        private static double ReadPrimaryMonitorDpiScale()
        {
            double scale = 1.0;

            try
            {
                IntPtr monitorHandle = MonitorFromPoint(new Point(0, 0), MonitorDefaultToPrimary);
                int hresult = GetDpiForMonitor(monitorHandle, EffectiveDpiType, out uint dpiX, out uint dpiY);

                if (hresult == 0)
                {
                    scale = dpiX / 96.0;
                }

            }
            catch (Exception)
            {
                scale = 1.0;
            }

            return scale;
        }

        private static string ReadTheme()
        {
            string theme = "Unknown";

            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {

                if (key is not null)
                {
                    object? rawValue = key.GetValue("AppsUseLightTheme");

                    if (rawValue is int lightThemeFlag)
                    {
                        theme = lightThemeFlag == 0 ? "Dark" : "Light";
                    }

                }

            }

            return theme;
        }

        private static string ReadChartColourScheme()
        {
            string schemeId = string.Empty;
            string settingsPath = Path.Combine(AppPaths.AppDataFolder, "settings.json");

            try
            {

                if (File.Exists(settingsPath))
                {
                    string settingsJson = File.ReadAllText(settingsPath);

                    using (JsonDocument document = JsonDocument.Parse(settingsJson))
                    {

                        if (document.RootElement.TryGetProperty("ChartSchemeId", out JsonElement schemeElement))
                        {
                            schemeId = schemeElement.GetString() ?? string.Empty;
                        }

                    }

                }

            }
            catch (Exception)
            {
                schemeId = string.Empty;
            }

            ChartSchemePreset preset = ChartSchemeCatalog.Resolve(schemeId);
            string displayName = preset.DisplayName;

            return displayName;
        }

        private static bool ReadIsElevated()
        {

            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);

                bool elevated = principal.IsInRole(WindowsBuiltInRole.Administrator);

                return elevated;
            }

        }
    }
}

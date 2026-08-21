using System.Text.Json;

namespace NetworkMonitor.UITests.Fixtures
{
    // Reads one property straight out of the fixture's settings.json, as raw JSON text.
    //
    // This is the whole point of SettingsPhase: a setting is only proven saved when the file on
    // disk says so. Reading the value back off the control would pass for a setting whose UI
    // binding works and whose persistence does not — the class of defect commit 3a822b8 fixed.
    //
    // Values come back as raw JSON (`true`, `7`, `"classic"`) rather than parsed types, so one
    // comparison covers every setting kind without this having to know which is which. Any read
    // failure returns empty: Settings.Save writes through AtomicFile, but a read that lands
    // mid-replace should retry rather than throw, and every caller polls.
    public static class SettingsFileReader
    {
        private const string SettingsFileName = "settings.json";

        public static string ReadValue(string dataFolder, string propertyName)
        {
            string value = string.Empty;

            try
            {
                string settingsPath = Path.Combine(dataFolder, SettingsFileName);

                if (File.Exists(settingsPath))
                {
                    string json = File.ReadAllText(settingsPath);

                    using (JsonDocument document = JsonDocument.Parse(json))
                    {

                        if (document.RootElement.TryGetProperty(propertyName, out JsonElement property))
                        {
                            value = property.GetRawText();
                        }

                    }

                }

            }
            catch (Exception)
            {
                value = string.Empty;
            }

            return value;
        }
    }
}

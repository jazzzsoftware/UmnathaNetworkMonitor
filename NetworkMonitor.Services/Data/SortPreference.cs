using System.Text.Json;
using NetworkMonitor.Services.Platform;

namespace NetworkMonitor.Services.Data
{
    public record SortPreference(string Property, bool Ascending)
    {
        private static string FilePath(string pageKey)
        {
            string filePath = Path.Combine(
                AppPaths.AppDataFolder,
                $"sort-{pageKey}.json");

            return filePath;
        }

        public static SortPreference? Load(string pageKey)
        {
            string path = FilePath(pageKey);
            SortPreference? result = null;

            if (File.Exists(path))
            {

                try
                {
                    result = JsonSerializer.Deserialize<SortPreference>(File.ReadAllText(path));
                }
                catch (JsonException exception)
                {
                    AppLog.Error("SortPreference.Load", exception);
                }
                catch (IOException exception)
                {
                    AppLog.Error("SortPreference.Load", exception);
                }

            }

            return result;
        }

        public void Save(string pageKey)
        {
            string json = JsonSerializer.Serialize(this);

            AtomicFile.WriteAllText(FilePath(pageKey), json);
        }
    }
}
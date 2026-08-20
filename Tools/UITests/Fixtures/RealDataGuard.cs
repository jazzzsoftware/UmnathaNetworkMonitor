using Microsoft.Data.Sqlite;
using NetworkMonitor.Core.Common;

namespace NetworkMonitor.UITests.Fixtures
{
    // The one place the suite is allowed to touch the operator's real data folder. CopyAside
    // always copies — never moves — so a hard kill mid-phase leaves the original exactly where
    // the app expects to find it (README's "Recovering a stranded backup" section documents the
    // manual recovery for exactly that case). Restore only ever deletes the backup once the
    // restored database has been opened and its row counts checked against the manifest CopyAside
    // wrote alongside it.
    public static class RealDataGuard
    {
        private const string DatabaseFileName = "networkmonitor.db";
        private const string ManifestFileName = "uitest-row-counts.txt";
        private const string BackupSuffix = ".uitest-backup-";
        private const long NoDatabaseSentinel = -1L;

        private static readonly string[] TrackedTables =
        {
            "Devices",
            "ScanSessions",
            "DeviceEvents",
            "TrafficEntries",
            "TrafficRollups",
            "LocalTrafficEntries",
            "LocalTrafficRollups",
            "DigestReports",
            "SpeedTestResults"
        };

        public static string CopyAside()
        {
            string realFolder = ResolveRealDataFolder();
            string backupFolder = realFolder + BackupSuffix + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

            if (Directory.Exists(realFolder))
            {
                CopyDirectoryRecursive(realFolder, backupFolder, null);
            }
            else
            {
                Directory.CreateDirectory(backupFolder);
            }

            Dictionary<string, long> rowCounts = CountRows(Path.Combine(backupFolder, DatabaseFileName));

            WriteManifest(Path.Combine(backupFolder, ManifestFileName), rowCounts);

            return backupFolder;
        }

        public static bool Restore(string backupPath)
        {
            bool restored = false;
            Exception? failure = null;

            try
            {
                Dictionary<string, long> expectedRowCounts = ReadManifest(Path.Combine(backupPath, ManifestFileName));
                string realFolder = ResolveRealDataFolder();

                if (Directory.Exists(realFolder))
                {
                    Directory.Delete(realFolder, true);
                }

                Directory.CreateDirectory(realFolder);
                CopyDirectoryRecursive(backupPath, realFolder, ManifestFileName);

                Dictionary<string, long> restoredRowCounts = CountRows(Path.Combine(realFolder, DatabaseFileName));
                bool countsMatch = RowCountsMatch(expectedRowCounts, restoredRowCounts);

                if (countsMatch)
                {
                    Directory.Delete(backupPath, true);
                }

                restored = countsMatch;
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (!restored)
            {
                ReportFailure(backupPath, failure);
            }

            return restored;
        }

        // Deliberately never honours UMNATHA_DATA_FOLDER: a guard that could be redirected by
        // the same override the fixture uses would guard nothing. This is the one call in the
        // whole suite that must always resolve to the operator's real folder.
        private static string ResolveRealDataFolder()
        {
            string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string realFolder = AppDataFolderResolver.Resolve(null, localApplicationData);

            return realFolder;
        }

        private static void CopyDirectoryRecursive(string sourceFolder, string destinationFolder, string? excludeFileName)
        {
            Directory.CreateDirectory(destinationFolder);

            foreach (string filePath in Directory.GetFiles(sourceFolder))
            {
                string fileName = Path.GetFileName(filePath);
                bool skip = excludeFileName is not null && string.Equals(fileName, excludeFileName, StringComparison.OrdinalIgnoreCase);

                if (!skip)
                {
                    string destinationFile = Path.Combine(destinationFolder, fileName);
                    File.Copy(filePath, destinationFile, true);
                }

            }

            foreach (string sourceSubFolder in Directory.GetDirectories(sourceFolder))
            {
                string subFolderName = Path.GetFileName(sourceSubFolder);
                string destinationSubFolder = Path.Combine(destinationFolder, subFolderName);

                CopyDirectoryRecursive(sourceSubFolder, destinationSubFolder, excludeFileName);
            }

        }

        private static Dictionary<string, long> CountRows(string databasePath)
        {
            Dictionary<string, long> rowCounts = new Dictionary<string, long>();

            if (File.Exists(databasePath))
            {

                using (SqliteConnection connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly"))
                {
                    connection.Open();

                    foreach (string table in TrackedTables)
                    {
                        rowCounts[table] = CountRowsInTable(connection, table);
                    }

                }

            }
            else
            {

                foreach (string table in TrackedTables)
                {
                    rowCounts[table] = NoDatabaseSentinel;
                }

            }

            return rowCounts;
        }

        private static long CountRowsInTable(SqliteConnection connection, string table)
        {
            long count = NoDatabaseSentinel;
            bool exists = TableExists(connection, table);

            if (exists)
            {

                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";

                    object? result = command.ExecuteScalar();

                    count = result is null ? 0 : Convert.ToInt64(result);
                }

            }

            return count;
        }

        private static bool TableExists(SqliteConnection connection, string table)
        {
            bool exists;

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
                command.Parameters.AddWithValue("$name", table);

                object? result = command.ExecuteScalar();

                exists = result is not null && Convert.ToInt64(result) > 0;
            }

            return exists;
        }

        private static void WriteManifest(string manifestPath, Dictionary<string, long> rowCounts)
        {
            List<string> lines = new List<string>();

            foreach (string table in TrackedTables)
            {
                lines.Add($"{table}={rowCounts[table]}");
            }

            File.WriteAllLines(manifestPath, lines);
        }

        private static Dictionary<string, long> ReadManifest(string manifestPath)
        {
            Dictionary<string, long> rowCounts = new Dictionary<string, long>();

            if (File.Exists(manifestPath))
            {

                foreach (string line in File.ReadAllLines(manifestPath))
                {
                    string[] parts = line.Split('=');

                    if (parts.Length == 2 && long.TryParse(parts[1], out long count))
                    {
                        rowCounts[parts[0]] = count;
                    }

                }

            }

            return rowCounts;
        }

        private static bool RowCountsMatch(Dictionary<string, long> expected, Dictionary<string, long> actual)
        {
            bool allMatch = true;

            foreach (string table in TrackedTables)
            {
                long expectedCount = expected.TryGetValue(table, out long expectedValue) ? expectedValue : NoDatabaseSentinel;
                long actualCount = actual.TryGetValue(table, out long actualValue) ? actualValue : NoDatabaseSentinel;

                if (expectedCount != actualCount)
                {
                    allMatch = false;
                }

            }

            return allMatch;
        }

        private static void ReportFailure(string backupPath, Exception? failure)
        {
            Console.WriteLine();
            Console.WriteLine("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
            Console.WriteLine("!!  RESTORE FAILED. YOUR REAL DATA IS SAFE, BUT NOT WHERE THE APP EXPECTS IT.  !!");
            Console.WriteLine($"!!  IT IS BACKED UP AT: {backupPath}");
            Console.WriteLine("!!  DO NOT DELETE THAT FOLDER. See Tools/UITests/README.md to restore it by hand.");
            Console.WriteLine("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");

            if (failure is not null)
            {
                Console.WriteLine($"!!  Reported error: {failure.Message}");
            }

            Console.WriteLine();
        }
    }
}

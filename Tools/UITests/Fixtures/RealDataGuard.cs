using System.Diagnostics;
using Microsoft.Data.Sqlite;
using NetworkMonitor.Core.Common;

namespace NetworkMonitor.UITests.Fixtures
{
    // The one place the suite is allowed to touch the operator's real data folder. CopyAside
    // always copies — never moves — so a hard kill mid-phase leaves the original exactly where
    // the app expects to find it (README's "Recovering a stranded backup" section documents the
    // manual recovery for exactly that case). Row counts are captured from the LIVE database
    // BEFORE it is copied, never from the copy afterwards — a torn copy must not be able to
    // bless itself as the expected truth. Restore validates the backup fully before touching the
    // real folder at all, restores into a sibling staging folder, and only swaps it into place
    // (rename, not delete-then-copy) once the staged copy's row counts have been checked against
    // that pre-copy manifest — there is never a window where neither copy exists.
    //
    // The internal (string realFolder) overloads exist so --guard-selftest can exercise this
    // whole class against a throwaway folder it builds itself. The public, parameterless
    // CopyAside()/Restore(string) — the only entry points real phases call — always resolve the
    // operator's true real folder and cannot be redirected.
    public static class RealDataGuard
    {
        private const string DatabaseFileName = "networkmonitor.db";
        private const string ManifestFileName = "uitest-row-counts.txt";
        private const string BackupSuffix = ".uitest-backup-";
        private const string RestoreStagingSuffix = ".uitest-restore-staging-";
        private const string DisplacedSuffix = ".uitest-displaced-";
        private const string NetworkMonitorProcessName = "NetworkMonitor";
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
            string backupFolder = CopyAside(realFolder);

            return backupFolder;
        }

        public static bool Restore(string backupPath)
        {
            string realFolder = ResolveRealDataFolder();
            bool restored = Restore(backupPath, realFolder);

            return restored;
        }

        // realFolder is an explicit parameter (rather than always calling ResolveRealDataFolder
        // internally) purely so --guard-selftest can drive this method against a throwaway
        // folder. Internal, not public: real callers must go through the parameterless overload
        // above so the operator's real folder can never be substituted by a caller's mistake.
        internal static string CopyAside(string realFolder)
        {
            EnsureAppIsNotRunning();

            string liveDatabasePath = Path.Combine(realFolder, DatabaseFileName);

            if (File.Exists(liveDatabasePath))
            {
                TryCheckpointWal(liveDatabasePath);
            }

            // Counted here, on the live database, before a single byte is copied. Counting the
            // backup afterwards would let a torn copy record itself as correct.
            Dictionary<string, long> rowCounts = CountRows(liveDatabasePath);
            string backupFolder = BuildUniqueSiblingFolderPath(realFolder, BackupSuffix);

            if (Directory.Exists(realFolder))
            {
                CopyDirectoryRecursive(realFolder, backupFolder, null);
            }
            else
            {
                Directory.CreateDirectory(backupFolder);
            }

            WriteManifest(Path.Combine(backupFolder, ManifestFileName), rowCounts);

            return backupFolder;
        }

        internal static bool Restore(string backupPath, string realFolder)
        {
            bool restored = false;
            Exception? failure = null;

            try
            {
                EnsureAppIsNotRunning();
                ValidateBackupOrThrow(backupPath);

                Dictionary<string, long> expectedRowCounts = ReadManifest(Path.Combine(backupPath, ManifestFileName));
                string stagingFolder = BuildUniqueSiblingFolderPath(realFolder, RestoreStagingSuffix);

                // Anything that goes wrong between here and a successful swap — CopyDirectoryRecursive
                // throwing partway, or CountRows throwing on the staged copy — must not leave a full
                // copy of the operator's data sitting in a throwaway staging folder.
                try
                {
                    CopyDirectoryRecursive(backupPath, stagingFolder, ManifestFileName);

                    Dictionary<string, long> restoredRowCounts = CountRows(Path.Combine(stagingFolder, DatabaseFileName));
                    bool countsMatch = RowCountsMatch(expectedRowCounts, restoredRowCounts);

                    if (!countsMatch)
                    {
                        throw new InvalidOperationException(
                            "Restored row counts did not match the manifest captured before the original was copied aside. "
                            + $"The staged copy was discarded and the backup is left untouched at {backupPath}.");
                    }

                }
                catch (Exception)
                {
                    TryDeleteStagingFolder(stagingFolder);

                    throw;
                }

                SwapInStagedFolder(realFolder, stagingFolder);

                restored = true;

                TryDeleteBackup(backupPath);
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

        private static void EnsureAppIsNotRunning()
        {
            Process[] runningProcesses = Process.GetProcessesByName(NetworkMonitorProcessName);
            bool isRunning = runningProcesses.Length > 0;

            foreach (Process process in runningProcesses)
            {
                process.Dispose();
            }

            if (isRunning)
            {
                throw new InvalidOperationException(
                    $"{NetworkMonitorProcessName}.exe is still running. Its data folder cannot be safely copied or "
                    + "restored while the app could still be writing to networkmonitor.db — a checkpoint landing "
                    + "mid-copy can tear the .db/-wal pair and silently lose recent history. Shut it down first "
                    + "(InstalledApp.ShutDown) and confirm it has exited before calling this.");
            }

        }

        // Best-effort: merges the WAL into the main file and truncates it, so the file copy that
        // follows sees a consistent, checkpoint-free .db rather than a .db/-wal pair that could
        // be torn between the two File.Copy calls. Swallows failures — EnsureAppIsNotRunning is
        // the hard guarantee; this only reduces the risk further when it can.
        private static void TryCheckpointWal(string databasePath)
        {

            try
            {

                using (SqliteConnection connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
                {
                    connection.Open();

                    using (SqliteCommand command = connection.CreateCommand())
                    {
                        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";

                        command.ExecuteNonQuery();
                    }

                }

            }
            catch (Exception exception)
            {
                Console.WriteLine(
                    $"Could not checkpoint the WAL for {databasePath} before copying it aside ({exception.Message}). "
                    + "The backup may carry an un-checkpointed WAL. EnsureAppIsNotRunning is still the safety "
                    + "guarantee against a torn copy; this only reduces the risk further when a checkpoint succeeds.");
            }

        }

        // Everything Restore must be sure of before it deletes or moves anything real: a real
        // path, a folder that exists, a manifest that parses to a full set of tracked tables, and
        // — only when that manifest says a database existed — the database file itself.
        private static void ValidateBackupOrThrow(string backupPath)
        {

            if (string.IsNullOrWhiteSpace(backupPath))
            {
                throw new ArgumentException("Restore was given an empty or missing backup path.", nameof(backupPath));
            }

            if (!Directory.Exists(backupPath))
            {
                throw new InvalidOperationException($"Restore was given a backup path that does not exist: {backupPath}");
            }

            string manifestPath = Path.Combine(backupPath, ManifestFileName);

            if (!File.Exists(manifestPath))
            {
                throw new InvalidOperationException(
                    $"Backup at {backupPath} has no {ManifestFileName} manifest — refusing to restore an unverifiable backup.");
            }

            Dictionary<string, long> manifestCounts = ReadManifest(manifestPath);

            if (manifestCounts.Count != TrackedTables.Length)
            {
                throw new InvalidOperationException(
                    $"Backup at {backupPath}'s manifest is missing or unparsable entries — refusing to restore an unverifiable backup.");
            }

            bool manifestExpectsDatabase = manifestCounts.Values.Any(count => count != NoDatabaseSentinel);
            string databasePath = Path.Combine(backupPath, DatabaseFileName);

            if (manifestExpectsDatabase && !File.Exists(databasePath))
            {
                throw new InvalidOperationException(
                    $"Backup at {backupPath} is missing {DatabaseFileName} even though its manifest expects one — refusing to restore.");
            }

        }

        // Moves the current real folder aside (rename, not delete), moves the staged, validated
        // copy into place, then discards the displaced original. If the second move fails, the
        // displaced original is moved straight back — realFolder is never left absent. Neither
        // cleanup step (rollback, final delete) is allowed to throw: a failure there must never
        // be reported as a failed restore when the swap itself already succeeded (or, on the
        // rollback path, must never replace the exception that explains why it failed).
        //
        // beforeSecondMoveForTesting is a seam for --guard-selftest only: it runs after the first
        // move (real -> displaced) and before the second (staging -> real), so the self-test can
        // force the second move to fail — e.g. by deleting stagingFolder — and assert the
        // rollback puts the original folder back intact. Always null in production; Restore never
        // passes it.
        internal static void SwapInStagedFolder(string realFolder, string stagingFolder, Action? beforeSecondMoveForTesting = null)
        {
            string displacedFolder = BuildUniqueSiblingFolderPath(realFolder, DisplacedSuffix);

            if (Directory.Exists(realFolder))
            {
                Directory.Move(realFolder, displacedFolder);
            }

            beforeSecondMoveForTesting?.Invoke();

            try
            {
                Directory.Move(stagingFolder, realFolder);
            }
            catch (Exception moveFailure)
            {
                RollBackDisplacedFolder(realFolder, displacedFolder, moveFailure);

                throw;
            }

            TryDeleteDisplacedFolder(displacedFolder);
        }

        private static void RollBackDisplacedFolder(string realFolder, string displacedFolder, Exception originalFailure)
        {

            try
            {

                if (Directory.Exists(displacedFolder) && !Directory.Exists(realFolder))
                {
                    Directory.Move(displacedFolder, realFolder);
                }

            }
            catch (Exception rollbackFailure)
            {
                Console.WriteLine(
                    $"Swap failed ({originalFailure.Message}) and moving the original folder back also failed "
                    + $"({rollbackFailure.Message}). The original data should still be intact at {displacedFolder} — "
                    + "recover it by hand before doing anything else. See Tools/UITests/README.md.");
            }

        }

        private static void TryDeleteDisplacedFolder(string displacedFolder)
        {

            try
            {

                if (Directory.Exists(displacedFolder))
                {
                    Directory.Delete(displacedFolder, true);
                }

            }
            catch (Exception exception)
            {
                Console.WriteLine(
                    $"Restore succeeded, but the displaced original at {displacedFolder} could not be deleted "
                    + $"automatically ({exception.Message}). Delete it by hand once you've confirmed the app's data is correct.");
            }

        }

        private static void TryDeleteBackup(string backupPath)
        {

            try
            {
                Directory.Delete(backupPath, true);
            }
            catch (Exception exception)
            {
                Console.WriteLine(
                    $"Restore succeeded, but the backup at {backupPath} could not be deleted automatically "
                    + $"({exception.Message}). Delete it by hand once you've confirmed the app's data is correct.");
            }

        }

        private static void TryDeleteStagingFolder(string stagingFolder)
        {

            try
            {

                if (Directory.Exists(stagingFolder))
                {
                    Directory.Delete(stagingFolder, true);
                }

            }
            catch (Exception exception)
            {
                Console.WriteLine(
                    $"Restore was refused, and the staged copy at {stagingFolder} could not be cleaned up automatically "
                    + $"({exception.Message}). It is safe to delete by hand — the backup at its original path is untouched.");
            }

        }

        private static string BuildUniqueSiblingFolderPath(string baseFolder, string suffix)
        {
            string candidate = baseFolder + suffix + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");
            int attempt = 1;

            while (Directory.Exists(candidate))
            {
                candidate = baseFolder + suffix + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + "-" + attempt.ToString();
                attempt++;
            }

            return candidate;
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

                    // overwrite:false — a collision here means something unexpected is already at
                    // the destination (e.g. a stranded backup reused by name); merging into it
                    // silently is exactly the failure mode finding 6 called out, so this must throw.
                    File.Copy(filePath, destinationFile, false);
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

                using (SqliteConnection connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False"))
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
            Console.WriteLine("!!  RESTORE REFUSED OR FAILED. YOUR REAL DATA IS SAFE, BUT CHECK ITS LOCATION.  !!");
            Console.WriteLine($"!!  BACKUP (IF ANY IS STILL VALID) IS AT: {backupPath}");
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

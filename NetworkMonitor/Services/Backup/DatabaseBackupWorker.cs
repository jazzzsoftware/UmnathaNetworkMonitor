using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NetworkMonitor.Data;
using NetworkMonitor.Models.Devices;
using NetworkMonitor.Services.Common;
using NetworkMonitor.Services.Csv;
using NetworkMonitor.Services.Platform;

namespace NetworkMonitor.Services.Backup
{
    public class DatabaseBackupWorker(IDbContextFactory<AppDbContext> dbFactory) : BackgroundService
    {
        private static readonly TimeSpan BackupInterval = TimeSpan.FromHours(24);
        private static readonly TimeSpan RetryFloor = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan BackupTimeout = TimeSpan.FromMinutes(5);
        private const int RetentionDays = 3;
        private const string TimestampFormat = "yyyy-MM-dd_HH-mm-ss";

        protected override async Task ExecuteAsync(CancellationToken ct)
        {

            while (!ct.IsCancellationRequested)
            {
                bool backupCreated = false;

                try
                {
                    TimeSpan delay = GetDelayUntilNextBackup();

                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, ct);
                    }

                    bool created = false;

                    await Watchdog.RunAsync(async token => created = await CreateBackupAsync(token), BackupTimeout, ct);

                    backupCreated = created;
                }
                catch (OperationCanceledException)
                {
                }
                catch (TimeoutException)
                {
                    AppLog.Info($"Database backup timed out after {BackupTimeout.TotalSeconds:0} seconds and was aborted; it will retry shortly.");
                }
                catch (Exception exception)
                {
                    AppLog.Error("DatabaseBackupWorker.ExecuteAsync", exception);
                }

                if (!backupCreated && !ct.IsCancellationRequested)
                {

                    try
                    {
                        await Task.Delay(RetryFloor, ct);
                    }
                    catch (OperationCanceledException)
                    {
                    }

                }

            }

        }

        private async Task<bool> CreateBackupAsync(CancellationToken ct)
        {
            string sourcePath = AppDbContext.DbPath;
            bool created = false;

            if (File.Exists(sourcePath))
            {
                string backupDirectory = GetBackupDirectory();

                Directory.CreateDirectory(backupDirectory);

                string timestamp = DateTime.Now.ToString(TimestampFormat);
                string backupPath = Path.Combine(backupDirectory, $"networkmonitor_{timestamp}.db");

                await Task.Run(() => BackupDatabaseFile(sourcePath, backupPath), ct);

                try
                {
                    await ExportApprovedDevicesAsync(backupDirectory, timestamp, ct);
                }
                catch
                {
                    TryDelete(backupPath);

                    throw;
                }

                PruneOldBackups(backupDirectory);

                created = true;
            }

            return created;
        }

        private async Task ExportApprovedDevicesAsync(string backupDirectory, string timestamp, CancellationToken ct)
        {
            await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
            List<Device> approved = await db.Devices
                .AsNoTracking()
                .Where(device => device.IsApproved)
                .ToListAsync(ct);

            string csv = DeviceCsvExporter.ToCsv(approved);
            string csvPath = Path.Combine(backupDirectory, $"approved-devices_{timestamp}.csv");

            await File.WriteAllTextAsync(csvPath, csv, ct);
        }

        private static void BackupDatabaseFile(string sourcePath, string backupPath)
        {

            using (SqliteConnection sourceConnection = new SqliteConnection($"Data Source={sourcePath}"))
            {

                using (SqliteConnection backupConnection = new SqliteConnection($"Data Source={backupPath}"))
                {
                    sourceConnection.Open();
                    backupConnection.Open();
                    sourceConnection.BackupDatabase(backupConnection);
                }

            }

        }

        private static void PruneOldBackups(string backupDirectory)
        {

            try
            {
                DateTime cutoffUtc = DateTime.UtcNow.AddDays(-RetentionDays);

                PruneOldFiles(backupDirectory, "networkmonitor_*.db", cutoffUtc);
                PruneOldFiles(backupDirectory, "approved-devices_*.csv", cutoffUtc);
            }
            catch (Exception exception)
            {
                AppLog.Error("DatabaseBackupWorker.PruneOldBackups", exception);
            }

        }

        private static void PruneOldFiles(string backupDirectory, string searchPattern, DateTime cutoffUtc)
        {

            foreach (string file in Directory.EnumerateFiles(backupDirectory, searchPattern))
            {
                DateTime? timestampUtc = ParseBackupTimestampUtc(file);

                if (timestampUtc is not null && timestampUtc.Value < cutoffUtc)
                {
                    TryDelete(file);
                }

            }

        }

        private static TimeSpan GetDelayUntilNextBackup()
        {
            string backupDirectory = GetBackupDirectory();

            Directory.CreateDirectory(backupDirectory);

            DateTime? newestBackupUtc = GetNewestBackupTimeUtc(backupDirectory);
            TimeSpan delay;

            if (newestBackupUtc is null)
            {
                delay = TimeSpan.Zero;
            }
            else
            {
                DateTime nextDueUtc = newestBackupUtc.Value + BackupInterval;
                TimeSpan remaining = nextDueUtc - DateTime.UtcNow;
                delay = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }

            return delay;
        }

        private static DateTime? GetNewestBackupTimeUtc(string backupDirectory)
        {
            DateTime? newest = null;

            foreach (string file in Directory.EnumerateFiles(backupDirectory, "networkmonitor_*.db"))
            {
                DateTime? timestampUtc = ParseBackupTimestampUtc(file);

                if (timestampUtc is not null && (newest is null || timestampUtc.Value > newest.Value))
                {
                    newest = timestampUtc.Value;
                }

            }

            return newest;
        }

        private static DateTime? ParseBackupTimestampUtc(string filePath)
        {
            string name = Path.GetFileNameWithoutExtension(filePath);
            int separatorIndex = name.IndexOf('_');
            DateTime? result = null;

            if (separatorIndex >= 0 && separatorIndex + 1 < name.Length)
            {
                string stamp = name[(separatorIndex + 1)..];

                if (DateTime.TryParseExact(stamp, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime parsed))
                {
                    result = parsed.ToUniversalTime();
                }

            }

            return result;
        }

        private static void TryDelete(string filePath)
        {

            try
            {
                File.Delete(filePath);
            }
            catch (Exception exception)
            {
                AppLog.Error("DatabaseBackupWorker.TryDelete", exception);
            }

        }

        private static string GetBackupDirectory()
        {
            string databaseDirectory = Path.GetDirectoryName(AppDbContext.DbPath)!;
            string backupDirectory = Path.Combine(databaseDirectory, "Backups");

            return backupDirectory;
        }
    }
}

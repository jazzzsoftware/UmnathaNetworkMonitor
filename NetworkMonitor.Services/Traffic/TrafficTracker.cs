using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Hosting;
using NetworkMonitor.Services.Data;
using NetworkMonitor.Models.Traffic;
using NetworkMonitor.Services.Platform;
using System.ComponentModel;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NetworkMonitor.Core.Common;
using NetworkMonitor.Core.Traffic;

namespace NetworkMonitor.Services.Traffic
{
    public sealed class TrafficTracker(
        TrafficCollector collector,
        Settings settings,
        IDbContextFactory<AppDbContext> dbFactory) : BackgroundService
    {
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const int MaxCacheEntries = 512;

        private const string WanRollupSql = """
            INSERT INTO TrafficRollups (MinuteEpoch, ProcessName, BytesUploaded, BytesDownloaded, ProcessPath)
            VALUES ($minute, $name, $upload, $download, $path)
            ON CONFLICT(MinuteEpoch, ProcessName) DO UPDATE SET
                BytesUploaded = BytesUploaded + excluded.BytesUploaded,
                BytesDownloaded = BytesDownloaded + excluded.BytesDownloaded,
                ProcessPath = COALESCE(ProcessPath, excluded.ProcessPath)
            """;

        private const string LocalRollupSql = """
            INSERT INTO LocalTrafficRollups (MinuteEpoch, ProcessName, ProcessPath, RemoteIp, Protocol, RemotePort, BytesUploaded, BytesDownloaded)
            VALUES ($minute, $name, $path, $ip, $protocol, $port, $upload, $download)
            ON CONFLICT(MinuteEpoch, ProcessName, RemoteIp, Protocol, RemotePort) DO UPDATE SET
                BytesUploaded = BytesUploaded + excluded.BytesUploaded,
                BytesDownloaded = BytesDownloaded + excluded.BytesDownloaded,
                ProcessPath = COALESCE(ProcessPath, excluded.ProcessPath)
            """;

        private static readonly string[] WanRollupParameters = { "$minute", "$name", "$upload", "$download", "$path" };
        private static readonly string[] LocalRollupParameters = { "$minute", "$name", "$path", "$ip", "$protocol", "$port", "$upload", "$download" };
        private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan PurgeTimeout = TimeSpan.FromMinutes(2);

        // The raw tables are only ever read by the 5-minute live view; every longer range and the
        // daily digest read the rollups, so keeping per-second rows for TrafficPurgeDays would
        // grow the database, the WAL and the nightly backup for data nothing queries.
        private static readonly TimeSpan RawEntryRetention = TimeSpan.FromHours(1);
        private static readonly TimeSpan RawPurgeInterval = TimeSpan.FromMinutes(5);

        private readonly Dictionary<int, ProcessInfo> _infoCache = new();
        private DateTime _lastRawPurgeUtc = DateTime.MinValue;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        public event EventHandler<TrafficFlushedEventArgs>? Flushed;

        protected override async Task ExecuteAsync(CancellationToken ct)
        {

            while (!ct.IsCancellationRequested)
            {

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(settings.TrafficIntervalSeconds), ct);
                    await Watchdog.RunAsync(FlushAsync, FlushTimeout, ct);
                    await Watchdog.RunAsync(PurgeRawEntriesAsync, PurgeTimeout, ct);
                }
                catch (OperationCanceledException)
                {
                }
                catch (TimeoutException)
                {
                    AppLog.Info($"Traffic flush timed out after {FlushTimeout.TotalSeconds:0} seconds and was aborted; it will retry on the next cycle.");
                }
                catch (Exception exception)
                {
                    AppLog.Error("TrafficTracker.ExecuteAsync", exception);
                }

            }

        }

        private async Task FlushAsync(CancellationToken ct)
        {
            Dictionary<int, (long Upload, long Download)> snapshot = collector.DrainAndReset();
            Dictionary<LocalFlowKey, (long Upload, long Download)> localSnapshot = collector.DrainAndResetLocal();

            DateTime timestamp = DateTime.UtcNow;
            List<TrafficEntry> entries = new();

            foreach (KeyValuePair<int, (long Upload, long Download)> kvp in snapshot)
            {
                (string processName, string? processPath) = ResolveProcess(kvp.Key);

                entries.Add(new TrafficEntry
                {
                    Timestamp = timestamp,
                    ProcessName = processName,
                    ProcessPath = processPath,
                    BytesUploaded = kvp.Value.Upload,
                    BytesDownloaded = kvp.Value.Download
                });
            }

            List<LocalTrafficEntry> localEntries = new();
            List<LocalTrafficDelta> localDeltas = new();

            foreach (KeyValuePair<LocalFlowKey, (long Upload, long Download)> pair in localSnapshot)
            {
                (string processName, string? processPath) = ResolveProcess(pair.Key.Pid);
                string remoteIp = LanClassifier.Format(pair.Key.RemoteIp);
                int protocol = pair.Key.Protocol;
                int remotePort = pair.Key.RemotePort;

                localEntries.Add(new LocalTrafficEntry
                {
                    Timestamp = timestamp,
                    ProcessName = processName,
                    ProcessPath = processPath,
                    RemoteIp = remoteIp,
                    Protocol = protocol,
                    RemotePort = remotePort,
                    BytesUploaded = pair.Value.Upload,
                    BytesDownloaded = pair.Value.Download
                });

                localDeltas.Add(new LocalTrafficDelta(processName, processPath, remoteIp, protocol, remotePort, pair.Value.Upload, pair.Value.Download));
            }

            if (entries.Count > 0 || localEntries.Count > 0)
            {
                await WriteFlushAsync(timestamp, entries, localEntries, localDeltas, ct);
            }

            // Raised even for an empty flush: the view models age their live rate windows from
            // this event, so skipping idle intervals would leave stale rates on screen.
            Flushed?.Invoke(this, new TrafficFlushedEventArgs(entries, localDeltas));

            if (_infoCache.Count > MaxCacheEntries)
            {
                PruneInfoCache(snapshot, localSnapshot);
            }

        }

        private async Task WriteFlushAsync(
            DateTime timestamp,
            List<TrafficEntry> entries,
            List<LocalTrafficEntry> localEntries,
            List<LocalTrafficDelta> localDeltas,
            CancellationToken ct)
        {
            long minuteEpoch = MinuteEpochFor(timestamp);

            await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);

            DbConnection connection = db.Database.GetDbConnection();
            DbTransaction dbTransaction = transaction.GetDbTransaction();

            if (entries.Count > 0)
            {
                db.TrafficEntries.AddRange(entries);
            }

            if (localEntries.Count > 0)
            {
                db.LocalTrafficEntries.AddRange(localEntries);
            }

            await db.SaveChangesAsync(ct);

            if (entries.Count > 0)
            {
                List<object?[]> rows = new List<object?[]>(entries.Count);

                foreach (TrafficEntry entry in entries)
                {
                    rows.Add(new object?[] { minuteEpoch, entry.ProcessName, entry.BytesUploaded, entry.BytesDownloaded, entry.ProcessPath });
                }

                await ExecuteUpsertAsync(connection, dbTransaction, WanRollupSql, WanRollupParameters, rows, ct);
            }

            if (localDeltas.Count > 0)
            {
                List<object?[]> rows = new List<object?[]>(localDeltas.Count);

                foreach (LocalTrafficDelta delta in localDeltas)
                {
                    rows.Add(new object?[] { minuteEpoch, delta.ProcessName, delta.ProcessPath, delta.RemoteIp, delta.Protocol, delta.RemotePort, delta.BytesUploaded, delta.BytesDownloaded });
                }

                await ExecuteUpsertAsync(connection, dbTransaction, LocalRollupSql, LocalRollupParameters, rows, ct);
            }

            await transaction.CommitAsync(ct);
        }

        private async Task PurgeRawEntriesAsync(CancellationToken ct)
        {
            DateTime nowUtc = DateTime.UtcNow;

            if (nowUtc - _lastRawPurgeUtc >= RawPurgeInterval)
            {
                _lastRawPurgeUtc = nowUtc;

                DateTime cutoff = nowUtc - RawEntryRetention;

                await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);

                await db.TrafficEntries
                    .Where(entry => entry.Timestamp < cutoff)
                    .ExecuteDeleteAsync(ct);

                await db.LocalTrafficEntries
                    .Where(entry => entry.Timestamp < cutoff)
                    .ExecuteDeleteAsync(ct);
            }

        }

        private static async Task ExecuteUpsertAsync(
            DbConnection connection,
            DbTransaction transaction,
            string sql,
            string[] parameterNames,
            List<object?[]> rows,
            CancellationToken ct)
        {
            await using DbCommand command = connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText = sql;

            List<DbParameter> parameters = new List<DbParameter>(parameterNames.Length);

            foreach (string parameterName in parameterNames)
            {
                DbParameter parameter = command.CreateParameter();
                parameter.ParameterName = parameterName;
                command.Parameters.Add(parameter);
                parameters.Add(parameter);
            }

            foreach (object?[] row in rows)
            {

                for (int index = 0; index < parameters.Count; index++)
                {
                    parameters[index].Value = row[index] ?? DBNull.Value;
                }

                await command.ExecuteNonQueryAsync(ct);
            }

        }

        private static long MinuteEpochFor(DateTime timestamp)
        {
            long minuteEpoch = ((long)(timestamp - DateTime.UnixEpoch).TotalSeconds / 60) * 60;

            return minuteEpoch;
        }

        private void PruneInfoCache(
            Dictionary<int, (long Upload, long Download)> snapshot,
            Dictionary<LocalFlowKey, (long Upload, long Download)> localSnapshot)
        {
            HashSet<int> active = new HashSet<int>(snapshot.Keys);

            foreach (LocalFlowKey key in localSnapshot.Keys)
            {
                active.Add(key.Pid);
            }

            List<int> stale = new();

            foreach (int pid in _infoCache.Keys)
            {

                if (!active.Contains(pid))
                {
                    stale.Add(pid);
                }

            }

            foreach (int pid in stale)
            {
                _infoCache.Remove(pid);
            }

        }

        private (string Name, string? Path) ResolveProcessInfo(int pid, Process process)
        {
            DateTime startTime = default;
            bool haveStartTime = false;

            try
            {
                startTime = process.StartTime;
                haveStartTime = true;
            }
            catch (Win32Exception)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (NotSupportedException)
            {
            }

            (string Name, string? Path) resolved;

            if (_infoCache.TryGetValue(pid, out ProcessInfo cached)
                && cached.HaveStartTime == haveStartTime
                && (!haveStartTime || cached.StartTime == startTime))
            {
                resolved = (cached.Name, cached.Path);
            }
            else
            {
                string name = process.ProcessName;
                string? path = GetProcessPath(pid);
                _infoCache[pid] = new ProcessInfo(startTime, haveStartTime, name, path);

                resolved = (name, path);
            }

            return resolved;
        }

        private (string Name, string? Path) ResolveProcess(int pid)
        {
            (string Name, string? Path) resolved;

            if (pid == WellKnownPids.System)
            {
                resolved = (WellKnownPids.SystemName, null);
            }
            else
            {

                try
                {
                    using Process process = Process.GetProcessById(pid);
                    resolved = ResolveProcessInfo(pid, process);
                }
                catch (ArgumentException)
                {

                    // The process exited during the interval. Its bytes are real, so fall back to
                    // the name last cached for that pid instead of discarding the counter.
                    if (_infoCache.TryGetValue(pid, out ProcessInfo cached))
                    {
                        resolved = (cached.Name, cached.Path);
                    }
                    else
                    {
                        resolved = (WellKnownPids.SystemName, null);
                    }

                }

            }

            return resolved;
        }

        private static string? GetProcessPath(int pid)
        {
            IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
            string? result = null;

            if (handle != IntPtr.Zero)
            {

                try
                {
                    StringBuilder buffer = new StringBuilder(1024);
                    uint size = (uint)buffer.Capacity;

                    if (QueryFullProcessImageName(handle, 0, buffer, ref size))
                    {
                        result = buffer.ToString();
                    }

                }
                finally
                {
                    CloseHandle(handle);
                }

            }

            return result;
        }

        private readonly record struct ProcessInfo(DateTime StartTime, bool HaveStartTime, string Name, string? Path);
    }
}

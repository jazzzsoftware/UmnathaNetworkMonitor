using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NetworkMonitor.Data;
using NetworkMonitor.Models;
using NetworkMonitor.Services.Common;
using NetworkMonitor.Services.Platform;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace NetworkMonitor.Services.Traffic
{
    public class TrafficTracker(
        TrafficCollector collector,
        Settings settings,
        IDbContextFactory<AppDbContext> dbFactory) : BackgroundService
    {
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const int MaxCacheEntries = 512;
        private const int SystemPid = 4;
        private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(30);
        private readonly Dictionary<int, ProcessInfo> _infoCache = new();

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

                try
                {
                    using Process process = Process.GetProcessById(kvp.Key);
                    (string processName, string? processPath) = ResolveProcessInfo(kvp.Key, process);

                    entries.Add(new TrafficEntry
                    {
                        Timestamp = timestamp,
                        ProcessName = processName,
                        ProcessPath = processPath,
                        BytesUploaded = kvp.Value.Upload,
                        BytesDownloaded = kvp.Value.Download
                    });
                }
                catch (ArgumentException)
                {
                }

            }

            List<LocalTrafficEntry> localEntries = new();
            List<LocalTrafficDelta> localDeltas = new();

            foreach (KeyValuePair<LocalFlowKey, (long Upload, long Download)> pair in localSnapshot)
            {
                (string processName, string? processPath) = ResolveLocalProcess(pair.Key.Pid);
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
                await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);

                if (entries.Count > 0)
                {
                    db.TrafficEntries.AddRange(entries);
                    await db.SaveChangesAsync(ct);

                    await UpsertRollupsAsync(db, timestamp, entries, ct);
                }

                if (localEntries.Count > 0)
                {
                    db.LocalTrafficEntries.AddRange(localEntries);
                    await db.SaveChangesAsync(ct);

                    await UpsertLocalRollupsAsync(db, timestamp, localDeltas, ct);
                }

                Flushed?.Invoke(this, new TrafficFlushedEventArgs(entries, localDeltas));
            }

            if (snapshot.Count > 0 && _infoCache.Count > MaxCacheEntries)
            {
                PruneInfoCache(snapshot);
            }

        }

        private void PruneInfoCache(Dictionary<int, (long Upload, long Download)> snapshot)
        {
            List<int> stale = new();

            foreach (int pid in _infoCache.Keys)
            {

                if (!snapshot.ContainsKey(pid))
                {
                    stale.Add(pid);
                }

            }

            foreach (int pid in stale)
            {
                _infoCache.Remove(pid);
            }

        }

        private static async Task UpsertRollupsAsync(AppDbContext db, DateTime timestamp, List<TrafficEntry> entries, CancellationToken ct)
        {
            long minuteEpoch = ((long)(timestamp - DateTime.UnixEpoch).TotalSeconds / 60) * 60;

            await db.Database.OpenConnectionAsync(ct);

            DbConnection connection = db.Database.GetDbConnection();

            await using (DbTransaction transaction = await connection.BeginTransactionAsync(ct))
            await using (DbCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO TrafficRollups (MinuteEpoch, ProcessName, BytesUploaded, BytesDownloaded, ProcessPath)
                    VALUES ($minute, $name, $upload, $download, $path)
                    ON CONFLICT(MinuteEpoch, ProcessName) DO UPDATE SET
                        BytesUploaded = BytesUploaded + excluded.BytesUploaded,
                        BytesDownloaded = BytesDownloaded + excluded.BytesDownloaded,
                        ProcessPath = COALESCE(ProcessPath, excluded.ProcessPath)
                    """;

                DbParameter minuteParameter = command.CreateParameter();
                minuteParameter.ParameterName = "$minute";
                minuteParameter.Value = minuteEpoch;
                command.Parameters.Add(minuteParameter);

                DbParameter nameParameter = command.CreateParameter();
                nameParameter.ParameterName = "$name";
                command.Parameters.Add(nameParameter);

                DbParameter uploadParameter = command.CreateParameter();
                uploadParameter.ParameterName = "$upload";
                command.Parameters.Add(uploadParameter);

                DbParameter downloadParameter = command.CreateParameter();
                downloadParameter.ParameterName = "$download";
                command.Parameters.Add(downloadParameter);

                DbParameter pathParameter = command.CreateParameter();
                pathParameter.ParameterName = "$path";
                command.Parameters.Add(pathParameter);

                foreach (TrafficEntry entry in entries)
                {
                    nameParameter.Value = entry.ProcessName;
                    uploadParameter.Value = entry.BytesUploaded;
                    downloadParameter.Value = entry.BytesDownloaded;
                    pathParameter.Value = entry.ProcessPath is null ? (object)DBNull.Value : entry.ProcessPath;

                    await command.ExecuteNonQueryAsync(ct);
                }

                await transaction.CommitAsync(ct);
            }

        }

        private static async Task UpsertLocalRollupsAsync(AppDbContext db, DateTime timestamp, List<LocalTrafficDelta> localDeltas, CancellationToken ct)
        {
            long minuteEpoch = ((long)(timestamp - DateTime.UnixEpoch).TotalSeconds / 60) * 60;

            await db.Database.OpenConnectionAsync(ct);

            DbConnection connection = db.Database.GetDbConnection();

            await using (DbTransaction transaction = await connection.BeginTransactionAsync(ct))
            await using (DbCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO LocalTrafficRollups (MinuteEpoch, ProcessName, ProcessPath, RemoteIp, Protocol, RemotePort, BytesUploaded, BytesDownloaded)
                    VALUES ($minute, $name, $path, $ip, $protocol, $port, $upload, $download)
                    ON CONFLICT(MinuteEpoch, ProcessName, RemoteIp, Protocol, RemotePort) DO UPDATE SET
                        BytesUploaded = BytesUploaded + excluded.BytesUploaded,
                        BytesDownloaded = BytesDownloaded + excluded.BytesDownloaded,
                        ProcessPath = COALESCE(ProcessPath, excluded.ProcessPath)
                    """;

                DbParameter minuteParameter = command.CreateParameter();
                minuteParameter.ParameterName = "$minute";
                minuteParameter.Value = minuteEpoch;
                command.Parameters.Add(minuteParameter);

                DbParameter nameParameter = command.CreateParameter();
                nameParameter.ParameterName = "$name";
                command.Parameters.Add(nameParameter);

                DbParameter pathParameter = command.CreateParameter();
                pathParameter.ParameterName = "$path";
                command.Parameters.Add(pathParameter);

                DbParameter ipParameter = command.CreateParameter();
                ipParameter.ParameterName = "$ip";
                command.Parameters.Add(ipParameter);

                DbParameter protocolParameter = command.CreateParameter();
                protocolParameter.ParameterName = "$protocol";
                command.Parameters.Add(protocolParameter);

                DbParameter portParameter = command.CreateParameter();
                portParameter.ParameterName = "$port";
                command.Parameters.Add(portParameter);

                DbParameter uploadParameter = command.CreateParameter();
                uploadParameter.ParameterName = "$upload";
                command.Parameters.Add(uploadParameter);

                DbParameter downloadParameter = command.CreateParameter();
                downloadParameter.ParameterName = "$download";
                command.Parameters.Add(downloadParameter);

                foreach (LocalTrafficDelta delta in localDeltas)
                {
                    nameParameter.Value = delta.ProcessName;
                    pathParameter.Value = delta.ProcessPath is null ? (object)DBNull.Value : delta.ProcessPath;
                    ipParameter.Value = delta.RemoteIp;
                    protocolParameter.Value = delta.Protocol;
                    portParameter.Value = delta.RemotePort;
                    uploadParameter.Value = delta.BytesUploaded;
                    downloadParameter.Value = delta.BytesDownloaded;

                    await command.ExecuteNonQueryAsync(ct);
                }

                await transaction.CommitAsync(ct);
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
            catch (Exception)
            {
            }

            if (_infoCache.TryGetValue(pid, out ProcessInfo cached)
                && cached.HaveStartTime == haveStartTime
                && (!haveStartTime || cached.StartTime == startTime))
            {
                (string Name, string? Path) hit = (cached.Name, cached.Path);

                return hit;
            }

            string name = process.ProcessName;
            string? path = GetProcessPath(pid);
            _infoCache[pid] = new ProcessInfo(startTime, haveStartTime, name, path);

            (string Name, string? Path) resolved = (name, path);

            return resolved;
        }

        private (string Name, string? Path) ResolveLocalProcess(int pid)
        {
            (string Name, string? Path) resolved;

            if (pid == SystemPid)
            {
                resolved = ("System", null);
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
                    resolved = ("System", null);
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

using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetworkMonitor.Data;
using NetworkMonitor.Models;
using NetworkMonitor.Services.Traffic;

namespace NetworkMonitor.Services.Digest
{
    public class DigestGenerator(IDbContextFactory<AppDbContext> dbFactory)
    {
        public event EventHandler<DigestReport>? ReportGenerated;

        public async Task<DigestReport> GenerateAsync(DateTime startUtc, DateTime endUtc, bool isScheduled, CancellationToken ct = default)
        {
            await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);

            List<DeviceEvent> events = await db.DeviceEvents
                .AsNoTracking()
                .Where(deviceEvent => deviceEvent.Timestamp >= startUtc && deviceEvent.Timestamp < endUtc)
                .ToListAsync(ct);

            List<Device> devices = await db.Devices.AsNoTracking().ToListAsync(ct);
            List<AppTrafficTotal> traffic = await LoadTrafficTotalsAsync(db, startUtc, endUtc, ct);
            List<LocalTrafficMinute> localTraffic = await LoadLocalTrafficTotalsAsync(db, startUtc, endUtc, ct);

            DigestSummary summary = DigestSummaryBuilder.Build(events, devices, traffic, localTraffic, startUtc, endUtc);

            DateTime speedTestStartUtc = endUtc.AddDays(-1);
            summary.SpeedTests = await db.SpeedTestResults
                .Where(result => result.Timestamp >= speedTestStartUtc && result.Timestamp < endUtc)
                .OrderBy(result => result.Timestamp)
                .Select(result => new SpeedTestRowSummary
                {
                    Timestamp = result.Timestamp,
                    DownloadMbps = result.DownloadMbps,
                    UploadMbps = result.UploadMbps,
                    LatencyMs = result.LatencyMs,
                    JitterMs = result.JitterMs,
                    Server = result.Server
                })
                .ToListAsync(ct);

            DigestReport report = new DigestReport
            {
                PeriodStart = startUtc,
                PeriodEnd = endUtc,
                GeneratedAt = DateTime.UtcNow,
                Headline = summary.Headline,
                SummaryJson = JsonSerializer.Serialize(summary),
                IsScheduled = isScheduled
            };

            db.DigestReports.Add(report);
            await db.SaveChangesAsync(ct);

            ReportGenerated?.Invoke(this, report);

            return report;
        }

        private static async Task<List<AppTrafficTotal>> LoadTrafficTotalsAsync(
            AppDbContext db, DateTime startUtc, DateTime endUtc, CancellationToken ct)
        {
            List<AppTrafficTotal> totals = new();
            long startEpoch = (long)(startUtc - DateTime.UnixEpoch).TotalSeconds;
            long endEpoch = (long)(endUtc - DateTime.UnixEpoch).TotalSeconds;

            await db.Database.OpenConnectionAsync(ct);

            DbConnection connection = db.Database.GetDbConnection();

            await using (DbCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT ProcessName, SUM(BytesUploaded) AS Upload, SUM(BytesDownloaded) AS Download
                    FROM TrafficRollups
                    WHERE MinuteEpoch >= $start AND MinuteEpoch < $end
                    GROUP BY ProcessName
                    """;

                DbParameter startParameter = command.CreateParameter();
                startParameter.ParameterName = "$start";
                startParameter.Value = startEpoch;
                command.Parameters.Add(startParameter);

                DbParameter endParameter = command.CreateParameter();
                endParameter.ParameterName = "$end";
                endParameter.Value = endEpoch;
                command.Parameters.Add(endParameter);

                await using (DbDataReader reader = await command.ExecuteReaderAsync(ct))
                {

                    while (await reader.ReadAsync(ct))
                    {
                        totals.Add(new AppTrafficTotal
                        {
                            ProcessName = reader.GetString(0),
                            BytesUploaded = reader.GetInt64(1),
                            BytesDownloaded = reader.GetInt64(2)
                        });
                    }

                }

            }

            return totals;
        }

        private static async Task<List<LocalTrafficMinute>> LoadLocalTrafficTotalsAsync(
            AppDbContext db, DateTime startUtc, DateTime endUtc, CancellationToken ct)
        {
            List<LocalTrafficMinute> totals = new();
            long startEpoch = (long)(startUtc - DateTime.UnixEpoch).TotalSeconds;
            long endEpoch = (long)(endUtc - DateTime.UnixEpoch).TotalSeconds;

            await db.Database.OpenConnectionAsync(ct);

            DbConnection connection = db.Database.GetDbConnection();

            await using (DbCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT ProcessName, RemoteIp, SUM(BytesUploaded) AS Upload, SUM(BytesDownloaded) AS Download
                    FROM LocalTrafficRollups
                    WHERE MinuteEpoch >= $start AND MinuteEpoch < $end
                    GROUP BY ProcessName, RemoteIp
                    """;

                DbParameter startParameter = command.CreateParameter();
                startParameter.ParameterName = "$start";
                startParameter.Value = startEpoch;
                command.Parameters.Add(startParameter);

                DbParameter endParameter = command.CreateParameter();
                endParameter.ParameterName = "$end";
                endParameter.Value = endEpoch;
                command.Parameters.Add(endParameter);

                await using (DbDataReader reader = await command.ExecuteReaderAsync(ct))
                {

                    while (await reader.ReadAsync(ct))
                    {
                        string processName = reader.GetString(0);
                        string remoteIp = reader.GetString(1);
                        long upload = reader.GetInt64(2);
                        long download = reader.GetInt64(3);
                        totals.Add(new LocalTrafficMinute(0, processName, remoteIp, upload, download));
                    }

                }

            }

            return totals;
        }
    }
}

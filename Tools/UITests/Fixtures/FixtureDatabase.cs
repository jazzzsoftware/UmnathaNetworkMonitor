using System.Linq;
using Microsoft.Data.Sqlite;

namespace NetworkMonitor.UITests.Fixtures
{
    // Reads counts straight out of the fixture database while the app under test has it open.
    //
    // PurgePhase needs this because a purge's whole point is what is no longer there, and the UI
    // shows a summary line rather than the rows themselves. Read-only and shared-cache-free: the
    // app owns this file, and nothing here should ever write to it or hold a lock on it.
    public static class FixtureDatabase
    {
        private const string DatabaseFileName = "networkmonitor.db";

        public static long CountRows(string dataFolder, string tableName)
        {
            long count = ExecuteScalar(dataFolder, $"SELECT COUNT(*) FROM {tableName}");

            return count;
        }

        // Counts rows whose Timestamp column is older than the given moment. Timestamps are stored
        // as text by EF's SQLite provider, and comparing them as text is only correct because that
        // format sorts chronologically — the same assumption the app's own queries make.
        public static long CountRowsOlderThan(string dataFolder, string tableName, DateTime cutoffUtc)
        {
            string cutoffText = cutoffUtc.ToString("yyyy-MM-dd HH:mm:ss");
            long count = ExecuteScalar(dataFolder, $"SELECT COUNT(*) FROM {tableName} WHERE Timestamp < '{cutoffText}'");

            return count;
        }

        // The largest per-timestamp download total among seeded rows still inside the window.
        //
        // This is a safe floor for the chart's peak bucket: rows sharing a timestamp necessarily
        // land in the same bucket, so the drawn peak can only be at or above this. Restricted to the
        // seeded process names on purpose - the app under test is capturing real traffic into this
        // same table while the suite drives it, and counting that would race the draw and could
        // demand a peak the chart never saw.
        //
        // Returns 0 when the seeded rows have aged out of the window entirely, which is the caller's
        // cue to skip rather than invent an assertion. That is what the 5-minute range does: its raw
        // rows only cover the few minutes before the fixture was seeded, so whether any remain
        // depends on how long the run has been going.
        public static long MaxSeededBucketDownload(string dataFolder, string tableName, DateTime cutoffUtc, IReadOnlyList<string> processNames)
        {
            string cutoffText = cutoffUtc.ToString("yyyy-MM-dd HH:mm:ss");
            string quotedNames = string.Join(", ", processNames.Select(processName => $"'{processName.Replace("'", "''")}'"));
            string sql =
                $"SELECT MAX(bucketTotal) FROM (SELECT SUM(BytesDownloaded) AS bucketTotal FROM {tableName} "
                + $"WHERE Timestamp >= '{cutoffText}' AND ProcessName IN ({quotedNames}) GROUP BY Timestamp)";

            long maximum = ExecuteScalar(dataFolder, sql);

            if (maximum < 0L)
            {
                maximum = 0L;
            }

            return maximum;
        }

        // The whole-millisecond latency of the newest speed test result, which is what the mini
        // graph's speed test line shows. Read live rather than hardcoded from the seed: phase 04
        // drives a real speed test, so by the time the widget is checked the newest result is that
        // one and not the fixture's. Asserting against whatever is newest is also the widget's
        // actual contract - show the latest result - rather than a restatement of the seed.
        public static long NewestSpeedTestLatencyMs(string dataFolder)
        {
            long latencyMs = ExecuteScalar(dataFolder, "SELECT CAST(ROUND(LatencyMs) AS INTEGER) FROM SpeedTestResults ORDER BY Timestamp DESC LIMIT 1");

            return latencyMs;
        }

        private static long ExecuteScalar(string dataFolder, string sql)
        {
            string databasePath = Path.Combine(dataFolder, DatabaseFileName);
            SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            };

            long value = -1;

            using (SqliteConnection connection = new SqliteConnection(builder.ToString()))
            {
                connection.Open();

                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = sql;

                    object? scalar = command.ExecuteScalar();

                    if (scalar is long count)
                    {
                        value = count;
                    }

                }

            }

            return value;
        }
    }
}

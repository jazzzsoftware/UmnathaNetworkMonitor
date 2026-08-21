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

using Microsoft.Data.Sqlite;
using NetworkMonitor.Services.Platform;

namespace NetworkMonitor.Services.Data
{
    // Runs on its own connection rather than through the injected context factory. The checkpoint is
    // the last thing to happen at shutdown, by which time the host — and with it the service provider
    // EF resolves its internal services from — has already been disposed. Going through the factory
    // there threw ObjectDisposedException and the write-ahead log was never truncated.
    public static class DatabaseCheckpoint
    {
        public static void Truncate()
        {

            try
            {
                using SqliteConnection connection = new SqliteConnection($"Data Source={AppDbContext.DbPath}");
                connection.Open();

                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";

                command.ExecuteNonQuery();
            }
            catch (Exception exception)
            {
                AppLog.Error("DatabaseCheckpoint.Truncate", exception);
            }

        }
    }
}

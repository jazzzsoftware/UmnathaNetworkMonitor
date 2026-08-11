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
                // ReadWrite, not the "Data Source=" default of ReadWriteCreate: if DbPath is ever
                // wrong we want a failure in the log, not a silently created empty database.
                SqliteConnectionStringBuilder connectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = AppDbContext.DbPath,
                    Mode = SqliteOpenMode.ReadWrite
                };

                using (SqliteConnection connection = new SqliteConnection(connectionString.ToString()))
                {
                    connection.Open();

                    using SqliteCommand command = connection.CreateCommand();
                    command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";

                    // wal_checkpoint does NOT throw when another connection still holds the file — it
                    // returns a row (busy, log, checkpointed) with busy = 1 and leaves the WAL intact.
                    // ExecuteNonQuery discarded that row, so a checkpoint that silently did nothing
                    // was indistinguishable from one that worked.
                    using SqliteDataReader reader = command.ExecuteReader();

                    if (reader.Read())
                    {
                        long busy = reader.GetInt64(0);
                        long walPages = reader.GetInt64(1);
                        long checkpointed = reader.GetInt64(2);

                        if (busy != 0)
                        {
                            AppLog.Info($"WAL checkpoint was blocked by another connection (busy={busy}, log={walPages}, checkpointed={checkpointed}); the write-ahead log was not truncated.");
                        }

                    }
                    else
                    {
                        AppLog.Info("WAL checkpoint returned no result row; the write-ahead log may not have been truncated.");
                    }

                }

                // The pooled handle outlives the using block, and Environment.Exit follows straight
                // after. RetentionProbe clears the pool for the same reason.
                SqliteConnection.ClearAllPools();
            }
            catch (Exception exception)
            {
                AppLog.Error("DatabaseCheckpoint.Truncate", exception);
            }

        }
    }
}

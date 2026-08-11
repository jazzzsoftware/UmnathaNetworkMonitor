using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using NetworkMonitor.Services.Platform;

namespace NetworkMonitor.Services.Data
{
    public static class DatabaseInitializer
    {
        private const string HistoryTable = "__EFMigrationsHistory";
        private const string LegacyProbeTable = "Devices";
        private const string BaselineProductVersion = "10.0.10";

        public static async Task InitializeAsync(AppDbContext db)
        {
            bool baselined = await TryBaselineLegacyDatabaseAsync(db);

            if (baselined)
            {
                AppLog.Info($"Database baselined: an existing pre-migration database was marked as already at {FirstMigrationId(db)}.");
            }

            await db.Database.MigrateAsync();
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }

        private static async Task<bool> TryBaselineLegacyDatabaseAsync(AppDbContext db)
        {
            bool baselined = false;
            bool hasHistory = await TableExistsAsync(db, HistoryTable);
            bool hasLegacyTables = await TableExistsAsync(db, LegacyProbeTable);

            if (!hasHistory && hasLegacyTables)
            {
                string migrationId = FirstMigrationId(db);

                if (migrationId.Length > 0)
                {
                    await db.Database.ExecuteSqlRawAsync(
                        $"CREATE TABLE IF NOT EXISTS \"{HistoryTable}\" (\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, \"ProductVersion\" TEXT NOT NULL);");

                    await db.Database.ExecuteSqlRawAsync(
                        $"INSERT INTO \"{HistoryTable}\" (\"MigrationId\", \"ProductVersion\") VALUES ({{0}}, {{1}});",
                        migrationId,
                        BaselineProductVersion);

                    baselined = true;
                }

            }

            return baselined;
        }

        private static string FirstMigrationId(AppDbContext db)
        {
            List<string> migrations = db.Database.GetMigrations().ToList();
            string migrationId = string.Empty;

            if (migrations.Count > 0)
            {
                migrationId = migrations[0];
            }

            return migrationId;
        }

        private static async Task<bool> TableExistsAsync(AppDbContext db, string tableName)
        {
            bool exists = false;
            DbConnection connection = db.Database.GetDbConnection();
            bool openedHere = false;

            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync();
                openedHere = true;
            }

            try
            {

                await using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";

                    DbParameter parameter = command.CreateParameter();
                    parameter.ParameterName = "$name";
                    parameter.Value = tableName;
                    command.Parameters.Add(parameter);

                    object? result = await command.ExecuteScalarAsync();

                    exists = result is not null && Convert.ToInt64(result) > 0;
                }

            }
            finally
            {

                if (openedHere)
                {
                    await connection.CloseAsync();
                }

            }

            return exists;
        }
    }
}

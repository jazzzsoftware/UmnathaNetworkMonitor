using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;

// Diagnostic for the traffic retention design: proves that the raw-entry purge completes well
// inside its watchdog, that the database file plateaus rather than shrinks, and that the rollups
// survive the purge and still carry history beyond the raw window.
//
// THIS TOOL DELETES ROWS. Point it at a COPY of networkmonitor.db, never the live file — the guard
// below refuses the app's own data folder, but copying first is the habit that matters.
//
//   dotnet run -- <path-to-copy.db> [retentionMinutes]
//
// retentionMinutes defaults to 60, matching TrafficTracker.RawEntryRetention. Pass something small
// (2 is a good choice) to force every raw row past the cutoff and exercise a full-size purge.

if (args.Length < 1)
{
    Console.WriteLine("usage: dotnet run -- <path-to-database-copy.db> [retentionMinutes]");
    Console.WriteLine();
    Console.WriteLine("Copy the database first:");
    Console.WriteLine(@"  copy %LOCALAPPDATA%\UmnathaNetworkMonitor\networkmonitor.db* <somewhere>\");

    return 1;
}

string dbPath = Path.GetFullPath(args[0]);
int retentionMinutes = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 60;

string liveFolder = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "UmnathaNetworkMonitor");

if (dbPath.StartsWith(liveFolder, StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine($"Refusing to run: {dbPath} is inside the live data folder.");
    Console.WriteLine("This tool deletes rows. Copy the database somewhere else and point at the copy.");

    return 1;
}

if (!File.Exists(dbPath))
{
    Console.WriteLine($"No such file: {dbPath}");

    return 1;
}

Console.WriteLine($"Database : {dbPath}");
Console.WriteLine($"Retention: {retentionMinutes} minutes (TrafficTracker ships 60)");
Console.WriteLine();

SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder
{
    DataSource = dbPath
};

using SqliteConnection connection = new SqliteConnection(builder.ToString());
connection.Open();

ReportFiles("BEFORE", dbPath);
ReportPages("BEFORE", connection);
Console.WriteLine();

Console.WriteLine("=== Census before purge ===");
CensusRaw(connection, "TrafficEntries");
CensusRaw(connection, "LocalTrafficEntries");
CensusRollup(connection, "TrafficRollups");
CensusRollup(connection, "LocalTrafficRollups");
Console.WriteLine();

Console.WriteLine("=== Raw vs rollup minute coverage (same span, before purge) ===");
CompareMinutes(connection, "TrafficEntries", "TrafficRollups");
CompareMinutes(connection, "LocalTrafficEntries", "LocalTrafficRollups");
Console.WriteLine();

// The app's cutoff: DateTime.UtcNow - RawEntryRetention, compared against a DateTime column that
// EF Core's SQLite provider stores as TEXT in this exact format.
DateTime cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(retentionMinutes);
string cutoffText = cutoff.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
Console.WriteLine($"=== Purge (cutoff {cutoffText} UTC) ===");

long wanDeleted = TimedDelete(connection, "TrafficEntries", cutoffText, out TimeSpan wanElapsed);
Console.WriteLine($"TrafficEntries      deleted {wanDeleted,10:N0} rows in {wanElapsed.TotalSeconds,7:F2}s");

long lanDeleted = TimedDelete(connection, "LocalTrafficEntries", cutoffText, out TimeSpan lanElapsed);
Console.WriteLine($"LocalTrafficEntries deleted {lanDeleted,10:N0} rows in {lanElapsed.TotalSeconds,7:F2}s");

TimeSpan totalElapsed = wanElapsed + lanElapsed;
Console.WriteLine($"Total purge time {totalElapsed.TotalSeconds:F2}s against the 120s PurgeTimeout watchdog "
    + $"({totalElapsed.TotalSeconds / 120.0 * 100.0:F1}% of budget)");
Console.WriteLine();

Console.WriteLine("=== Census after purge ===");
CensusRaw(connection, "TrafficEntries");
CensusRaw(connection, "LocalTrafficEntries");
CensusRollup(connection, "TrafficRollups");
CensusRollup(connection, "LocalTrafficRollups");
Console.WriteLine();

using (SqliteCommand checkpoint = connection.CreateCommand())
{
    checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
    checkpoint.ExecuteNonQuery();
}

ReportPages("AFTER ", connection);
connection.Close();
SqliteConnection.ClearAllPools();
ReportFiles("AFTER ", dbPath);
Console.WriteLine("(The file is expected to hold steady, not shrink — there is no VACUUM or");
Console.WriteLine(" auto_vacuum, so freed pages go on the freelist and are reused.)");
Console.WriteLine();

// Everything older than the raw cutoff must be served by the rollups, because the raw rows for
// that period no longer exist.
using SqliteConnection reopened = new SqliteConnection(builder.ToString());
reopened.Open();

Console.WriteLine("=== History beyond the raw window ===");

foreach (int hours in new[] { 1, 6, 24 })
{
    ReportRollupWindow(reopened, "TrafficRollups", hours, cutoff);
    ReportRollupWindow(reopened, "LocalTrafficRollups", hours, cutoff);
}

Console.WriteLine();
Console.WriteLine("=== Raw leftovers older than cutoff (must be 0) ===");
Console.WriteLine($"TrafficEntries      : {ScalarLong(reopened, $"SELECT COUNT(*) FROM TrafficEntries WHERE Timestamp < '{cutoffText}'")}");
Console.WriteLine($"LocalTrafficEntries : {ScalarLong(reopened, $"SELECT COUNT(*) FROM LocalTrafficEntries WHERE Timestamp < '{cutoffText}'")}");

return 0;

static void CompareMinutes(SqliteConnection connection, string rawTable, string rollupTable)
{
    // Fold each raw Timestamp onto the same minute boundary MinuteEpochFor uses, then compare the
    // set of minutes carrying raw rows against the set carrying rollup rows. MinuteEpoch is unix
    // SECONDS truncated to the minute, not minutes — see TrafficTracker.MinuteEpochFor.
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = $"""
        WITH rawMinutes AS (
            SELECT DISTINCT (CAST(strftime('%s', Timestamp) AS INTEGER) / 60) * 60 AS Minute
            FROM {rawTable}
        )
        SELECT (SELECT COUNT(*) FROM rawMinutes),
               (SELECT COUNT(*) FROM rawMinutes WHERE Minute NOT IN (SELECT MinuteEpoch FROM {rollupTable})),
               (SELECT MIN(Minute) FROM rawMinutes),
               (SELECT MAX(Minute) FROM rawMinutes)
        """;

    using SqliteDataReader reader = command.ExecuteReader();
    reader.Read();

    if (reader.IsDBNull(2))
    {
        Console.WriteLine($"{rawTable,-20} no raw rows to compare");

        return;
    }

    long rawMinutes = reader.GetInt64(0);
    long missing = reader.GetInt64(1);
    string from = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(2)).UtcDateTime.ToString("HH:mm");
    string to = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(3)).UtcDateTime.ToString("HH:mm");

    Console.WriteLine($"{rawTable,-20} {rawMinutes,4} minutes carry raw rows ({from}-{to}), "
        + $"{missing,4} of them have NO matching rollup minute");
}

static long TimedDelete(SqliteConnection connection, string table, string cutoffText, out TimeSpan elapsed)
{
    Stopwatch stopwatch = Stopwatch.StartNew();

    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = $"DELETE FROM {table} WHERE Timestamp < $cutoff";
    command.CommandTimeout = 600;
    command.Parameters.AddWithValue("$cutoff", cutoffText);
    long deleted = command.ExecuteNonQuery();

    stopwatch.Stop();
    elapsed = stopwatch.Elapsed;

    return deleted;
}

static void CensusRaw(SqliteConnection connection, string table)
{
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = $"SELECT COUNT(*), MIN(Timestamp), MAX(Timestamp) FROM {table}";

    using SqliteDataReader reader = command.ExecuteReader();
    reader.Read();

    long count = reader.GetInt64(0);
    string oldest = reader.IsDBNull(1) ? "-" : reader.GetString(1);
    string newest = reader.IsDBNull(2) ? "-" : reader.GetString(2);

    Console.WriteLine($"{table,-20} {count,10:N0} rows   oldest {oldest,-30} newest {newest}");
}

static void CensusRollup(SqliteConnection connection, string table)
{
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = $"SELECT COUNT(*), MIN(MinuteEpoch), MAX(MinuteEpoch) FROM {table}";

    using SqliteDataReader reader = command.ExecuteReader();
    reader.Read();

    long count = reader.GetInt64(0);
    string oldest = reader.IsDBNull(1) ? "-" : DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(1)).UtcDateTime.ToString("yyyy-MM-dd HH:mm");
    string newest = reader.IsDBNull(2) ? "-" : DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(2)).UtcDateTime.ToString("yyyy-MM-dd HH:mm");

    Console.WriteLine($"{table,-20} {count,10:N0} rows   oldest {oldest,-30} newest {newest}");
}

static void ReportRollupWindow(SqliteConnection connection, string table, int hours, DateTime cutoff)
{
    long fromEpoch = (long)(DateTime.UtcNow.AddHours(-hours) - DateTime.UnixEpoch).TotalSeconds;
    long cutoffEpoch = (long)(cutoff - DateTime.UnixEpoch).TotalSeconds;

    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = $"""
        SELECT COUNT(DISTINCT MinuteEpoch),
               COALESCE(SUM(BytesUploaded + BytesDownloaded), 0),
               COUNT(DISTINCT CASE WHEN MinuteEpoch < $cutoff THEN MinuteEpoch END)
        FROM {table}
        WHERE MinuteEpoch >= $from
        """;
    command.Parameters.AddWithValue("$from", fromEpoch);
    command.Parameters.AddWithValue("$cutoff", cutoffEpoch);

    using SqliteDataReader reader = command.ExecuteReader();
    reader.Read();

    long minutes = reader.GetInt64(0);
    long bytes = reader.GetInt64(1);
    long beyondRaw = reader.GetInt64(2);

    Console.WriteLine($"{table,-20} last {hours,2}h: {minutes,5} minutes, {bytes / 1024.0 / 1024.0,10:N1} MB, "
        + $"{beyondRaw,5} minutes older than the raw cutoff");
}

static long ScalarLong(SqliteConnection connection, string sql)
{
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = sql;
    long value = Convert.ToInt64(command.ExecuteScalar());

    return value;
}

static void ReportFiles(string label, string dbPath)
{
    long db = File.Exists(dbPath) ? new FileInfo(dbPath).Length : 0;
    long wal = File.Exists(dbPath + "-wal") ? new FileInfo(dbPath + "-wal").Length : 0;

    Console.WriteLine($"{label} files: db {db / 1024.0 / 1024.0,8:N1} MB   wal {wal / 1024.0 / 1024.0,8:N1} MB");
}

static void ReportPages(string label, SqliteConnection connection)
{
    long pageSize = ScalarLong(connection, "PRAGMA page_size");
    long pageCount = ScalarLong(connection, "PRAGMA page_count");
    long freelist = ScalarLong(connection, "PRAGMA freelist_count");

    Console.WriteLine($"{label} pages: {pageCount,9:N0} total, {freelist,9:N0} free "
        + $"({freelist * pageSize / 1024.0 / 1024.0:N1} MB reusable, page size {pageSize})");
}

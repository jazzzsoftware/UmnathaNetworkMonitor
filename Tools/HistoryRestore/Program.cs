using System.Globalization;
using Microsoft.Data.Sqlite;

// Puts device history back into a live database from one of the app's own backups, without
// discarding anything the live database has gained since that backup was taken.
//
// The case this exists for: a user lowers "Purge history older than (days)", the next scan runs
// ScanWorker.PurgeOldHistoryAsync, and events older than the new cutoff are deleted for good.
// Restoring the backup file wholesale would undo that, but it would also throw away every scan,
// traffic sample and digest since the backup. This merges instead — it inserts only the rows the
// live database is missing and never updates or deletes an existing row.
//
// Dry run by default. Nothing is written without --apply.

const string DataFolderName = "UmnathaNetworkMonitor";
const string LiveFileName = "networkmonitor.db";

// The two tables ScanWorker's history purge deletes from. Traffic and digest retention are
// separate settings with their own purge paths and are deliberately not touched here.
string[] tables = ["DeviceEvents", "ScanSessions"];

string? backupPath = null;
string? livePath = null;
bool apply = false;

for (int index = 0; index < args.Length; index++)
{
    string argument = args[index];

    if (argument == "--apply")
    {
        apply = true;
    }
    else if (argument == "--live" && index + 1 < args.Length)
    {
        index++;
        livePath = args[index];
    }
    else if (backupPath is null)
    {
        backupPath = argument;
    }

}

if (backupPath is null)
{
    Console.WriteLine("Usage: dotnet run --project Tools/HistoryRestore -- <backup.db> [--live <path>] [--apply]");
    Console.WriteLine();
    Console.WriteLine("  <backup.db>   A backup from %LOCALAPPDATA%\\UmnathaNetworkMonitor\\Backups.");
    Console.WriteLine("  --live        Target database. Defaults to the installed app's database.");
    Console.WriteLine("  --apply       Write the rows. Without it this is a dry run and changes nothing.");

    return 1;
}

livePath ??= Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    DataFolderName,
    LiveFileName);

if (!File.Exists(backupPath))
{
    Console.WriteLine($"Backup not found: {backupPath}");

    return 1;
}

if (!File.Exists(livePath))
{
    Console.WriteLine($"Live database not found: {livePath}");

    return 1;
}

Console.WriteLine($"Live   : {livePath}");
Console.WriteLine($"Backup : {backupPath}");
Console.WriteLine($"Mode   : {(apply ? "APPLY - rows will be written" : "dry run - nothing will be written")}");
Console.WriteLine();

// A non-empty write-ahead log usually means the app is still running, and inserting underneath it
// risks writing against a database another process is mid-transaction on.
string walPath = livePath + "-wal";

if (File.Exists(walPath) && new FileInfo(walPath).Length > 0)
{
    Console.WriteLine("WARNING: the live database has a non-empty -wal file, which usually means the");
    Console.WriteLine("         app is still running. Close it before applying.");
    Console.WriteLine();

    if (apply)
    {
        Console.WriteLine("Refusing to write while the app may be running.");

        return 1;
    }

}

long missingEvents;
long collisions;
long orphans;

using (SqliteConnection survey = Connect(livePath, backupPath, false))
{
    Report(survey, "Live", "main");
    Report(survey, "Backup", "backup");
    Console.WriteLine();

    missingEvents = Scalar(survey, MissingCountSql("DeviceEvents"));
    long missingSessions = Scalar(survey, MissingCountSql("ScanSessions"));
    Console.WriteLine($"  DeviceEvents to restore : {missingEvents,8:N0}");
    Console.WriteLine($"  ScanSessions to restore : {missingSessions,8:N0}");
    Console.WriteLine();

    collisions = Scalar(survey,
        @"SELECT COUNT(*) FROM backup.DeviceEvents b
          JOIN main.DeviceEvents m ON m.Id = b.Id
          WHERE m.DeviceId <> b.DeviceId OR m.EventType <> b.EventType OR m.Timestamp <> b.Timestamp");

    orphans = Scalar(survey,
        @"SELECT COUNT(*) FROM backup.DeviceEvents b
          WHERE NOT EXISTS (SELECT 1 FROM main.DeviceEvents m WHERE m.Id = b.Id)
            AND NOT EXISTS (SELECT 1 FROM main.Devices d WHERE d.Id = b.DeviceId)");

    Console.WriteLine("Safety checks (both must be 0):");
    Console.WriteLine($"  same Id but different content : {collisions}");
    Console.WriteLine($"  event with no matching device : {orphans}");
    Console.WriteLine();
}

if (collisions > 0 || orphans > 0)
{
    Console.WriteLine("REFUSING: the backup and the live database disagree about rows that share an Id,");
    Console.WriteLine("or the backup references devices this database no longer has. A merge would");
    Console.WriteLine("corrupt the history rather than restore it.");

    return 1;
}

if (!apply)
{
    Console.WriteLine("Dry run complete. Re-run with --apply to write these rows.");

    return 0;
}

string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
string safetyCopy = Path.Combine(
    Path.GetDirectoryName(livePath)!,
    $"{Path.GetFileNameWithoutExtension(livePath)}_pre-restore_{stamp}.db");

File.Copy(livePath, safetyCopy);
Console.WriteLine($"Safety copy: {safetyCopy}");
Console.WriteLine();

using (SqliteConnection writer = Connect(livePath, backupPath, true))
{
    using SqliteTransaction transaction = writer.BeginTransaction();

    foreach (string table in tables)
    {
        using SqliteCommand insert = writer.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = InsertSql(writer, table);
        Console.WriteLine($"  {table} inserted {insert.ExecuteNonQuery():N0}");
    }

    transaction.Commit();
    Console.WriteLine();

    Report(writer, "Live after restore", "main");

    long stillOrphaned = Scalar(writer,
        "SELECT COUNT(*) FROM main.DeviceEvents e WHERE NOT EXISTS (SELECT 1 FROM main.Devices d WHERE d.Id = e.DeviceId)");

    Console.WriteLine($"  events with a missing device : {stillOrphaned}");
}

Console.WriteLine();
Console.WriteLine("Done. If anything looks wrong, copy the safety copy back over the live database.");

return 0;

static SqliteConnection Connect(string livePath, string backupPath, bool writable)
{
    SqliteConnectionStringBuilder builder = new()
    {
        DataSource = livePath,
        Mode = writable ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadOnly
    };

    SqliteConnection connection = new(builder.ToString());
    connection.Open();

    using SqliteCommand attach = connection.CreateCommand();
    attach.CommandText = $"ATTACH DATABASE '{backupPath.Replace("'", "''")}' AS backup";
    attach.ExecuteNonQuery();

    return connection;
}

static string MissingCountSql(string table)
{
    string sql = $@"SELECT COUNT(*) FROM backup.{table} b
                    WHERE NOT EXISTS (SELECT 1 FROM main.{table} m WHERE m.Id = b.Id)";

    return sql;
}

// The column list is read from the live schema rather than hardcoded, so a table that gains a
// column in a later migration still restores correctly instead of silently dropping it.
static string InsertSql(SqliteConnection connection, string table)
{
    List<string> columns = [];

    using (SqliteCommand info = connection.CreateCommand())
    {
        info.CommandText = $"PRAGMA main.table_info({table})";

        using SqliteDataReader reader = info.ExecuteReader();

        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

    }

    string columnList = string.Join(", ", columns.Select(column => $"\"{column}\""));
    string selectList = string.Join(", ", columns.Select(column => $"b.\"{column}\""));

    string sql = $@"INSERT INTO main.{table} ({columnList})
                    SELECT {selectList} FROM backup.{table} b
                    WHERE NOT EXISTS (SELECT 1 FROM main.{table} m WHERE m.Id = b.Id)";

    return sql;
}

static long Scalar(SqliteConnection connection, string sql)
{
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = sql;
    object? value = command.ExecuteScalar();
    long result = value is null || value is DBNull ? 0L : Convert.ToInt64(value);

    return result;
}

static void Report(SqliteConnection connection, string label, string schema)
{
    long events = Scalar(connection, $"SELECT COUNT(*) FROM {schema}.DeviceEvents");
    long sessions = Scalar(connection, $"SELECT COUNT(*) FROM {schema}.ScanSessions");

    using SqliteCommand oldest = connection.CreateCommand();
    oldest.CommandText = $"SELECT MIN(Timestamp) FROM {schema}.DeviceEvents";
    object? value = oldest.ExecuteScalar();
    string oldestText = value is null || value is DBNull ? "(none)" : value.ToString()!;

    Console.WriteLine($"{label,-20} events {events,8:N0}   sessions {sessions,8:N0}   oldest {oldestText}");
}

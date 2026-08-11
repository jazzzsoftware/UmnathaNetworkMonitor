using Microsoft.EntityFrameworkCore;
using NetworkMonitor.Models.Devices;
using NetworkMonitor.Services.Data;

string workFolder = Path.Combine(Path.GetTempPath(), "nm-migration-verify");

if (Directory.Exists(workFolder))
{
    Directory.Delete(workFolder, true);
}

Directory.CreateDirectory(workFolder);

int failures = 0;

void Check(string label, bool condition)
{
    Console.WriteLine((condition ? "  PASS  " : "  FAIL  ") + label);

    if (!condition)
    {
        failures++;
    }
}

AppDbContext Open(string path)
{
    DbContextOptionsBuilder<AppDbContext> builder = new DbContextOptionsBuilder<AppDbContext>();
    builder.UseSqlite($"Data Source={path}");
    return new AppDbContext(builder.Options);
}

async Task<bool> HasTable(string path, string table)
{
    await using AppDbContext db = Open(path);
    await db.Database.OpenConnectionAsync();
    await using System.Data.Common.DbCommand command = db.Database.GetDbConnection().CreateCommand();
    command.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';";
    object? result = await command.ExecuteScalarAsync();
    return result is not null && Convert.ToInt64(result) > 0;
}

Console.WriteLine();
Console.WriteLine("SCENARIO 1 — existing v0.0.8-era database created by EnsureCreated (no __EFMigrationsHistory)");

string legacyPath = Path.Combine(workFolder, "legacy.db");

await using (AppDbContext seed = Open(legacyPath))
{
    await seed.Database.EnsureCreatedAsync();
    seed.Devices.Add(new Device { MacAddress = "AA:BB:CC:DD:EE:FF", IpAddress = "192.168.1.50", Hostname = "field-device" });
    await seed.SaveChangesAsync();
}

Check("pre-state: history table absent", !await HasTable(legacyPath, "__EFMigrationsHistory"));
Check("pre-state: Devices table present", await HasTable(legacyPath, "Devices"));

await using (AppDbContext db = Open(legacyPath))
{
    await DatabaseInitializer.InitializeAsync(db);
}

await using (AppDbContext verify = Open(legacyPath))
{
    List<string> applied = (await verify.Database.GetAppliedMigrationsAsync()).ToList();
    List<string> pending = (await verify.Database.GetPendingMigrationsAsync()).ToList();
    int deviceCount = await verify.Devices.CountAsync();
    Device? survivor = await verify.Devices.FirstOrDefaultAsync();

    Check("history table created", await HasTable(legacyPath, "__EFMigrationsHistory"));
    Check($"InitialCreate recorded as applied (applied=[{string.Join(",", applied)}])", applied.Count == 1 && applied[0].EndsWith("_InitialCreate"));
    Check($"no pending migrations (pending=[{string.Join(",", pending)}])", pending.Count == 0);
    Check($"existing row survived (count={deviceCount})", deviceCount == 1);
    Check($"row content intact (host={survivor?.Hostname})", survivor is not null && survivor.Hostname == "field-device");
}

Console.WriteLine();
Console.WriteLine("SCENARIO 2 — fresh install, no database file at all");

string freshPath = Path.Combine(workFolder, "fresh.db");

await using (AppDbContext db = Open(freshPath))
{
    await DatabaseInitializer.InitializeAsync(db);
}

await using (AppDbContext verify = Open(freshPath))
{
    List<string> applied = (await verify.Database.GetAppliedMigrationsAsync()).ToList();
    List<string> pending = (await verify.Database.GetPendingMigrationsAsync()).ToList();

    Check("Devices table created", await HasTable(freshPath, "Devices"));
    Check("SpeedTestResults table created", await HasTable(freshPath, "SpeedTestResults"));
    Check("LocalTrafficRollups table created", await HasTable(freshPath, "LocalTrafficRollups"));
    Check($"InitialCreate recorded as applied (applied=[{string.Join(",", applied)}])", applied.Count == 1);
    Check($"no pending migrations (pending=[{string.Join(",", pending)}])", pending.Count == 0);
    Check("insert works against the migrated schema", await CanInsert(freshPath));
}

Console.WriteLine();
Console.WriteLine("SCENARIO 3 — already-baselined database, second launch (must be idempotent)");

await using (AppDbContext db = Open(legacyPath))
{
    await DatabaseInitializer.InitializeAsync(db);
}

await using (AppDbContext verify = Open(legacyPath))
{
    List<string> applied = (await verify.Database.GetAppliedMigrationsAsync()).ToList();
    int deviceCount = await verify.Devices.CountAsync();

    Check($"still exactly one applied migration (applied=[{string.Join(",", applied)}])", applied.Count == 1);
    Check($"data still intact (count={deviceCount})", deviceCount == 1);
}

Console.WriteLine();
Console.WriteLine("SCENARIO 4 — does InitialCreate produce the same schema EnsureCreated did?");

string ensurePath = Path.Combine(workFolder, "schema-ensure.db");
string migratePath = Path.Combine(workFolder, "schema-migrate.db");

await using (AppDbContext db = Open(ensurePath))
{
    await db.Database.EnsureCreatedAsync();
}

await using (AppDbContext db = Open(migratePath))
{
    await db.Database.MigrateAsync();
}

Dictionary<string, string> ensureSchema = await ReadSchema(ensurePath);
Dictionary<string, string> migrateSchema = await ReadSchema(migratePath);

bool hadLock = migrateSchema.Remove("__EFMigrationsLock");
migrateSchema.Remove("__EFMigrationsHistory");

Console.WriteLine($"        (EF-internal tables excluded from comparison: __EFMigrationsHistory, __EFMigrationsLock present={hadLock})");

Check($"same application-object count (EnsureCreated={ensureSchema.Count}, Migrate={migrateSchema.Count})", ensureSchema.Count == migrateSchema.Count);

foreach (KeyValuePair<string, string> entry in ensureSchema)
{
    bool present = migrateSchema.TryGetValue(entry.Key, out string? migrateSql);
    bool identical = present && string.Equals(entry.Value, migrateSql, StringComparison.Ordinal);

    Check($"schema matches for {entry.Key}", identical);

    if (present && !identical)
    {
        Console.WriteLine($"        EnsureCreated: {entry.Value}");
        Console.WriteLine($"        Migrate      : {migrateSql}");
    }

}

foreach (string name in migrateSchema.Keys)
{

    if (!ensureSchema.ContainsKey(name))
    {
        Check($"unexpected extra object in migrated schema: {name}", false);
    }

}

async Task<Dictionary<string, string>> ReadSchema(string path)
{
    Dictionary<string, string> schema = new Dictionary<string, string>();

    await using AppDbContext db = Open(path);
    await db.Database.OpenConnectionAsync();
    await using System.Data.Common.DbCommand command = db.Database.GetDbConnection().CreateCommand();
    command.CommandText = "SELECT name, COALESCE(sql, '') FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY name;";

    await using System.Data.Common.DbDataReader reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        string name = reader.GetString(0);
        string sql = reader.GetString(1).Replace("\r\n", " ").Replace("\n", " ").Replace("  ", " ").Trim();
        schema[name] = sql;
    }

    return schema;
}

async Task<bool> CanInsert(string path)
{
    bool ok;

    try
    {
        await using AppDbContext db = Open(path);
        db.Devices.Add(new Device { MacAddress = "11:22:33:44:55:66", IpAddress = "192.168.1.51", Hostname = "new-device" });
        await db.SaveChangesAsync();
        ok = await db.Devices.CountAsync() == 1;
    }
    catch (Exception exception)
    {
        Console.WriteLine("        insert threw: " + exception.Message);
        ok = false;
    }

    return ok;
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? $"ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
return failures == 0 ? 0 : 1;

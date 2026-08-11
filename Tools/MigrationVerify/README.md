# MigrationVerify

Proves that a schema change ships safely to the databases already on users' machines.

CLAUDE.md's Database rules say every schema change ships an EF Core migration, and that "delete the DB and let it rebuild" is not an acceptable fix now the app is publicly released. This tool is how that claim gets checked instead of assumed. Run it whenever you add a migration.

## Why it exists as a separate project

`NetworkMonitor.Services` targets `net10.0-windows` with `UseWinUI`, and the EF design host cannot load it — it fails resolving the `Microsoft.Windows.SDK.NET` runtime pack. The DB layer itself (`AppDbContext`, `AppPaths`, `AppDbContextDesignTimeFactory`, `DatabaseInitializer`, `AppLog`, and the `Migrations` folder) is platform-neutral, so this plain `net10.0` host compiles those files directly via `<Compile Include>` links. There is no copy of the source: edit the real files and this picks the change up.

That is also how migrations are generated:

```
dotnet ef migrations add <Name> --project Tools/MigrationVerify --startup-project Tools/MigrationVerify --namespace NetworkMonitor.Services.Data.Migrations
```

then move the generated files into `NetworkMonitor.Services/Data/Migrations/`.

## Running it

```
dotnet run --project Tools/MigrationVerify
```

Exit code 0 means every check passed; 1 means at least one failed, and the failing check is named. It works entirely in `%TEMP%\nm-migration-verify` and **never touches the real database** at `%LOCALAPPDATA%\UmnathaNetworkMonitor\networkmonitor.db`.

## What it checks

1. **An existing pre-migration database** — the v0.0.8-to-v0.0.11 case, created by `EnsureCreated` with no `__EFMigrationsHistory` table. Seeds a row, runs `DatabaseInitializer`, then confirms the history table is created, `InitialCreate` is recorded as *applied* rather than replayed, nothing is pending, and the seeded row survives with its content intact. This is the case that would otherwise throw `SqliteException: no such column` on every existing install.
2. **A fresh install** with no database file — confirms the tables are created, the migration is recorded, nothing is pending, and an insert works against the resulting schema.
3. **A second launch** against an already-baselined database — confirms baselining is idempotent and does not duplicate history rows or touch data.
4. **Schema equivalence** — builds one database with `EnsureCreated` and another with `MigrateAsync`, then compares `sqlite_master` object by object. This is the check that matters most: baselining asserts the migration produces what `EnsureCreated` produced, and if that is false the mismatch is silent.

`__EFMigrationsHistory` and `__EFMigrationsLock` are excluded from check 4 — they are EF-internal. Note that `MigrateAsync` does create `__EFMigrationsLock` in user databases, which `EnsureCreated` never did; it is a migration concurrency lock and carries no application data.

## Not a solution project

Per CLAUDE.md's `/Tools/` policy this is registered in `NetworkMonitor.slnx` as a folder of files, not as a `<Project>`, so `dotnet build NetworkMonitor.slnx` stays clean. It pins `SQLitePCLRaw.bundle_e_sqlite3` the way the app does.

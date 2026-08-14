# HistoryRestore

Puts device history back into a live database from one of the app's own backups, without
discarding anything the live database has gained since that backup was taken.

## Why it exists

`HistoryPurgeDays` is a retention setting, and retention is a one-way door. Lower it and the next
scan runs `ScanWorker.PurgeOldHistoryAsync`, which deletes every `DeviceEvent` older than the new
cutoff. Setting the number back does not bring them back.

Restoring a backup file wholesale would undo the deletion, but it would also throw away every
scan, traffic sample, speed test and digest recorded since that backup was taken. This tool merges
instead: it inserts only the rows the live database is missing, and it never updates or deletes an
existing row.

It was written on 2026-08-14 after a retention change cost 894 device events and 516 scan
sessions, all of which it recovered.

## Usage

```
dotnet run --project Tools/HistoryRestore -- <backup.db> [--live <path>] [--apply]
```

| Argument | Meaning |
|---|---|
| `<backup.db>` | A backup from `%LOCALAPPDATA%\UmnathaNetworkMonitor\Backups`. Required. |
| `--live <path>` | Target database. Defaults to the installed app's `networkmonitor.db`. |
| `--apply` | Write the rows. Without it this is a **dry run** and changes nothing. |

Run it without `--apply` first and read the report. Exit code 0 means success or a clean dry run;
1 means it refused or could not run.

Example:

```
dotnet run --project Tools/HistoryRestore -- "%LOCALAPPDATA%\UmnathaNetworkMonitor\Backups\networkmonitor_2026-08-13_13-58-30.db"
```

## What it does

Restores `DeviceEvents` and `ScanSessions` — the two tables the history purge deletes from.
Traffic and digest retention are separate settings with their own purge paths and are deliberately
not touched.

Before writing anything it checks:

- **Same `Id`, different content.** If a row exists in both databases under one primary key but
  the two disagree, the databases are not a parent and child of one history and merging them would
  corrupt it. Refuses.
- **Events with no matching device.** `DeviceEvents.DeviceId` is a foreign key to `Devices`. If the
  backup references devices the live database no longer holds, the insert would violate it.
  Refuses.
- **A non-empty `-wal` file**, which usually means the app is still running. Refuses to write.

When applying, it copies the live database to `networkmonitor_pre-restore_<timestamp>.db` beside
it first, then inserts inside a single transaction. If anything looks wrong afterwards, copy that
file back over `networkmonitor.db`.

Column lists are read from the live schema with `PRAGMA table_info` rather than hardcoded, so a
table that gains a column in a later migration still restores correctly instead of silently
dropping it.

## Close the app first

The tool refuses to write while the write-ahead log is non-empty, but close the app before running
it with `--apply` regardless. A dry run is safe at any time — it opens the database read-only.

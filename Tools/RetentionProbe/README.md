# RetentionProbe

Diagnostic for the traffic-retention design in `TrafficTracker`. It answers, with numbers rather
than eyeballing, the questions the smoke checklist asks about storage retention:

- Does the raw-entry purge finish inside its 120-second watchdog?
- Does the database file settle, and how much of it is reusable free space?
- Do the rollups survive the purge and still carry history beyond the raw window?

It is **not** part of the solution build — it lives in the slnx as a solution folder of files, the
same way `Installer/` does. Run it from the command line when you need it.

## Running it

**This tool deletes rows.** Copy the database first and point the probe at the copy. It refuses to
open anything inside `%LOCALAPPDATA%\UmnathaNetworkMonitor`, but copying is the habit that matters.

```
copy %LOCALAPPDATA%\UmnathaNetworkMonitor\networkmonitor.db* C:\temp\probe\
cd Tools\RetentionProbe
dotnet run -- C:\temp\probe\networkmonitor.db 2
```

The second argument is the retention window in minutes. It defaults to **60**, matching
`TrafficTracker.RawEntryRetention`. Pass something small — 2 is a good choice — to push every raw
row past the cutoff and time a full-size purge.

Copy the `-wal` and `-shm` files too. A live database keeps recent writes in the WAL, and without it
the copy is missing whatever hasn't been checkpointed.

## Reading the output

The one result that surprises people: **the file is supposed to plateau, not shrink.** There is no
`VACUUM` and no `auto_vacuum` anywhere in the codebase, so deleted pages go onto the freelist and
get reused by later writes. A file that stops growing is the pass condition. The `BEFORE`/`AFTER
pages` lines show how much of the file is already reusable.

A large `-wal` is also normal. The probe runs `PRAGMA wal_checkpoint(TRUNCATE)` — the same thing
`MainWindow.CheckpointDatabase()` does on the graceful exit path — and the WAL collapses to zero.

`MinuteEpoch` is unix **seconds** truncated to a minute boundary, not minutes — see
`TrafficTracker.MinuteEpochFor`. Getting that wrong makes every window query match every row.

## Baseline, 2026-07-28

Recorded so a later run has something to compare against. Full context in
`Documents/Code Review/2026-07-27/smoke-checklist.md`.

| Measure | Value |
|---|---|
| Raw rows purged | 1 432 `TrafficEntries` + 4 908 `LocalTrafficEntries` |
| Purge time | 0.02 s against the 120 s watchdog (0.02% of budget) |
| File | 90.9 MB, 68.4 MB already free; unchanged at 90.9 MB after the purge |
| WAL | 48.2 MB → 0.0 MB on checkpoint |
| Rollups | 14 526 + 147 691 rows spanning 5 days, identical before and after |
| Coverage | 0 raw-carrying minutes lacked a matching rollup minute |

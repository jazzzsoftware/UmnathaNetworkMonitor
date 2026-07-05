# Chunk 7 — Backup (reviewed 2026-06-23)

Single new file (`DatabaseBackupWorker`, added this session). Self-reviewed critically. The design (SQLite online-backup snapshot + approved-devices CSV, restart-aware cadence) is sound, but there's **one real bug I introduced**: a tight retry loop on persistent backup failure.

---

## Findings

### C7-1 [RISK] Tight infinite loop if backups keep failing
`DatabaseBackupWorker.cs` — `GetDelayUntilNextBackup()` derives the wait from the **newest existing `networkmonitor_*.db` file**; if none exists it returns `TimeSpan.Zero`. The `ExecuteAsync` loop swallows a generic `Exception` and immediately re-iterates. So if `CreateBackupAsync` always fails before writing a file (disk full, permission denied, path issue), **no backup file is ever created → delay stays `Zero` → the loop spins as fast as the CPU allows**, logging nothing. Only `OperationCanceledException` breaks out.
**Fix:** after each attempt (success or failure) always wait at least a floor interval before retrying — e.g. compute the delay, and on a caught exception `await Task.Delay(BackupInterval, ct)` (or a backoff) instead of looping immediately.
Status: **FIXED 2026-06-25 (batch 1)** — added `RetryFloor` (5 min); `CreateBackupAsync` now returns `bool`, and the loop waits `RetryFloor` whenever no backup was created (exception *or* missing source DB), so it can no longer tight-spin.

### C7-2 [RISK, low] No retention — backups grow unbounded
By design (per the feature request, no auto-purge), but one `.db` + one `.csv` per day accumulate forever; a multi-MB DB × 365/yr adds up. Flagged so it's a conscious choice. Optional: keep last N days.
Status: **FIXED 2026-06-25 (backup batch) via U7-1** — `RetentionDays = 3`; after each successful backup, `PruneOldBackups` deletes `networkmonitor_*.db` and `approved-devices_*.csv` older than 3 days (by parsed filename timestamp).

### C7-3 [RISK, low] DB snapshot and CSV export are not atomic
`CreateBackupAsync` writes the `.db` snapshot then the approved-devices `.csv`. If the CSV step throws, the `.db` is already written, so the next run isn't due for ~24h — leaving a `.db` with no matching `.csv` for that timestamp. Minor; cosmetic mismatch only.
Status: **FIXED 2026-06-25 (backup batch)** — `CreateBackupAsync` now wraps the CSV export in try/catch; on failure it deletes the just-written `.db` (`TryDelete`) and rethrows, so the pair is all-or-nothing. The rethrow leaves `backupCreated = false` → the retry-floor (5 min) retries instead of waiting ~24h.

### C7-4 [CLEANUP] Cadence keys off file write-time, and only off the `.db` file
`GetNewestBackupTimeUtc` uses `File.GetLastWriteTimeUtc` of `networkmonitor_*.db`. If a user touches/copies backup files the schedule shifts, and a failed CSV (C7-3) won't be retried until the next `.db` is due. Low impact; noting for awareness. Could instead persist the last successful backup time.
Status: **FIXED 2026-06-25 (backup batch)** — cadence now keys off the timestamp **embedded in the filename** (`ParseBackupTimestampUtc` parses `yyyy-MM-dd_HH-mm-ss` → UTC), not `File.GetLastWriteTimeUtc`. Immune to file touches/copies. (Failed-CSV retry is now handled by C7-3's delete-and-retry-floor.)

---

## Notes (not findings)
- Using the SQLite **online backup API** (`SqliteConnection.BackupDatabase`) is the correct choice for snapshotting a live WAL database — consistent without needing the app's checkpoint.
- The restart-aware "only back up if newest is older than 24h" check correctly avoids spamming backups on frequent restarts (its only flaw is the failure case, C7-1).
- Convention compliance (single exit, blank-line blocks, explicit types, no `var`) looks correct for this file.
- Shares the app-data-path duplication noted in C3-7.

## Triage / actions
No fixes applied (record-only). Priority when fixing: **C7-1** (real bug — add a retry floor). C7-2 is a product decision; C7-3/C7-4 are minor.

---

## Files reviewed
- `NetworkMonitor/Services/DatabaseBackupWorker.cs`

## User findings (reconciled)

### U7-1 [ACTION — resolves C7-2] Backup retention = 3 days
Decision on C7-2: backups are **not** unbounded. Implement a retention policy that keeps only the **last 3 days** of backups; delete older `networkmonitor_*.db` and matching `*.csv` files. Implement in the fix phase (in `DatabaseBackupWorker`, prune after each successful backup).
**Note:** retention by file age interacts with C7-3/C7-4 (the `.db`/`.csv` pairing and write-time-based cadence) — prune both file types by their shared timestamp so a `.db` and its `.csv` are removed together.
Status: open (batch — fix phase). **C7-2 RESOLVED: 3-day retention.**

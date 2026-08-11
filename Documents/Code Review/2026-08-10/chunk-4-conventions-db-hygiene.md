# Chunk 4 — Conventions, DB & project hygiene

Range `c07260c..b215581`. Ledger: `progress.md`.

**8 findings — 0 BUG · 2 RISK · 6 CLEANUP. All `open`.**

## What was verified as correct

Audited by full-diff scan, not spot check.

- **Zero `var`**, zero `""` literals, zero single-character identifiers, zero `[ObservableProperty]`, zero inline `{ get; set; }` property braces, across every added and changed `.cs` file in the range.
- **Backing-field-above-property is followed exactly** in every new observable type: `MiniGraphViewModel.cs:28-74` (six hand-written `SetProperty` properties), `SettingsViewModel.cs:319-455` (eight more), `AllDevicesViewModel.cs:96-111`. All in the Properties section, all after the constructor.
- **`LiveRateBuffer.cs:30-32`** — flagged for scrutiny. `_capacity` is declared immediately above the `Capacity` property it backs, inside the Properties section, after the constructor. **That is the rule, not a violation.** (A violation was alleged in the coordinator's first pass and withdrawn — see the ledger Log.)
- **`MiniGraphWindow.xaml.cs` member order is correct**: fields (20-72) → constructor (74) → `ViewModel` property (135) → public methods (140-178) → private methods (180+). The `[DllImport]` block (180-211) and the nested `NativePoint`/`NativeRect` structs (213-227) open the private section — they *are* private static methods and nested types, both covered by the stated interop and nested-type exemptions.
- **`LiveTrafficFeed.cs` is clean**: fields 29-37, event 39, two backing-field/property pairs 41-73, public methods 75-117, private methods 119-273. Single exit point throughout.
- **Layering respected.** All new pure, testable logic went to Core/Models — `AxisScale`, `FlushSpread`, `LiveRateBuffer`, `HorizontalStripMetrics`, `MiniGraphFormatter` — each with a matching test file. `MiniGraphState` and `TaskbarTopmostGuard` are correctly in Services (settings- and Windows-bound). **Exceptions to this are C4-3 and chunk 5's C5-3.**
- **XAML is close to exemplary.** `MiniGraphWindow.xaml` and `MiniTrafficSection.xaml` get attribute-per-line, blank lines around every element, ` />` / `>` placement, and — the rule easiest to get wrong — the **attribute order** right throughout: `MiniGraphWindow.xaml:48-55` runs `x:Name`/`Grid.Row`/`Label`/`MinHeight`/`Margin` (simple) → `DoubleTapped` (event) → `Points="{x:Bind …}"` (binding) last. Same at 76-79, 103-107, 111-122. `AllDevicesPage.xaml:40-46` and the `SettingsPage.xaml` mini-graph card follow it too.
- **`NetworkMonitor.slnx` is fully in sync.** All 11 non-code files added in this range are registered: 5 images under `/Documents/Images/`, `Documents/Posts/2026-08-07 Reddit r-homelab.md`, `Documents/Release Notes (pending).md`, 2 plans and 2 specs. **`Tools/RetentionProbe/` is registered as a folder of files** — `Program.cs`, `README.md` *and* `RetentionProbe.csproj` as `<File Path=…>`, not `<Project>` — so `dotnet build NetworkMonitor.slnx` stays clean, exactly as the policy requires. There is no `Directory.Build.props` that could pull it in. `RetentionProbe.csproj` also pins `SQLitePCLRaw.bundle_e_sqlite3 3.0.3` the way the app does, with an inline comment saying why.
- **CLAUDE.md was itself updated in-range** (`c1b63be..293a2ef`) with the new Database section, the `/Tools/` policy, the solution-folder layout and five new Key Files rows. Doc hygiene here is a strength, not an afterthought.
- **Versioning is consistent.** `<Version>0.0.11`, tag `v0.0.11` and the top release-notes heading all agree. The switch from the runtime-written `ReleaseNotesVersion` to literal headings reads as a regression until you find `Documents/Release Notes (pending).md`: it is a deliberate fix for a real defect (`<Version>` bumps at installer-build time, so the runtime heading mislabelled unreleased notes as shipped), with a documented 3-step release procedure.

---

## Database impact — NONE

`NetworkMonitor.Services/Data/AppDbContext.cs` is **byte-identical** between `c07260c` and `b215581`: no new `DbSet`, no changed `HasIndex`/`HasKey`/`OnDelete`. The three files added under `NetworkMonitor.Models/` are `Formatting/MiniGraphFormatter.cs`, `Formatting/TrafficRateFormatter.cs` (one new static method) and `Widget/MiniGraphOrientation.cs` (a two-member enum) — none is an entity, and none is referenced by any entity or mapped property.

The ~96 new lines in `NetworkMonitor.Services/Data/Settings.cs` (`DevicesOnlineOnly` plus fifteen `MiniGraph*` keys) are **`settings.json` preferences, not schema** — `Settings` is a plain POCO serialised by `System.Text.Json`, never a `DbSet`.

Backward compatibility with an old `settings.json` is sound. Every new key carries a C# property initialiser, so keys absent from an existing file deserialise to their declared default:

| Key | Default |
|---|---|
| `ShowMiniGraph` | `false` |
| `MiniGraphShowInternet` / `ShowLocal` / `ShowSpeedTest` / `ShowUnknownDevices` | `true` |
| `MiniGraphX` / `MiniGraphY` | `int.MinValue` (explicit "never placed" sentinel) |
| `MiniGraphOpacity` | `100` |
| `MiniGraphStripHeight` | `40` |
| `MiniGraphShowBorder` | `true` |

The risky ones are clamped on read rather than trusted: `MiniGraphState.cs:80` clamps opacity to 50–100, and `HorizontalStripMetrics.ClampHeight` bounds strip height to 40–120 on both save (`MiniGraphState.cs:126`) and restore (`MiniGraphWindow.xaml.cs:452`). A hand-edited or corrupt value cannot produce an invisible or unusable widget.

The only DB-adjacent change is `DatabaseCheckpoint.cs`, which runs a `PRAGMA`, not DDL.

**No migration was required and none is missing.**

---

## C4-1 `[RISK]` — the migration baseline is still absent, so the *next* schema change cannot ship safely

`NetworkMonitor/App.xaml.cs:219`

`App.xaml.cs:219` still calls `await db.Database.EnsureCreatedAsync()`, and `git ls-tree -r b215581 | grep -i migration` returns nothing: there is **no `Migrations/` folder anywhere in the repo**. CLAUDE.md's statement that this "still needs converting" is accurate at HEAD.

**This range is not at fault** — it changed no schema. The risk is recorded here because it compounds: v0.0.8, v0.0.9, v0.0.10 and v0.0.11 databases in the field were all created by `EnsureCreated` and have no `__EFMigrationsHistory` table. `EnsureCreated` is a no-op against an existing file, so the first developer to add a column gets a working dev machine and a `SqliteException: no such column` on **every existing user's install**.

**Fix.** A one-time baseline: generate `InitialCreate` from the current model, then switch to `MigrateAsync()` with the initial migration marked as applied for pre-existing databases. Do it **before** the next entity change, not with it.

**Status:** `open` — pre-existing debt, not introduced here

---

## C4-2 `[RISK]` — `DatabaseCheckpoint` cannot tell a successful checkpoint from a blocked one

`NetworkMonitor.Services/Data/DatabaseCheckpoint.cs:17, 23`

Assessing each aspect:

- **Connection string** (`:17`) — `$"Data Source={AppDbContext.DbPath}"` is string-concatenated rather than built with `SqliteConnectionStringBuilder`, which the sibling tool `Tools/RetentionProbe/Program.cs:53-56` does use. It works for the real path, but `Data Source=` defaults to `Mode=ReadWriteCreate`, so if `DbPath` were ever wrong this silently **creates an empty database** instead of failing.
- **Racing other writers** (`:23`) — this is the real one. `PRAGMA wal_checkpoint(TRUNCATE)` does **not throw** when it is blocked; it returns a row `(busy, log, checkpointed)` with `busy = 1`. `ExecuteNonQuery()` discards that row, so if any other connection still holds the file the WAL is simply not truncated and **nothing is logged**. A WAL that fails to truncate at shutdown is indistinguishable from one that succeeds.
- **Failure handling** — the `catch` is correct, and the comment explaining why it bypasses the disposed DI container is genuinely useful.
- **Exit paths** — called from exactly one place, `MainWindow.xaml.cs:307` in `ShutdownGracefully()`, reached from `OnAppWindowClosing` (exit requested), `OnExitApp` (tray) and `ShutdownForUpdate`, each guarded by `_shutdownCompleted`. That covers every intentional exit, and placement after `StopHost()` is right.
- **Minor** — `RetentionProbe` calls `SqliteConnection.ClearAllPools()` after closing; `DatabaseCheckpoint` does not, so the pooled handle survives the `using`. Immaterial because `Environment.Exit(0)` follows immediately, but worth matching for symmetry.

**Fix.** Use `ExecuteScalar` / a reader and log when `busy != 0`. Build the connection string with `SqliteConnectionStringBuilder` and set `Mode = ReadWrite` so a wrong path fails loudly.

**Status:** `open`

---

## C4-3 `[CLEANUP]` — `SpreadAcrossBuckets` is duplicated verbatim in the app layer

`NetworkMonitor/ViewModels/InternetViewModel.cs:369-392` · `NetworkMonitor/ViewModels/LocalViewModel.cs:387-413`

Byte-for-byte identical ~25-line methods. This is pure, deterministic, testable logic sitting in the **UI project**, which is precisely what CLAUDE.md's "new pure logic that needs tests goes in Core, not Services" is aimed at.

The irony is that `FlushSpread.Distribute` underneath it was correctly put in Core **and tested** (`FlushSpreadTests.cs`, 147 lines) — it is the bucket-mapping wrapper around it that stayed behind.

**Fix.** One `Core/Traffic` helper taking `IReadOnlyList<ChartPoint>` removes the copy and makes it testable. See also chunk 3's C3-4, which is a defect in exactly this path.

**Status:** `open`

---

## C4-4 `[CLEANUP]` — blank-line rule around object-initializer blocks, applied inconsistently within one file

`NetworkMonitor/MiniGraphWindow.xaml.cs`

No blank line after the initializer's closing `};` before the next statement at **:92→93, :98→99, :104→105, :112→113, :1039→1040, :1046→1047**; and no blank line after the opening `{` at **:314→315**, where the first statement is an initializer block.

The same file gets it right eight lines later each time (:1065→1066, :1078→1079, :1090→1091, :1102→1103, :1133→1134), so this is inconsistency rather than a house style.

**Status:** `open`

---

## C4-5 `[CLEANUP]` — missing blank line before a method's closing brace

`NetworkMonitor/Views/Controls/TrafficAreaChart.xaml.cs:600-601`

`DrawCompactAxis` ends with an `if` block whose closing `}` is immediately followed by the method's closing `}`. Clear-cut violation of "a blank line immediately before the closing `}` of any method when the last statement ends with `}`". Every other method in the changed region observes it — note the deliberate blank line *added* at `:461` for exactly this reason.

**Status:** `open`

---

## C4-6 `[CLEANUP]` — `RetentionProbe` has multiple exit points and an implicitly typed array

`Tools/RetentionProbe/Program.cs:24, 28, 39, 46, 122, 133, 159`

- `CompareMinutes` has an early `return;` at `:159` with the method continuing to `:169`.
- The top-level program body returns at `:24, :39, :46` and `:133` — four exits.
- `:122` uses `foreach (int hours in new[] { 1, 6, 24 })`, an implicitly typed array, against the "always explicit types" rule (`new int[] { … }` is the explicit form).

Guard clauses in a throwaway CLI are defensible, but CLAUDE.md grants `/Tools/` no exemption from the coding conventions — it exempts it only from being a solution project.

**Status:** `open`

---

## C4-7 `[CLEANUP]` — `SettingsViewModel` member order interleaves public and private

`NetworkMonitor/ViewModels/SettingsViewModel.cs:511`

The new `public void SyncMiniGraphFromState()` is inserted between the private `OnSettingChanged` and the public `PurgeHistoryAsync`. The file already broke this rule before the change, so this perpetuates rather than introduces it.

The related backing-field asymmetry in the same file is recorded separately as **C1-8**.

**Status:** `open`

---

## C4-8 `[CLEANUP]` — cosmetic XAML attribute-order inconsistency

`NetworkMonitor/Views/TrafficHostPage.xaml:17-19, 37-39`

`Grid.Column` is placed before `x:Name` on the new `SelectorBar` and `ToggleButton`, whereas `MiniGraphWindow.xaml:48-55` and the rest of the codebase lead with `x:Name`. Both are simple assignments, so the documented ordering rule is not broken — it is a cosmetic inconsistency, and it matches what the file already did.

**Status:** `open`

---

## C4-9 `[CLEANUP]` — CLAUDE.md's canonical XAML reference points at a file that doesn't exist

`CLAUDE.md:88`

The line states "`DevicesPage.xaml` is the canonical formatting reference," but no such file exists — `NetworkMonitor/Views/` contains `AllDevicesPage.xaml` and `DevicesHostPage.xaml`. CLAUDE.md was edited in this very range without catching it. A reviewer or agent following the instruction literally has nothing to open.

Related cosmetic nit: `NetworkMonitor.slnx` mixes `Documents/superpowers/…` and `Documents/Superpowers/…` casing in `Path` attributes within the same folder node. Harmless on Windows, pre-existing.

**Status:** `open`

---

## Files reviewed

Every added and changed `.cs` and `.xaml` file in the range, plus:

- `CLAUDE.md`, `NetworkMonitor.slnx`
- `NetworkMonitor.Services/Data/AppDbContext.cs`, `Settings.cs`, `DatabaseCheckpoint.cs`
- `NetworkMonitor/NetworkMonitor.csproj`, `Tools/RetentionProbe/RetentionProbe.csproj`
- `NetworkMonitor/Views/SettingsPage.xaml` (ReleaseNotesDialog), `Documents/Release Notes (pending).md`
- `NetworkMonitor/Views/DevicesPage.xaml` — **does not exist** (C4-9)

## User findings

_(to be filled in at co-review — assign `U4-n` IDs)_

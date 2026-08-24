# NetworkMonitor

## Project

WinUI 3 desktop app (.NET 10, unpackaged) that periodically scans the local network, tracks devices by MAC address, and maintains a known-devices list.

Five projects: **NetworkMonitor** (the WinUI app — pure UI shell: App/MainWindow/Splash, Views, ViewModels, Converters), **NetworkMonitor.Models** (net10.0 class library — all model types plus the shared unit formatters), **NetworkMonitor.Core** (net10.0 class library — pure, UI-free service logic: classifiers, grouper, CSV, digest builder/schedule, speed-test maths, mDNS parsing, OuiDatabase), **NetworkMonitor.Services** (net10.0-windows class library — background workers, ETW collector, EF context + Settings under `Data\`, digest renderer/PDF, platform services; UseWinUI for the Win2D renderer), and **NetworkMonitor.Tests** (xunit — references Models + Core via ProjectReference only; no source links). Layering: Models ← Core ← Services ← App. In every project each sub-folder is its own namespace (e.g. `NetworkMonitor.Services.Traffic`). New pure logic that needs tests goes in Core, not Services. In Solution Explorer the first four sit under the `/App/` solution folder and the test project under `/Tests/`.

## Stack

- **UI**: WinUI 3 (Windows App SDK), Blazor NavigationView shell
- **MVVM**: CommunityToolkit.Mvvm (source generators)
- **DataGrid**: CommunityToolkit.WinUI.UI.Controls.DataGrid 7.x
- **ORM**: EF Core 10 + SQLite (migrations required — see Database below)
- **DI / background**: Microsoft.Extensions.Hosting, BackgroundService
- **Scanning**: System.Net.NetworkInformation.Ping + `arp -a` + Dns.GetHostEntryAsync

## Database

**Every schema change ships an EF Core migration, in the same commit as the change.** A schema change is anything that adds, removes or alters a table, column or index — including a new `DbSet`, a new property on an existing entity, and a changed key or index definition.

The app is publicly released, so a user's `networkmonitor.db` holds the only copy of their device and traffic history. "Delete the DB and let it rebuild" was acceptable while the app was pre-release and the user was the sole tester; it is not acceptable now and must never be offered as the fix for a schema change. Without a migration, an updated build either throws on startup against the old schema or silently loses history.

Practical rules:

- Author the migration alongside the code change; never defer it to "later" or to the release step.
- Startup applies pending migrations. `App.xaml.cs` calls `DatabaseInitializer.InitializeAsync(db)`, which baselines then migrates; `EnsureCreatedAsync` is gone from the codebase. A database that has application tables but no `__EFMigrationsHistory` (anything created by the pre-v0.0.12 `EnsureCreated` path) gets the initial migration written into the history table as **already applied** rather than replayed onto it, so `MigrateAsync` then applies only what came after. Never reintroduce `EnsureCreated` — it is a no-op against an existing file and would silently skip every later migration.
- Migrations live in `NetworkMonitor.Services/Data/Migrations/`. Generate them through `Tools/MigrationVerify`, not through the Services project directly — Services is `net10.0-windows` with `UseWinUI` and the EF design host cannot load it (it fails to resolve the `Microsoft.Windows.SDK.NET` runtime pack). See `Tools/MigrationVerify/README.md` for the command.
- **Run `dotnet run --project Tools/MigrationVerify` after adding a migration.** It builds a database the old way and a database the new way and diffs `sqlite_master` object by object, plus proves an existing pre-migration database keeps its rows. Exit code 0 or the migration is not safe to ship. It works in `%TEMP%` and never touches the real database.
- Migrations are additive where possible. Prefer a nullable column with a sensible default over a destructive rewrite.
- **State the DB impact of every change**, even when the answer is "none". Changes to `settings.json` preferences, UI, or pure runtime behaviour are not schema changes and need no migration — say so explicitly.
- DB location: `%LOCALAPPDATA%\UmnathaNetworkMonitor\networkmonitor.db` (plus `-wal` and `-shm`). Verify against `AppPaths.cs` before quoting it.

## Build

Open `NetworkMonitor.slnx` in Visual Studio 2026. Set platform to **x64** (not Any CPU — WinUI 3 does not support Any CPU). Run restore before first build.

Building from the command line is not equivalent. `dotnet build NetworkMonitor.slnx` emits one `WIN2D0001` warning against `NetworkMonitor.Services` — the slnx does not propagate the platform down to the project, so Win2D sees an Any CPU build and says `Microsoft.Graphics.Canvas.dll` cannot be referenced correctly. Passing `-p:Platform=x64` does not suppress it. The Visual Studio x64 build is clean. Treat the CLI as a quick compile check, not as the definition of a clean build.

New root-level files (docs, config) must be added to `NetworkMonitor.slnx` so they appear in Solution Explorer — non-project files aren't picked up automatically. Use `/AI/` for AI-assistant docs (`CLAUDE.md`), `/Project/GitHub/` for the repo's public paperwork (`README.md`, `CONTRIBUTING.md`, `SECURITY.md`, `LICENSE`, `NOTICE.md`), `/Project/Config/` for machine-read root config (`.editorconfig`, `.gitattributes`, `.gitignore`), and `/Documents/` for user-facing docs.

Solution-folder layout: the five projects are grouped under `/App/` (NetworkMonitor, Models, Core, Services) and `/Tests/` (NetworkMonitor.Tests). Solution folders are virtual — they group the project entries in Solution Explorer and change nothing on disk.

`/Tools/` holds standalone tooling — things you run, not things that ship. It is a real on-disk folder registered in the slnx as folders of files, deliberately **not** as buildable projects, so `dotnet build NetworkMonitor.slnx` never attempts to build them. It currently holds `Tools/Installer/` (the Inno Setup script + `build-installer.ps1`), `Tools/RetentionProbe/` (a command-line diagnostic for the traffic-retention design), `Tools/MigrationVerify/` (generates EF migrations and proves they ship safely to existing user databases), `Tools/HistoryRestore/` (merges purged device history back into the live database from a timestamped backup) and `Tools/UITests/` (the nine-phase FlaUI suite that drives the installed app end to end). New tooling goes here; anything with a `.csproj` under `/Tools/` should pin its packages the way the app does and must not be added as a solution project.

## Coding Conventions

- **No `var`** — always use explicit types.
- **No single-character variable names** — use descriptive names everywhere, including pattern-match variables and lambda parameters (e.g. `value is bool isOnline && isOnline`, not `value is bool b && b`).
- **Always use curly braces** on `if`, `else`, `for`, `foreach`, `while`, `using` blocks — even single-line bodies.
- Primary constructors are fine for simple DI injection.
- Prefer `string.Empty` over `""` for empty string initialisation.
- No comments unless the WHY is non-obvious.
- No trailing summary comments after methods.
- **One type per file** — each top-level type (class, record, struct, enum, interface) lives in its own file named exactly after it (`Foo` → `Foo.cs`). Nested types (declared inside another type) are exempt. The sole exception is a cohesive block of P/Invoke / COM interop declarations (interop structs and `[ComImport]` interfaces), which may stay in the file of the API that consumes them.
- **Single exit point** — every method has exactly one `return` statement, at the end.
- **Blank lines around all blocks** — every `if`, `else`, `foreach`, `for`, `while`, `switch`, `try`, `catch`, `finally`, `using`, and any other code block must have a blank line above and below it. No exceptions — this includes a blank line immediately after the opening `{` of any method/constructor/outer block when the first statement inside is a block, and a blank line immediately before the closing `}` of any method/constructor/outer block when the last statement inside ends with a `}`. The rule applies at every nesting level without exception.
- **Returns stand alone** — assign any computed value to a local variable first, then `return` that variable; no inline computation (ternary, switch, `new`, method chains) in the `return` statement itself. Always place a blank line above the `return`.
- **Class member order** — Fields → Constructor → Properties → Public methods → Override methods → Private methods. Non-property fields (injected dependencies, internal state) stay grouped in the Fields section before the constructor. A property's own backing field does NOT go there — it moves down into the Properties section (see next rule).
- **Backing field above its property** — a property's private backing field is declared immediately above the property it backs, separated by a blank line, inside the Properties section. Do NOT group backing fields with the other fields and do NOT put the property before the constructor. Hand-write the property with `SetProperty(ref _field, value)` (CommunityToolkit.Mvvm `ObservableObject`) — do not use the `[ObservableProperty]` source-generator attribute. Example:
  ```csharp
  private double _timeRangeHours = 5.0 / 60.0;

  public double TimeRangeHours
  {
      get => _timeRangeHours;
      set
      {

          if (SetProperty(ref _timeRangeHours, value))
          {
              _settings.InternetTimeRangeHours = value;
              _settings.Save();
              _ = LoadAsync(true);
          }

      }
  }
  ```
- **Property braces** — `{`, `get;`, `set;` (and `init;` / `private set;`) each on their own line. Expression-bodied properties (`=>`) are exempt.
- **No underscores in identifiers** — use camelCase for all local variables, parameters, and method names. Private fields use a leading underscore only (e.g. `_fieldName`); no underscores anywhere else in any identifier.

## XAML Formatting

- Blank line between `<?xml ...?>` and the root element.
- Element name on its own line; every attribute on its own line, indented 4 spaces from the opening `<`.
- No inline multi-attribute elements; no alignment spaces between attributes.
- **Attribute order within an element**: simple assignments first (literals and resource/theme references), then event handlers (`Click`, `ValueChanged`, etc.) and `Command` bindings, then value-assignment bindings (`Value="{x:Bind ...}"`, `Text="{x:Bind ...}"`, `IsOn="{x:Bind ...}"`, etc.) last.
- Self-closing elements: ` />` on the last attribute line.
- Container elements: `>` on the last attribute line.
- Blank line above and below every element — sibling elements are separated by a blank line; every container element has a blank line after its opening tag and before its closing tag.
- `AllDevicesPage.xaml` is the canonical formatting reference. (`MiniGraphWindow.xaml` is a good second example, and is the one to copy for attribute order.)

## Key Files

| File | Purpose |
|---|---|
| `NetworkMonitor/App.xaml.cs` | IHost build + DI registration + DB init |
| `NetworkMonitor.Services/Data/AppDbContext.cs` | EF context; DbPath points to LocalApplicationData |
| `NetworkMonitor.Services/Data/DatabaseInitializer.cs` | Baselines a pre-migration database, then `MigrateAsync` + WAL pragma |
| `NetworkMonitor.Core/Data/OuiDatabase.cs` | Loads oui.txt → MAC prefix → vendor name |
| `NetworkMonitor.Services/Scanning/NetworkScanner.cs` | Ping sweep + ARP parse + DNS resolve |
| `NetworkMonitor.Services/Scanning/DeviceTracker.cs` | Merges scan results into DB |
| `NetworkMonitor.Services/Scanning/ScanWorker.cs` | PeriodicTimer background scan loop |
| `NetworkMonitor/ViewModels/AllDevicesViewModel.cs` | Main device list (last 24h) + scan command |
| `NetworkMonitor/Views/AllDevicesPage.xaml` | Fing-style device grid (All tab, last 24h) |
| `NetworkMonitor/Views/ApprovedDevicesPage.xaml` | Editable approved-device list with Edit / Delete / CSV import-export |
| `NetworkMonitor.Services/Traffic/TrafficCollector.cs` | ETW kernel session; per-flow WAN (`_counters` by PID) + LAN (`_localCounters` by `LocalFlowKey`) capture. TCP recv/send use `daddr/dport`; UDP recv uses `saddr` |
| `NetworkMonitor.Services/Traffic/TrafficTracker.cs` | Flush loop → writes Traffic/LocalTraffic entries + rollups; raises `Flushed(entries, localDeltas)` |
| `NetworkMonitor.Services/Traffic/LiveTrafficFeed.cs` | Always-on singleton feeding the mini graph from `Flushed` / `SpeedTestCompleted` / `ScanCompleted`; two DB reads at startup, none after |
| `NetworkMonitor.Core/Traffic/LanClassifier.cs` | Classifies a remote IP as LAN vs WAN; `IsSelfOrLoopback` self/loopback drop |
| `NetworkMonitor.Core/Traffic/LocalFlowClassifier.cs` | `(protocol, remotePort)` → Data/Discovery + service tag (SMB…); single source of the discovery port list (`DiscoverySqlPredicate`) |
| `NetworkMonitor.Core/Traffic/LocalTrafficGrouper.cs` | Builds the two-level app/device row model + background (discovery) fold |
| `NetworkMonitor.Core/Traffic/LiveRateBuffer.cs` | Fixed ring of one-second buckets behind the mini graph; zero-fills idle gaps, spreads a flush across its interval |
| `NetworkMonitor.Core/Charting/PaletteVariant.cs` | Derives a chart colour for the dark or light card surface from one base hex |
| `NetworkMonitor.Core/Charting/ChartSchemeCatalog.cs` | The five chart colour presets; Classic is the default |
| `NetworkMonitor.Services/Charting/ChartPaletteService.cs` | Resolved palette per role + `PaletteChanged`; the single source of chart colour |
| `NetworkMonitor/ViewModels/InternetViewModel.cs` | WAN per-app grid + area chart + live rate badge |
| `NetworkMonitor/ViewModels/LocalViewModel.cs` | LAN app/device lenses, in-place row reconcile, live rate badge |
| `NetworkMonitor/Views/LocalPage.xaml` | Local traffic grid (lens toggle, service/discovery/rate chips, drill-down) |
| `NetworkMonitor/MiniGraphWindow.xaml` | Always-on-top widget: Internet + Local charts, speed and unknown-device strips, hover-to-opaque; one window in two orientations (panel / horizontal strip) |
| `NetworkMonitor.Core/Widget/HorizontalStripMetrics.cs` | Pure derived width (sum of enabled cells), font scale, height clamp and peak-visibility threshold for the strip |
| `NetworkMonitor.Services/Platform/MiniGraphState.cs` | Shared widget state — visibility, sections, opacity, orientation, per-orientation placement; written by the tray, the toolbar and Settings alike |
| `NetworkMonitor/Notifications/ToastPresenter.cs` | Builds and shows every Windows toast; holds each `ToastNotification` until the platform reports it finished, or its click handler never fires, and marshals the click back to the UI thread |
| `NetworkMonitor.Services/Update/UpdateService.cs` | Update check / download / SHA-256 verify / silent install; 20s check deadline |

## Git Workflow

- **Commit message**: Always suggest a subject line for the user to review and edit — never commit with it unapproved.
- **Subject punctuation**: The subject line must always end with a full stop (`.`). If the user's subject line doesn't end with one, append it before committing.
- **Message format**: Use the user's subject line, then add detailed bullet-point notes describing what changed (files, methods, behaviour), then a `Co-Authored-By` trailer.
- **Show before committing**: Display the full combined message and wait for approval before running `git commit`.
- **Push immediately** after every commit — once the message is approved, commit and push in the same step. Do not ask again before pushing.
- **Every commit ALWAYS pushes to BOTH targets via one remote (`all`)** — the `all` remote fetches from GitHub (public, **source of truth / master**) and has two push URLs: GitHub and Azure DevOps (private mirror). A single `git push all master` reaches both. There is no such thing as a commit that reaches only one target. Always push with `git push all master` — never a bare `git push`.
- **Report both targets in the commit result** — after pushing, the commit summary must explicitly confirm both were updated (e.g. "✅ Pushed to GitHub (master) + DevOps (mirror)"). Never report a commit as done without stating both are in sync.
- `master` tracks `all/master`, so `git status` reports against the ref you actually push to. There is no separate `origin` remote — `all` is the only remote and covers both targets.
- The `all` remote lives in local `.git/config` (not tracked), so it must be set up once per clone.
- **The long-lived branch is `master`. There is no `main`.** Tooling that guesses a default branch
  tends to assume `main` and is wrong here: `all/HEAD` points at `all/master`, releases are cut from
  `master`, and feature branches merge back into it. A feature branch pushes to its own name
  (`git push all <branch>`), which still reaches both GitHub and DevOps because `all` carries both
  push URLs - the "always push to both" rule is about the remote, not about the branch name.

- **Always state the problem before the fix.** The body opens with a section explaining what was wrong and why, then a second section listing what changed. A reader six months out needs the reasoning, not just the diff — the diff is already in git. Include the concrete evidence that identified the cause (measured figures, log lines, the observation that gave it away) and, where it matters, what was ruled out.

Example format — for a defect:
```
<user's subject line>

Problem

- <what was wrong, from the user's point of view>
- <the cause, and the evidence that established it>
- <what was NOT wrong, if theories were ruled out along the way>

Fix

- <what changed and where — file, method, behaviour>
- <what changed and where>

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
```

For work that isn't fixing a defect (a refactor, a doc update, a new feature), keep the same shape but title the first section **Context** — the motivation, what was awkward before — and the second **Change**. The rule is constant: say why before what.

## Notes

- OUI vendor file (`Assets/oui.txt`) is the real IEEE registry. Refresh it periodically by downloading the latest from https://standards-oui.ieee.org/oui/oui.txt and replacing the file (UTF-8, CRLF, no BOM).
- Settings are held in the `Settings` singleton (`Data/Settings.cs`), persisted to `settings.json`; on first run they seed from `appsettings.json` (`Scanner` section). SettingsPage persists each change instantly; scan-related changes take effect on the next scan.
- `ScanWorker` scans **immediately on startup**, then repeats on the configured interval — `RunScanLoopAsync` awaits `RunScanAsync` once before entering its loop. This note previously claimed the opposite ("the PeriodicTimer starts after the first interval — use Scan Network for an immediate scan"); that was wrong, and was caught on 2026-08-20 by the UI test suite, whose seeded History assertions came out 26 against an expected 18 because a startup scan had already run. "Scan Network" on the Devices page still forces a scan at any time.

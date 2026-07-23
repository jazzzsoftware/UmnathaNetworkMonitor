# NetworkMonitor

## Project

WinUI 3 desktop app (.NET 10, unpackaged) that periodically scans the local network, tracks devices by MAC address, and maintains a known-devices list.

Four projects: **NetworkMonitor** (the WinUI app — UI, workers, platform/ETW/EF services), **NetworkMonitor.Models** (net10.0 class library — all model types plus the shared unit formatters), **NetworkMonitor.Core** (net10.0 class library — pure, UI-free service logic: classifiers, grouper, CSV, digest builder/schedule, speed-test maths, mDNS parsing, OuiDatabase), and **NetworkMonitor.Tests** (xunit — references Models + Core via ProjectReference only; no source links). In every project each sub-folder is its own namespace (e.g. `NetworkMonitor.Core.Traffic`). New pure logic that needs tests goes in Core, not the app project.

## Stack

- **UI**: WinUI 3 (Windows App SDK), Blazor NavigationView shell
- **MVVM**: CommunityToolkit.Mvvm (source generators)
- **DataGrid**: CommunityToolkit.WinUI.UI.Controls.DataGrid 7.x
- **ORM**: EF Core 10 + SQLite (no migrations — EnsureCreated)
- **DI / background**: Microsoft.Extensions.Hosting, BackgroundService
- **Scanning**: System.Net.NetworkInformation.Ping + `arp -a` + Dns.GetHostEntryAsync

## Build

Open `NetworkMonitor.slnx` in Visual Studio 2026. Set platform to **x64** (not Any CPU — WinUI 3 does not support Any CPU). Run restore before first build.

New root-level files (docs, config) must be added to `NetworkMonitor.slnx` so they appear in Solution Explorer — non-project files aren't picked up automatically. Use the existing `/AI/` folder for AI-assistant docs (`CLAUDE.md`), `/Project Config/` for other root config/doc files (`.editorconfig`, `CONTRIBUTING.md`), and `/Documents/` for user-facing docs.

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
- `DevicesPage.xaml` is the canonical formatting reference.

## Key Files

| File | Purpose |
|---|---|
| `NetworkMonitor/App.xaml.cs` | IHost build + DI registration + DB init |
| `NetworkMonitor/Data/AppDbContext.cs` | EF context; DbPath points to LocalApplicationData |
| `NetworkMonitor.Core/Data/OuiDatabase.cs` | Loads oui.txt → MAC prefix → vendor name |
| `NetworkMonitor/Services/Scanning/NetworkScanner.cs` | Ping sweep + ARP parse + DNS resolve |
| `NetworkMonitor/Services/Scanning/DeviceTracker.cs` | Merges scan results into DB |
| `NetworkMonitor/Services/Scanning/ScanWorker.cs` | PeriodicTimer background scan loop |
| `NetworkMonitor/ViewModels/AllDevicesViewModel.cs` | Main device list (last 24h) + scan command |
| `NetworkMonitor/Views/AllDevicesPage.xaml` | Fing-style device grid (All tab, last 24h) |
| `NetworkMonitor/Views/ApprovedDevicesPage.xaml` | Editable approved-device list with Edit / Delete / CSV import-export |
| `NetworkMonitor/Services/Traffic/TrafficCollector.cs` | ETW kernel session; per-flow WAN (`_counters` by PID) + LAN (`_localCounters` by `LocalFlowKey`) capture. TCP recv/send use `daddr/dport`; UDP recv uses `saddr` |
| `NetworkMonitor/Services/Traffic/TrafficTracker.cs` | Flush loop → writes Traffic/LocalTraffic entries + rollups; raises `Flushed(entries, localDeltas)` |
| `NetworkMonitor.Core/Traffic/LanClassifier.cs` | Classifies a remote IP as LAN vs WAN; `IsSelfOrLoopback` self/loopback drop |
| `NetworkMonitor.Core/Traffic/LocalFlowClassifier.cs` | `(protocol, remotePort)` → Data/Discovery + service tag (SMB…); single source of the discovery port list (`DiscoverySqlPredicate`) |
| `NetworkMonitor.Core/Traffic/LocalTrafficGrouper.cs` | Builds the two-level app/device row model + background (discovery) fold |
| `NetworkMonitor/ViewModels/InternetViewModel.cs` | WAN per-app grid + area chart + live rate badge |
| `NetworkMonitor/ViewModels/LocalViewModel.cs` | LAN app/device lenses, in-place row reconcile, live rate badge |
| `NetworkMonitor/Views/LocalPage.xaml` | Local traffic grid (lens toggle, service/discovery/rate chips, drill-down) |

## Git Workflow

- **Commit message**: Always suggest a subject line for the user to review and edit — never commit with it unapproved.
- **Subject punctuation**: The subject line must always end with a full stop (`.`). If the user's subject line doesn't end with one, append it before committing.
- **Message format**: Use the user's subject line, then add detailed bullet-point notes describing what changed (files, methods, behaviour), then a `Co-Authored-By` trailer.
- **Show before committing**: Display the full combined message and wait for approval before running `git commit`.
- **Push immediately** after every commit — once the message is approved, commit and push in the same step. Do not ask again before pushing.
- **Every commit ALWAYS pushes to BOTH targets via one remote (`all`)** — the `all` remote fetches from GitHub (public, **source of truth / master**) and has two push URLs: GitHub and Azure DevOps (private mirror). A single `git push all master` reaches both. There is no such thing as a commit that reaches only one target. Always push with `git push all master` — never a bare `git push`.
- **Report both targets in the commit result** — after pushing, the commit summary must explicitly confirm both were updated (e.g. "✅ Pushed to GitHub (master) + DevOps (mirror)"). Never report a commit as done without stating both are in sync.
- `master` tracks `all/master`, so `git status` reports against the ref you actually push to. There is no separate `origin` remote — `all` is the only remote and covers both targets.
- The `all` remote lives in local `.git/config` (not tracked), so it must be set up once per clone; see `CONTRIBUTING.md` § Maintainer notes for the setup commands.

Example format:
```
<user's subject line>

- <what changed and where>
- <what changed and where>

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
```

## Notes

- OUI vendor file (`Assets/oui.txt`) is a placeholder. Download the real file from https://standards-oui.ieee.org/oui/oui.txt and replace it.
- Settings are held in the `Settings` singleton (`Data/Settings.cs`), persisted to `settings.json`; on first run they seed from `appsettings.json` (`Scanner` section). SettingsPage persists each change instantly; scan-related changes take effect on the next scan.
- The `ScanWorker` PeriodicTimer starts after the first interval — use "Scan Network" on the Devices page for an immediate scan.

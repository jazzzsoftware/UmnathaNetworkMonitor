# Chunk 1 — Auto-update

Reviewed 2026-07-27. Fix phase completed 2026-07-27 — see `progress.md`.

15 findings: **1 BUG · 4 RISK · 10 CLEANUP**. No Critical / data-loss issues. The Core layer (version parsing, release parsing, checksums) is clean, well-factored and unit-tested; everything below is at the edges — the exit path, failure reporting, and asset selection.

---

## C1-1 [RISK] `Environment.Exit(0)` skips graceful shutdown — status: fixed

`NetworkMonitor.Services/Update/InstallerLauncher.cs:21`

`LaunchAndExit` starts the installer and immediately calls `Environment.Exit(0)`. That bypasses `MainWindow.OnAppWindowClosing` → `StopHost()`, so `AppHost.StopAsync` never runs. Lost as a result:

- the current flush interval's traffic counters (`TrafficTracker` never flushes — up to `TrafficIntervalSeconds` of WAN **and** LAN bytes dropped on every update),
- the `PRAGMA wal_checkpoint(TRUNCATE)` (`MainWindow.CheckpointDatabase`), so the WAL is left for the new build to recover,
- `_trayIcon.Dispose()` — a ghost tray icon remains until the user hovers over it,
- `SaveWindowPlacement()` — window position/size from this session is not persisted.

The ETW session survives too, but that one self-heals: `TrafficCollector.StopOrphanedSession` (`TrafficCollector.cs:102`) attaches and stops a leftover `NetworkMonitorTraffic` session on next start.

This re-opens the ground of 2026-06-23 finding **C1-2** (host never `StopAsync`'d → ETW leak), which added the `StopHost` path that this new exit route walks around.

**Proposed fix:** have `UpdateViewModel` (or a shutdown callback passed to `IInstallerLauncher`) run the same graceful path `OnExitApp` uses — save placement, `StopHost()`, checkpoint, dispose the tray icon — then start the installer and exit. Simplest shape: `IInstallerLauncher.LaunchAndExit` takes an `Action` invoked before `Process.Start`, wired in the app layer to `MainWindow.Current!.ShutdownForUpdate()`.

---

## C1-2 [BUG] An unparseable version silently means "up to date" — status: fixed

`NetworkMonitor.Core/Update/SemanticVersion.cs:52`, `NetworkMonitor.Core/Update/UpdateDecision.cs:9`

`TryParse` accepts 1–3 components only, so `0.0.9.0` (four parts) fails. `UpdateDecision.IsNewer` returns `false` when *either* side fails to parse, and `UpdateService.CheckAsync:48` maps that to `UpdateCheckResult.UpToDate()` — the user is told **"You're on the latest version."** when in fact the comparison never happened.

The live path is currently safe by luck: `<Version>0.0.9</Version>` makes the SDK emit `AssemblyInformationalVersion` = `0.0.9+<sha>`, and `AppInfo.GetVersion()` strips the `+sha`. But `AppInfo.GetVersion` has a documented fallback to `assembly.GetName().Version.ToString()` (`AppInfo.cs:17-18`), which **always** produces four components — so removing/altering the informational-version attribute, or ever tagging a release `v1.0.0.1`, silently disables updates for good with no error anywhere.

**Proposed fix:** accept ≥4 components and ignore the trailing ones; separately, have `CheckAsync` distinguish "current version unparseable" from "up to date" and report it as a check failure so the condition is visible.

---

## C1-3 [RISK] The first update banner can be dropped — status: fixed

`NetworkMonitor.Services/Update/UpdateCheckWorker.cs:20`, `NetworkMonitor/App.xaml.cs:228-230`, `NetworkMonitor/ViewModels/UpdateViewModel.cs:28`

`UpdateCheckWorker` runs its first check 10 s after `AppHost.StartAsync()`. `UpdateViewModel` — the only `CheckCompleted` subscriber — is constructed lazily as a dependency of `MainWindow`, which is resolved *after* `StartAsync` returns, and `UpdateService` keeps no last result. On a cold start where window construction, `EnsureCreated`, and the OUI load push past 10 s, the result is raised into the void and no banner appears until the next 24-hour check.

**Proposed fix:** cache the last `UpdateCheckResult` on `UpdateService` and replay it to a subscriber on attach (or simply resolve `UpdateViewModel` eagerly before `AppHost.StartAsync()`).

---

## C1-4 [RISK] The download cannot be cancelled — status: fixed

`NetworkMonitor/ViewModels/UpdateViewModel.cs:166`

`UpdateNowAsync` passes `CancellationToken.None`, and while `IsBusy` the banner shows only a progress bar — no Cancel. `Apply` is gated on `IsBusy`, so a stalled download also freezes all further banner updates until the 10-minute `HttpClient` timeout fires. Closing the window doesn't abort it either (the app hides to tray; a real exit tears the process down mid-write).

**Proposed fix:** hold a `CancellationTokenSource` in the view model, add a Cancel button visible while `IsBusy`, and cancel it from the shutdown path.

---

## C1-5 [RISK] Release assets are matched by extension alone — status: fixed

`NetworkMonitor.Core/Update/ReleaseInfoParser.cs:72-96`

The asset loop takes the **last** `.exe` in the array as the installer and the **last** `.sha256` as its checksum, with no check that the two belong together. A release that ever ships a second executable (portable build, a helper tool) or an extra checksum file pairs an installer with a foreign hash — the download then always fails verification, and the user sees "The update could not be downloaded or verified" with no way to diagnose it.

**Proposed fix:** pick the installer first (prefer an expected name pattern, e.g. `Umnatha Network Monitor v*.exe`), then require the checksum asset to be `<installerName>.sha256`.

---

## C1-6 [RISK] The checksum guards corruption, not tampering — status: won't-fix (accepted)

`NetworkMonitor.Services/Update/UpdateService.cs:93-103,116-119`

Installer and `.sha256` come from the same GitHub release, so anything that can alter one can alter the other — the hash only proves the transfer wasn't corrupted. The launched installer's Authenticode signature is never checked, and because the app self-elevates (`App.IsElevated`/`RelaunchElevated`), `Process.Start` with `UseShellExecute = true` hands the downloaded binary an **elevated** process.

Probably an accepted risk for now (the build isn't code-signed), but it should be a recorded decision rather than an omission.

**Decision (user, 2026-07-27): accepted risk, `won't-fix` for now.** The build is not code-signed, so an Authenticode check would reject every update rather than protect one. Revisit when the installer is signed: verify the publisher before `Process.Start`. Until then, the security of the update path rests entirely on the GitHub release being authentic.

---

## C1-7 [CLEANUP] Cancellation is reported as a check failure — status: fixed

`NetworkMonitor.Services/Update/UpdateService.cs:68-71,78`

`OperationCanceledException` produces `Failed("The update check was cancelled.")`, and `CheckCompleted` is raised **outside** the `try`, so it fires on cancellation too. During host shutdown that can flash an error banner (severity `Error`) on the way out.

**Proposed fix:** on `OperationCanceledException` with `cancellationToken.IsCancellationRequested`, return without raising the event.

---

## C1-8 [CLEANUP] Progress is reported per 80 KB chunk — status: fixed

`NetworkMonitor.Services/Update/UpdateService.cs:143-147`

Every read reports a fraction, and `Progress<double>` posts each one to the UI thread — ≈1 300 dispatcher marshals for a 100 MB installer, each raising two `PropertyChanged` events (`DownloadProgress` + `DownloadProgressText`) for a bar that only shows whole percent.

**Proposed fix:** report only when the rounded percentage changes.

---

## C1-9 [CLEANUP] The downloaded installer is never cleaned up after use — status: fixed

`NetworkMonitor.Services/Update/UpdateService.cs:85-87`

`CleanFolder` runs at the *start* of a download, so after a successful update a ~100 MB installer sits in `%LocalAppData%\…\Updates` until the *next* update is downloaded — potentially months, and forever if the user never updates again.

**Proposed fix:** sweep the `Updates` folder at startup (it can only ever contain a spent or abandoned installer at that point).

---

## C1-10 [CLEANUP] No default User-Agent on the update `HttpClient` — status: fixed

`NetworkMonitor/App.xaml.cs:136-139`, `NetworkMonitor.Services/Update/UpdateService.cs:93,123`

`CheckAsync` adds `User-Agent: UmnathaNetworkMonitor` per-request, but the checksum `GetStringAsync` and the installer `GetAsync` go out with none. GitHub's asset CDN tolerates it; the API does not, so the inconsistency is a trap for any future request that goes to `api.github.com`.

**Proposed fix:** set `DefaultRequestHeaders.UserAgent` (and `Accept`) once at the DI registration and drop the per-request headers.

---

## C1-11 [CLEANUP] The update message renders twice on the Settings page — status: fixed

`NetworkMonitor/Views/SettingsPage.xaml:597-600`, `NetworkMonitor/MainWindow.xaml:110-169`

The Settings card binds a second `InfoBar` to the same `UpdateViewModel.IsBannerOpen`/`Severity`/`Message` (OneWay, `IsClosable="False"`). After "Check for updates" the identical message shows twice — once in the window-wide banner at the top and once inline — and the inline copy has no dismiss affordance of its own.

**Proposed fix:** give the Settings card a plain inline status `TextBlock` fed by the same view model, and leave the `InfoBar` as the single window-level surface.

---

## C1-12 [CLEANUP] XAML attribute order — `Command` after value bindings — status: fixed

`NetworkMonitor/MainWindow.xaml:149-150,154-155,162-163`, `NetworkMonitor/Views/SettingsPage.xaml:593-594`

CLAUDE.md orders attributes: simple assignments → event handlers and `Command` bindings → value-assignment bindings. These buttons put `IsEnabled="{x:Bind …}"` / `Visibility="{x:Bind …}"` **before** `Command="{x:Bind …}"`.

**Proposed fix:** move each `Command` above the value bindings.

---

## C1-13 [CLEANUP] The app comes back minimized after a silent update — status: fixed

`Installer/NetworkMonitor.iss` — `[Run]` … `Parameters: "--minimized"; Flags: nowait skipifnotsilent`

A user with the window open clicks **Update now**; the window vanishes and the app relaunches into the tray only. The 0.0.9 release notes (`SettingsPage.xaml:962`) promise it "installs it and restarts itself on the new version", so the visible outcome contradicts the documented one.

**Proposed fix:** relaunch without `--minimized`, or pass a flag that restores the pre-update window visibility (the app was, by definition, foreground when the button was clicked).

---

## C1-14 [CLEANUP] `_reportUpToDate` is a shared field, not a per-call flag — status: fixed

`NetworkMonitor/ViewModels/UpdateViewModel.cs:143,196`

`CheckNowAsync` sets the field, and `Apply` consumes whichever result arrives first. If the 24-hour background check lands between the click and its own result, the background result consumes the flag and the manual check silently shows nothing.

Vanishingly unlikely, but the fix is smaller than the explanation.

**Proposed fix:** have `CheckNowAsync` await `CheckAsync`'s returned `UpdateCheckResult` and apply it directly, instead of routing a manual check through the shared event.

---

## C1-15 [CLEANUP] `UpdateService` is the one untested layer — status: deferred

`NetworkMonitor.Tests/Update/`

Tests cover `SemanticVersion`, `ReleaseInfoParser`, `ChecksumVerifier`, `UpdateDecision` and `UpdateCheckResult` — all pure Core types. Nothing covers `UpdateService`: the checksum-mismatch → delete-and-throw path, the `CleanFolder` sweep, `ContentLength`-missing progress, or the exception → `Failed(...)` mapping.

**Proposed fix:** a fake `HttpMessageHandler` makes all of the above testable without network access. Note that `UpdateService` lives in Services, which the test project doesn't reference — either add the reference or move the download/verify orchestration into Core behind a stream-provider delegate.

**Deferred.** Both routes change the project layering (CLAUDE.md: tests reference Models + Core only; new pure logic belongs in Core), which is more than this review should decide on its own. The Core half was strengthened instead — `SemanticVersion` gained four-component cases and `ReleaseInfoParser` gained three asset-pairing cases covering the C1-5 fix.

---

## Files reviewed

- `NetworkMonitor.Core/Update/SemanticVersion.cs`
- `NetworkMonitor.Core/Update/UpdateDecision.cs`
- `NetworkMonitor.Core/Update/ReleaseInfoParser.cs`
- `NetworkMonitor.Core/Update/ChecksumVerifier.cs`
- `NetworkMonitor.Models/Update/AvailableUpdate.cs`
- `NetworkMonitor.Models/Update/UpdateAvailability.cs`
- `NetworkMonitor.Models/Update/UpdateCheckResult.cs`
- `NetworkMonitor.Services/Update/IUpdateService.cs`
- `NetworkMonitor.Services/Update/IInstallerLauncher.cs`
- `NetworkMonitor.Services/Update/UpdateService.cs`
- `NetworkMonitor.Services/Update/InstallerLauncher.cs`
- `NetworkMonitor.Services/Update/UpdateCheckWorker.cs`
- `NetworkMonitor/ViewModels/UpdateViewModel.cs`
- `NetworkMonitor/MainWindow.xaml` (update banner) + `MainWindow.xaml.cs` (shutdown path)
- `NetworkMonitor/Views/SettingsPage.xaml` (software-updates card) + `SettingsViewModel.cs` (`AutoCheckForUpdates`)
- `NetworkMonitor/App.xaml.cs` (update DI registration)
- `Installer/NetworkMonitor.iss`
- `NetworkMonitor.Tests/Update/*`

## User findings

### U1-1 [CLEANUP] Underscores in test method names — status: fixed

`NetworkMonitor.Tests/LocalTrafficGrouperTests.cs:17,51,83`

Three test methods used the `Scenario_Expectation` naming style — `ByApp_FoldsDiscoveryIntoBackgroundAndKeepsDataUpFront`, `ByDevice_GroupsOnRemoteIpWithFriendlyName`, `ByDevice_AllRowIsLabelledAllDevices` — against the CLAUDE.md rule that no identifier carries an underscore except a private field's leading one. Common xunit convention, but the project's rule is codebase-wide and the test project is not exempt.

**Fixed:** renamed to `ByAppFoldsDiscoveryIntoBackgroundAndKeepsDataUpFront`, `ByDeviceGroupsOnRemoteIpWithFriendlyName`, `ByDeviceAllRowIsLabelledAllDevices`.

A sweep of the whole test project found no others: the remaining underscores are all inside **string literals** (GitHub JSON keys such as `tag_name` / `browser_download_url`, and mDNS device names like `eWeLink_1000beb2e9.local`), which are data rather than identifiers and must stay verbatim.


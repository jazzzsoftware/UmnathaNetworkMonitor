# Auto-Updates — Design

**Date:** 2026-07-26
**Status:** Approved (brainstorming) — pending implementation plan

## Summary

Add an in-app auto-update capability to Umnatha Network Monitor. The app checks GitHub
Releases for a newer version, notifies the user via a non-modal InfoBar, and — on the
user's explicit click — downloads the self-contained Inno Setup installer, verifies its
SHA-256, launches it silently, and exits so the running files can be replaced. The
installer then relaunches the updated app.

The app is unpackaged and self-contained (bundles the .NET 10 and Windows App SDK
runtimes), distributed as a single admin-elevated Inno Setup installer `.exe` published
on GitHub Releases. That installer is the update artifact; the public GitHub Releases API
is the update feed (no authentication required).

## User experience / flow

1. **Check triggers** — the app checks the latest GitHub release:
   - **On startup**, a few seconds after launch, off the UI thread (must not slow startup).
   - **Every 24 hours** while running (the app is designed to run continuously from a
     logon scheduled task).
   - **On demand** via a **Check for updates** button on the Settings/About page.
2. **Update available** — if the latest release version is greater than the current
   version, a non-modal **InfoBar** appears at the top of the app content:
   *"Version 0.0.9 is available"* with **Update now** and **Later**. The same status and
   button are mirrored on the Settings/About page.
3. **Later** dismisses the InfoBar. It reappears on the next check or next launch. Nothing
   is downloaded until the user clicks **Update now**.
4. **Update now** — the app downloads the installer to a temp folder while showing its own
   **progress bar** (percent / MB downloaded), then verifies the downloaded file's
   **SHA-256** against the published checksum.
5. **On successful verify** — the app launches the installer with **`/SILENT`** (one UAC
   prompt; Inno Setup's own progress bar stays visible during file replacement), then
   **exits** so its files unlock. The installer replaces the files and **auto-relaunches**
   the app.
6. **Failures never fail silently** — every failure surfaces an error to the user; nothing
   is swallowed. Failures are shown in the same non-modal InfoBar (dismissible, not a
   popup) for automatic checks, and inline on Settings/About for manual checks:
   - No network / API unreachable → *"Couldn't check for updates — check your connection."*
   - Download failure or **SHA-256 mismatch** → abort, delete the downloaded file, and
     **never launch the installer**; show *"Update download failed"* / *"Update could not
     be verified"*.
   - Manual "Check for updates" additionally reports the success case: *"You're up to
     date."*
   - All failures are also written to the app log.

## Architecture

Respects the project layering **Models ← Core ← Services ← App**. Each sub-folder is its
own namespace. New pure, testable logic goes in Core, not Services.

### `NetworkMonitor.Models/Update/`

DTOs only:
- `AvailableUpdate` — version, installer download URL, checksum download URL, size in bytes.
- `UpdateCheckResult` — outcome of a check (up-to-date / update-available / failed) plus
  any error detail and the `AvailableUpdate` when present.

### `NetworkMonitor.Core/Update/` (pure, unit-tested)

- `SemanticVersion` — parse and compare version strings; tolerate a leading `v`
  (`v0.0.9`), handle pre-release ordering, and compare against the current app version.
- `ReleaseInfoParser` — parse the GitHub "latest release" JSON into an `AvailableUpdate`
  (locate the `.exe` asset and the `.sha256` asset by name/suffix).
- `ChecksumVerifier` — compute/compare SHA-256; case-insensitive hex comparison.
- `UpdateDecision` — given current version + parsed release, decide whether an update is
  offered.

### `NetworkMonitor.Services/Update/`

- `IUpdateService` / `UpdateService` — orchestrates a check: calls the Releases API via
  `HttpClient`, parses via Core, and (on user action) downloads the checksum then the
  installer with `IProgress<>` reporting, verifies via Core, launches the installer, and
  exits. Network and file work only — all decisions delegate to Core.
- `UpdateCheckWorker : BackgroundService` — the 24-hour periodic check timer; also runs
  the initial post-startup check. Honors the `AutoCheckForUpdates` setting.
- `IInstallerLauncher` / `InstallerLauncher` — thin wrapper over process launch + app exit
  so the launch/exit behaviour can be faked in tests.

### App (`NetworkMonitor`)

- `UpdateViewModel` — exposes update state (checking / available / downloading / error),
  the download progress value, and the `Update now` / `Later` / `Check for updates`
  commands. DI-registered like the other view models/services.
- The shell hosts the InfoBar bound to `UpdateViewModel`; Settings/About binds the mirror
  status, the manual **Check for updates** button, and the `AutoCheckForUpdates` toggle.

## Update feed & checksum delivery

- **Feed:** `GET https://api.github.com/repos/jazzzsoftware/UmnathaNetworkMonitor/releases/latest`
  (public, unauthenticated). Read `tag_name` (e.g. `v0.0.9`) and the `.exe` asset's
  `browser_download_url`. GitHub's 60-requests/hour unauthenticated limit is a non-issue
  at this cadence.
- **Checksum:** `build-installer.ps1` computes the installer's SHA-256 and writes a
  companion **`<installer>.exe.sha256`** file next to the installer. The GitHub release
  publishes **both** assets. At update time the app downloads the tiny `.sha256` first,
  then the installer, and compares before launching.

## Build & installer (`.iss`) changes

- **`build-installer.ps1`** — after ISCC produces the installer, compute
  `Get-FileHash -Algorithm SHA256` and write `<installer>.exe.sha256` (hash only, or
  `hash *filename` format — the app parses the leading hex token).
- **`NetworkMonitor.iss` silent-relaunch fix** — the current `[Run]` relaunch entry uses
  the `skipifsilent` flag, so a `/SILENT` install would **not** auto-relaunch the app.
  Adjust so the app restarts after a silent update:
  - Add `CloseApplications=yes` and `RestartApplications=yes` to `[Setup]` as a safety net
    for closing/replacing the running app.
  - Provide a relaunch `[Run]` entry that fires under silent mode (i.e. not skipped when
    silent), so step 5 restarts the app.
  - `DefaultDirName` is unchanged, so the existing logon scheduled task (which points at
    `{app}\NetworkMonitor.exe`) continues to work after the update.
- **Release process doc** — document that a release must upload both the installer `.exe`
  and its `.sha256` companion.

## Settings & edge cases

- New setting **`AutoCheckForUpdates`** (bool, default **on**) in the `Settings` singleton,
  persisted to `settings.json` like the other settings. Turning it off disables the
  automatic startup and 24-hour checks; the manual **Check for updates** button always
  works regardless.
- **Single instance** — the exit-then-relaunch handoff assumes a single running instance.
- **Elevation** — the app may run non-elevated (normal launch) or elevated (logon task).
  Launching the installer via `ShellExecute` lets its `PrivilegesRequired=admin` manifest
  raise the UAC prompt; the app does not need to be elevated itself to start the update.
- **Download location** — `%LOCALAPPDATA%\Umnatha Network Monitor\Updates\`; stale
  downloads are cleaned up (e.g. on the next successful check or launch).

## Testing

- **Core (real unit tests):**
  - `SemanticVersion` — equal, older, newer, `v`-prefix, differing component counts,
    pre-release ordering, malformed input.
  - `ReleaseInfoParser` — well-formed release JSON, missing `.exe` asset, missing `.sha256`
    asset, empty/no releases.
  - `ChecksumVerifier` — matching hash, mismatched hash, case-insensitivity, malformed
    checksum file content.
  - `UpdateDecision` — offered vs not offered across version relationships.
- **Service:** exercised with a fake `HttpMessageHandler` (canned API + download responses)
  and a fake `IInstallerLauncher`; no real network calls or installer launches in tests.
  Covers happy path, network failure, and checksum-mismatch abort (installer never
  launched, file deleted).

## Out of scope

- Code-signing / Authenticode verification (SHA-256 checksum is the chosen integrity
  mechanism).
- Delta/differential updates — the full self-contained installer is downloaded each time.
- Rollback beyond what Inno Setup provides.
- Auto-install without user action — updates are always user-initiated via **Update now**.

# Contributing

Thanks for your interest in improving Network Monitor.

## Building

- Open `NetworkMonitor.slnx` in Visual Studio 2026 (or later).
- Set the solution platform to **x64** — WinUI 3 does not support "Any CPU".
- Restore NuGet packages before the first build.
- Requires .NET 10 and the Windows App SDK workload.

## Running tests

The `NetworkMonitor.Tests` project covers device tracking, CSV import/export, digest scheduling, traffic classification and grouping, the update checker, and other non-UI logic:

```
dotnet test NetworkMonitor.Tests
```

It references **`NetworkMonitor.Models` and `NetworkMonitor.Core` only**, by `ProjectReference`, with no source links. That is deliberate: anything in `NetworkMonitor.Services` or the app project cannot be unit tested, so **new pure logic that needs tests belongs in Core**, not Services. The layering is Models ← Core ← Services ← App.

## Database changes

The app is publicly released, and a user's `networkmonitor.db` holds the only copy of their device and traffic history. **Every schema change ships an EF Core migration in the same commit as the change** — a new `DbSet`, a new property on an existing entity, a changed key or index all count. "Delete the database and let it rebuild" is not an acceptable upgrade path. Prefer additive migrations (a nullable column with a sensible default) over a destructive rewrite. Changes to `settings.json` preferences, UI or pure runtime behaviour are not schema changes and need no migration.

## Coding conventions

Full conventions, including rationale, live in [`CLAUDE.md`](CLAUDE.md) — it was written for AI-assisted development but applies equally to hand-written contributions. The `.editorconfig` in this repo enforces the mechanical parts automatically (no `var`, brace style, private field naming), so your IDE will flag most of it as you type. Rules that can't be enforced by tooling and are easy to miss:

- **Single exit point** — every method has exactly one `return`, at the end.
- **Blank lines around blocks** — every `if`, `for`, `foreach`, `while`, `switch`, `try`/`catch`/`finally`, and `using` block has a blank line above and below it, including right after an opening `{` and right before a closing `}`.
- **Returns stand alone** — assign a computed value to a local variable first; don't compute inline in a `return` statement.
- **No single-character variable names** — including lambda parameters and pattern-match variables.
- **Class member order** — fields → constructor → properties → public methods → overrides → private methods.

## Releasing (maintainers)

The app checks GitHub Releases for updates, so a release must be published in the shape the in-app updater expects.

1. Bump `<Version>` in `NetworkMonitor/NetworkMonitor.csproj` — it is the single source of truth for the About box and the installer name.
2. Build the installer:

   ```
   Tools\Installer\build-installer.ps1 -Version X.Y.Z
   ```

   This produces two files in `Tools\Installer\Output`:

   - `Umnatha Network Monitor vX.Y.Z.exe` — the installer
   - `Umnatha Network Monitor vX.Y.Z.exe.sha256` — its SHA-256 checksum

3. Create the GitHub release with tag `vX.Y.Z` and **upload both files as assets**. The updater reads `tag_name` from the latest release, then looks for one asset ending in `.exe` and one ending in `.sha256`; if either is missing, the release is ignored and the update check reports a failure.
4. The updater downloads the installer, verifies it against the checksum, and only then runs it silently (`/SILENT /SUPPRESSMSGBOXES /NORESTART`). A silent install closes the running app, replaces the files, and relaunches it minimised.

## Submitting changes

- Open an issue first for anything beyond a small fix, so we can agree on the approach before you invest time.
- Keep pull requests focused — one change per PR is easier to review than a bundle of unrelated fixes.
- Make sure `dotnet test NetworkMonitor.Tests` passes before opening a PR.

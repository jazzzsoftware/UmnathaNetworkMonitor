# Contributing

Thanks for your interest in improving Network Monitor.

## Building

- Open `NetworkMonitor.slnx` in Visual Studio 2026 (or later).
- Set the solution platform to **x64** — WinUI 3 does not support "Any CPU".
- Restore NuGet packages before the first build.
- Requires .NET 10 and the Windows App SDK workload.

## Running tests

The `NetworkMonitor.Tests` project covers device tracking, CSV import/export, digest scheduling, and other non-UI logic:

```
dotnet test NetworkMonitor.Tests
```

## Coding conventions

Full conventions, including rationale, live in [`CLAUDE.md`](CLAUDE.md) — it was written for AI-assisted development but applies equally to hand-written contributions. The `.editorconfig` in this repo enforces the mechanical parts automatically (no `var`, brace style, private field naming), so your IDE will flag most of it as you type. Rules that can't be enforced by tooling and are easy to miss:

- **Single exit point** — every method has exactly one `return`, at the end.
- **Blank lines around blocks** — every `if`, `for`, `foreach`, `while`, `switch`, `try`/`catch`/`finally`, and `using` block has a blank line above and below it, including right after an opening `{` and right before a closing `}`.
- **Returns stand alone** — assign a computed value to a local variable first; don't compute inline in a `return` statement.
- **No single-character variable names** — including lambda parameters and pattern-match variables.
- **Class member order** — fields → constructor → properties → public methods → overrides → private methods.

## Submitting changes

- Open an issue first for anything beyond a small fix, so we can agree on the approach before you invest time.
- Keep pull requests focused — one change per PR is easier to review than a bundle of unrelated fixes.
- Make sure `dotnet test NetworkMonitor.Tests` passes before opening a PR.

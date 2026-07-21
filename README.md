# Umnatha Network Monitor

A Windows desktop app that watches your local network: it scans continuously, tracks every device it finds, measures per-application bandwidth straight from the kernel, and emails you nothing because it never leaves your machine. Everything — devices, traffic, speed-test history, daily digests — is stored locally in SQLite.

## What it does

- **Scans the network** on a configurable interval (default: every 5 minutes) using ICMP ping, ARP table parsing, and reverse DNS lookup.
- **Tracks devices** by MAC address across IP changes and DHCP renewals, and classifies them by type (router, PC, mobile, camera, etc.) with vendor lookup from the IEEE OUI database.
- **Names devices via mDNS** — a per-scan DNS-SD (Bonjour) discovery pass fills in a friendly name and hardware model for devices that vendor and reverse-DNS lookups can't identify, chiefly randomized-MAC gear; discovered values never overwrite a name you've set.
- **Measures per-application traffic** — captures upload/download bytes per process directly from the Windows kernel (ETW) and charts it live, split into **Internet** (WAN) and **Local** (LAN) views. The Local view pivots **by app or by device**, folds away device-discovery chatter, tags SMB/file-share flows, and shows a live throughput badge (Mb/s · MB/s) on whatever's actively transferring.
- **Measures internet speed** — an hourly download/upload/latency/jitter test against Cloudflare, no account needed.
- **Generates a daily digest** summarising device activity and traffic, exportable to PDF or CSV.
- **Alerts via Windows toast + in-app banner** when a device appears or disappears, optionally limited to unknown devices only.
- **Backs itself up** — a timestamped database snapshot every 24 hours, pruned automatically.
- **Lives in the system tray**, with an optional start-with-Windows setting.

See [`Documents/Overview.md`](Documents/Overview.md) for the full feature tour, page-by-page, plus the complete settings reference. [`Documents/Architecture.md`](Documents/Architecture.md) covers the internals.

## Requirements

- Windows 10 (build 17763) or later, x64.
- Runs **as administrator** — capturing per-process traffic requires a kernel-level ETW session. If launched without admin rights, it relaunches itself elevated automatically.

## Building from source

- Open `NetworkMonitor.slnx` in Visual Studio 2026 (or later).
- Set the solution platform to **x64** — WinUI 3 does not support "Any CPU".
- Restore NuGet packages, then build. Requires .NET 10 and the Windows App SDK workload.

See [`CONTRIBUTING.md`](CONTRIBUTING.md) for coding conventions and how to run the test suite.

## Data & privacy

Everything lives locally in `%LOCALAPPDATA%\UmnathaNetworkMonitor\` — there's no cloud sync, telemetry, or account. Optional diagnostic logging (off by default) never records MAC addresses, IP addresses, or hostnames. See the *Data storage* and *Diagnostic logging* sections of [`Documents/Overview.md`](Documents/Overview.md) for exact file layouts.

## License

[MIT](LICENSE).

## Support

This project is provided as-is, maintained on a best-effort basis. Bug reports and feature ideas are welcome via Issues, but there's no guaranteed response time or roadmap commitment.

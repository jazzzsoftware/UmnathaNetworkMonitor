# Umnatha Network Monitor

This project started as a way to learn Agentic Engineering using Claude Code. It started with a need to monitor the activity of my many Home Assistant IoT devices and automations. From there it grew in scope to become a general network monitor.

The name Umnatha comes from isiXhosa, one of South Africa's indigenous languages, where it means 'Net'. (Pronounced oom-NAH-tah.)

This app is a Windows desktop app that watches your local network: it scans continuously, tracks every device it finds, and measures per-application bandwidth straight from the kernel. No data ever leaves your machine. Everything — devices, traffic, speed-test history, daily digests — is stored locally in SQLite.

<p align="center">
  <img src="Documents/Images/TeaserInternet.png" alt="Live per-application internet traffic" width="820">
  <br>
  <em>Live per-application internet bandwidth</em>
</p>

<p align="center">
  <img src="Documents/Images/TeaserLocal.png" alt="Local network traffic" width="820">
  <br>
  <em>Local network traffic, pivoted by app or by device</em>
</p>

<p align="center">
  <img src="Documents/Images/TeaserSpeedtest.png" alt="Hourly internet speed test" width="820">
  <br>
  <em>Hourly download / upload / latency / jitter speed tests</em>
</p>

<p align="center">
  <img src="Documents/Images/MiniGraph.png" alt="Floating mini graph on the desktop" width="640">
  <br>
  <em>The floating mini graph — live throughput, last speed test and unknown devices, always on top</em>
</p>

## What it does

- **Scans the network** on a configurable interval (default: every 5 minutes) using ICMP ping, ARP table parsing, and reverse DNS lookup.
- **Tracks devices** by MAC address across IP changes and DHCP renewals, and classifies them by type (router, PC, mobile, camera, etc.) with vendor lookup from the IEEE OUI database.
- **Names devices via mDNS** — a per-scan DNS-SD (Bonjour) discovery pass fills in a friendly name and hardware model for devices that vendor and reverse-DNS lookups can't identify, chiefly randomized-MAC gear; discovered values never overwrite a name you've set.
- **Maintains a known-devices list** — approve any device to give it a friendly name, type and notes; approved devices stop showing as unknown, and the whole list imports and exports as CSV.
- **Measures per-application traffic** — captures upload/download bytes per process directly from the Windows kernel (ETW) and charts it live, split into **Internet** (WAN) and **Local** (LAN) views. The Local view pivots **by app or by device**, folds away device-discovery chatter, tags SMB/file-share flows, and shows a live throughput badge (Mb/s · MB/s) on whatever's actively transferring.
- **Floats a mini graph on the desktop** — an optional always-on-top widget showing live Internet and Local throughput, the last speed test and any unknown devices, without keeping the main window open. It sits at whatever opacity you choose and rises to full when you hover it; double-click any section to jump straight to that page. It can also be laid out as a short, wide horizontal strip that is short enough to sit over the taskbar if you drag it there, with a width that follows whichever sections you enable and a height you set by dragging its edge.
- **Measures internet speed** — an hourly download/upload/latency/jitter test against Cloudflare, no account needed.
- **Generates a daily digest** summarising device activity and traffic, exportable to PDF or CSV.
- **Alerts via Windows toast + in-app banner** when a device appears or disappears, optionally limited to unknown devices only.
- **Keeps a history** of every appearance and disappearance, browsable per device, with automatic purging of old events.
- **Backs itself up** — a timestamped database snapshot every 24 hours, pruned automatically.
- **Lives in the system tray**, with an optional start-with-Windows setting.

See [`Documents/Overview.md`](Documents/Overview.md) for the full feature tour, page-by-page, plus the complete settings reference. [`Documents/Architecture.md`](Documents/Architecture.md) covers the internals.

## Screenshots

- [Devices](Documents/Images/Devices.png) — every device on the network, tracked by MAC with type, vendor and live status.
- [Internet traffic](Documents/Images/Internet.png) — per-application WAN bandwidth with a live download/upload chart.
- [Local traffic](Documents/Images/Local.png) — LAN traffic pivoted by app or device, with discovery chatter folded away.
- [Floating mini graph](Documents/Images/MiniGraph.png) — the always-on-top widget: live Internet and Local throughput, last speed test, unknown devices.
- [Speed test](Documents/Images/Speedtest.png) — hourly Cloudflare download/upload/latency/jitter history.
- [Daily digest](Documents/Images/DigestReport.png) — a summary report of device activity and traffic, exportable to PDF.

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

## Roadmap

Ideas being explored, in no particular order and with no committed timeline:

- ~~**Automatic updates** — in-app update checks and one-click upgrades.~~ Shipped in v0.0.9.
- **Chart colour schemes** — selectable palettes for the traffic and speed charts.
- ~~**Floating mini-graph** — a small always-on-top live throughput window.~~ Shipped in v0.0.10.
- ~~**Horizontal mini-graph strip** — a short, wide layout for the mini graph that will sit over the taskbar.~~ Shipped in v0.0.11.

## License

[MIT](LICENSE).

## Support

This project is provided as-is, maintained on a best-effort basis. Bug reports and feature ideas are welcome via Issues, but there's no guaranteed response time or roadmap commitment.

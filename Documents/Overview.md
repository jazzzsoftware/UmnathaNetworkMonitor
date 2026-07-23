# Network Monitor — Overview

Network Monitor (Umnatha Network Monitor) is a Windows desktop application that continuously scans your local network, tracks every device it finds, measures per-application network traffic, and produces a daily digest report. It alerts you when unknown devices appear or known devices go offline.

## What it does

- **Scans the network** on a configurable interval (default: every 5 minutes) using ICMP ping, ARP table parsing, and reverse DNS lookup.
- **Tracks devices** by MAC address across IP changes and DHCP renewals.
- **Classifies devices** by type (Router, PC, Mobile, Camera, etc.) with an emoji icon.
- **Identifies vendors** from the IEEE OUI database using the first three octets of each MAC address.
- **Enriches names via mDNS** — a per-scan mDNS/DNS-SD (Bonjour) discovery pass fills a friendly name and hardware model for devices that OUI vendor and reverse-DNS can't identify (chiefly randomized-MAC devices), stored in dedicated fields that never overwrite a name you've set.
- **Measures per-application traffic** — captures upload/download bytes per process directly from the Windows kernel and charts it live, in separate **Internet** (WAN) and **Local** (LAN) views, with a live throughput badge on whatever's actively transferring.
- **Measures internet speed** — an hourly (or on-demand) download/upload/latency/jitter test against Cloudflare (no account needed), using parallel connections so the numbers line up with speedtest.net; charts the history.
- **Generates a daily digest** — a once-a-day report summarising device activity and traffic, viewable in-app and exportable to PDF or CSV.
- **Alerts via toast notifications** (Windows notifications plus an in-app banner) when a device appears or disappears; can be limited to unknown devices only.
- **Maintains history** of every appearance and disappearance event, with automatic purging of old records.
- **Backs itself up** — a timestamped database snapshot and approved-device CSV every 24 hours, pruned after a few days.
- **Optional diagnostic logging** — a privacy-safe daily log of app events and errors you can share for troubleshooting.
- **Lives in the system tray** and can start automatically with Windows, minimised out of the way.

## Requirements

The app runs **as administrator** — capturing per-process traffic uses a kernel-level (ETW) network session that requires elevation. If launched without admin rights, it relaunches itself elevated automatically.

## Pages

| Page | Purpose |
|---|---|
| **Traffic** | Live per-application network usage with a stacked area chart and a sortable grid of download/upload bytes, split into **Internet** (WAN) and **Local** (LAN, with By-app/By-device lenses) tabs, plus a live throughput badge. The default page on launch. |
| **Devices** | A host page with four tabs: **Devices** (everything seen in the last 24 hours; amber rows are unknown), **Approved** (devices you've identified — set a friendly name, type and notes), **Unapproved** (unknown devices awaiting approval), and **History** (per-device appeared/disappeared log). |
| **Reports** | The daily digest viewer: see the latest report, browse past reports, generate one on demand, and export to PDF or CSV. |
| **Speed Test** | Internet speed history — run a test on demand or hourly; download/upload throughput (Mb/s & MB/s), latency and jitter shown as charts and a sortable grid, exportable to CSV. |
| **Settings** | Scan parameters, traffic, speed-test and digest options, notification preferences, history retention, start-with-Windows, a manual purge button, a Data Folder section, and Release Notes. |

## Traffic monitoring

The Traffic page shows which applications are using the network right now, with a live stacked area chart and a grid of per-process download/upload totals over a selectable time window. Data is captured from the kernel, so it reflects real per-process usage rather than per-adapter totals. It has two tabs:

- **Internet** — traffic to the wider internet (WAN), per application.
- **Local** — traffic on your own LAN (this PC to other devices on your network). Toggle **By app** (which apps are talking to the LAN) or **By device** (which devices your PC is talking to, and the apps behind each). The *By device* lens makes a large transfer obvious — for example a NAS backup climbs to the top as a big upload, tagged **SMB**, listed under **System** (Windows performs file-share copies in the kernel, so they're always credited to System rather than the app that started them). Background chatter — the constant device-discovery pings from browsers and antivirus — is folded into a single collapsible **"discovery only"** row so it doesn't drown out real transfers.

**Live rate badge.** While data is actively moving, the busy row shows a green pill with the current throughput in **both** units, e.g. `● 118 Mb/s · 15 MB/s`. It's smoothed over a few seconds, appears only in live mode and only above 0.5 Mb/s, so idle rows stay clean. Both the Internet and Local tabs show it. All sizes and speeds in the app use decimal (SI) units — KB/MB/GB with ÷1000 steps — the same convention as your ISP and speedtest.net.

Pick a time range (last 5 minutes, hour, 6 hours, 24 hours or 7 days) with the range buttons. The chart runs in **Live** mode and scrolls in real time; clicking a point or a grid row switches to **History** mode so you can inspect a past moment — click the mode badge to return to Live. The axis scale rounds to a clean value, and smooth scrolling can be toggled in Settings.

## Internet speed test

The Speed Test page measures your internet connection against Cloudflare's free service (no account or API key needed). To measure accurately on fast connections it opens **several parallel connections** and records the sustained speed over a few seconds — the same approach speedtest.net and Cloudflare's own web test use — so the numbers match them instead of reading low. A test runs automatically every hour and can be run on demand. Each result records download and upload throughput (shown in both Mb/s and MB/s), latency and jitter, with the nearest Cloudflare data centre as the server. History is shown as stacked throughput and latency charts plus a sortable grid, and can be exported to CSV. **Note:** an accurate test transfers roughly 750 MB, so the hourly schedule uses about 18 GB/day — turn it off in Settings if you're on a metered connection.

## Daily digest

Once a day (at a configurable hour) the app generates a digest covering the previous 24 hours: devices seen, new/unknown devices, connect/disconnect activity, and the top applications by traffic. If the app was off when a digest was due, missed days are caught up on the next start.

> **First run on a new/empty database:** the startup catch-up only creates a report if there's already data for the period, so a brand-new database (e.g. after a delete) produces **no digest on the first launch** — one appears on the next launch once data has been collected. Use **Generate now** to produce one immediately.

You can:

- View the latest and historical reports on the **Reports** page.
- **Generate now** for an immediate report of the last 24 hours.
- **Export to PDF** — a formatted document with charts and tables.
- **Export to CSV** — the selected report or all reports.
- Get a Windows toast when a new digest is ready (toggleable).

## Marking devices as known

On the Devices/Unapproved tabs, approve any unknown device. A dialog lets you assign a friendly name, device type, and optional notes. Once approved, the device moves to the Approved list and its row is no longer highlighted. Device lists can also be imported from and exported to CSV.

## Device naming & mDNS enrichment

A device's display name is chosen in priority order: the **friendly name** you've set, then an **mDNS name**, then its reverse-DNS **hostname**, then its **IP address**. To fill that middle rung, every scan runs a short mDNS/DNS-SD (Bonjour) discovery pass — overlapped with the ping sweep, so it adds no noticeable time — and records a discovered name and hardware **Model** for devices that respond. The Model shows as a column on the Devices grid.

This mainly helps **randomized-MAC devices** (many phones, and Apple/Google/IoT gear), where the OUI vendor lookup is meaningless and reverse-DNS usually fails, so the device would otherwise appear as a bare IP. Discovered names and models are stored in their own fields and refresh on each scan, but they **never overwrite a friendly name you've assigned**. mDNS is best-effort: devices that don't advertise over it (for example stock Android, or a locked iPhone) simply keep whatever name they already had.

## Notifications

Alerts appear both as Windows toast notifications and as an in-app banner. You can:
- Disable toasts entirely.
- Limit them to unknown devices only (useful once your network is fully mapped).

## System tray & startup

Closing the window doesn't quit the app — it hides to the system tray and keeps scanning. Double-click the tray icon (or use its right-click menu) to bring the window back, or choose **Exit** to quit fully.

Enable **Start with Windows** in Settings to launch the app automatically at logon. When started this way it opens **minimised in the system tray** — no window or taskbar button appears, just the tray icon. Launching it yourself (double-click) always opens the window normally.

## Settings

| Setting | Description |
|---|---|
| Subnet Base | First three octets of your network (e.g. `192.168.1`). |
| Start / End Host | Host range to scan (default: 1–254). |
| Scan Interval | How often a full scan runs, in minutes. |
| Ping Timeout | How long to wait for each ping reply, in milliseconds. |
| Max Parallel Pings | Concurrency limit for the ping sweep. |
| Purge history older than | Device events older than this many days are deleted automatically. Set to 0 to disable. |
| Traffic time range | Default time window shown on the Traffic chart. |
| Traffic sample interval | How often traffic counters are flushed, in seconds. |
| Purge traffic older than | Traffic samples, per-minute rollups and speed-test results older than this many days are deleted automatically. |
| Smooth chart scrolling | Toggle animated scrolling on the Traffic chart. |
| Run periodic speed tests | Run an hourly Cloudflare download/upload/latency speed test (Speed Test page). |
| Digest generation hour | Hour of day (0–23) the daily digest is generated. |
| Purge reports older than | Digest reports older than this many days are deleted automatically. |
| Notify on new digest | Show a Windows toast when a daily digest is ready. |
| Show Toast Notifications | Master switch for device toast alerts. |
| Unknown devices only | When enabled, toasts fire only for unrecognised devices. |
| Start with Windows | Launch automatically (minimised to tray) at logon. |
| Enable diagnostic logging | Write a daily diagnostic log (app events + errors, no device/network identifiers) to the Logs folder. Off by default. |

## Data storage

All data is stored locally in `%LOCALAPPDATA%\UmnathaNetworkMonitor\`:

```
networkmonitor.db   SQLite database (devices, events, traffic, daily digests, speed tests)
settings.json       User settings and window placement
sort-*.json         Saved column sort order for each list page
Logs\               Daily diagnostic log files (when logging is enabled)
Backups\            Automatic daily database snapshots + approved-device CSV exports
```

The in-app **Settings → Data Folder** section lists these files and has a link to open the folder.

The database is checkpointed on clean exit (tray → Exit), making the `.db` file safe to copy as a backup without needing the companion WAL files.

## Automatic backups

Every 24 hours the app saves a timestamped copy of the database, plus a CSV export of your approved devices, into the `Backups` folder:

```
%LOCALAPPDATA%\UmnathaNetworkMonitor\Backups\networkmonitor_2026-06-23_06-00-00.db
%LOCALAPPDATA%\UmnathaNetworkMonitor\Backups\approved-devices_2026-06-23_06-00-00.csv
```

These run in the background; backups older than **3 days** are pruned automatically after each new backup, and a `.db`/`.csv` pair is written together (a failed CSV export removes its `.db` so pairs stay in sync). There's no in-app restore — to recover, copy a backup `.db` over `networkmonitor.db` while the app is closed.

## Diagnostic logging

Logging is **off by default**. Enable it in **Settings → Other** ("Enable diagnostic logging", with an "Open logs folder" link) to write a daily `Log-yyyymmdd.txt` file to the `Logs` folder. It records app start/stop (with version), scan start/completed (counts only), periodic speed-test results (throughput, ping and jitter), and any unexpected errors. **No device or network identifiers (MAC, IP, hostname) are ever written**, so the logs are safe to share for troubleshooting. Log files older than 7 days are pruned on startup.

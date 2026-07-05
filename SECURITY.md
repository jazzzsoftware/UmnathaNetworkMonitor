# Security Policy

## Why this app needs administrator rights

Network Monitor runs elevated because capturing per-application network traffic requires opening a kernel-level ETW (Event Tracing for Windows) session — there's no way to attribute bytes to a specific process from user mode. If launched without admin rights, the app relaunches itself elevated automatically.

Elevation is used for exactly that ETW session, plus the network scan itself (ICMP ping and `arp -a`). It is not used to install drivers, modify system settings, or persist anything outside its own data folder.

## What the app does with that access

- All data — devices, traffic, speed tests, digests — is stored locally in `%LOCALAPPDATA%\UmnathaNetworkMonitor\`. There is no cloud sync, telemetry, or external server beyond the local network scan and the Cloudflare speed test.
- Optional diagnostic logging (off by default) never records MAC addresses, IP addresses, or hostnames.
- The source is fully available in this repository — the scanning and traffic-capture code lives under `NetworkMonitor/Services/Scanning/` and `NetworkMonitor/Services/Traffic/` if you want to verify this yourself before running it elevated.

## Reporting a vulnerability

If you find a security issue, please email **jazzzsoftware@outlook.com** rather than opening a public issue. Include steps to reproduce and the affected version. This is a best-effort, single-maintainer project — there's no guaranteed response time, but reports will be taken seriously and a fix released as soon as practical.

## Supported versions

Only the latest release is supported. Please update before reporting an issue to confirm it isn't already fixed.

# SpoofGUI

[![Release](https://img.shields.io/github/v/release/ZethRise/SpoofGUI?logo=github&color=blue)](https://github.com/ZethRise/SpoofGUI/releases)
[![Downloads](https://img.shields.io/github/downloads/ZethRise/SpoofGUI/total?logo=github&color=brightgreen)](https://github.com/ZethRise/SpoofGUI/releases)
[![Build](https://img.shields.io/github/actions/workflow/status/ZethRise/SpoofGUI/release.yml?logo=github&label=release)](https://github.com/ZethRise/SpoofGUI/actions)
[![License](https://img.shields.io/badge/license-GPL--3.0-lightgrey?logo=gnu)](LICENSE)
[![Stars](https://img.shields.io/github/stars/ZethRise/SpoofGUI?logo=github)](https://github.com/ZethRise/SpoofGUI/stargazers)

SpoofGUI is a native Windows GUI fork for [patterniha/SNI-Spoofing](https://github.com/patterniha/SNI-Spoofing), maintained at [ZethRise/SpoofGUI](https://github.com/ZethRise/SpoofGUI).

It is not a VPN. SpoofGUI runs the local SNI-Spoofing listener as administrator and bundles Xray (Proxy / System Proxy) and sing-box (Tunnel) for outbound. Point your client at:

```text
127.0.0.1:40443    (SNI-Spoofing listener)
127.0.0.1:20882    (Xray SOCKS5, when V2Ray is connected)
127.0.0.1:20883    (Xray HTTP,    when V2Ray is connected)
```

SOCKS / HTTP ports default to 20882 / 20883 and are configurable on the Settings page.

<img width="1264" height="802" alt="image" src="https://github.com/user-attachments/assets/4266a7de-bc91-484a-9efc-5b2e6ca03e78" /> 



## Current Status

- App version: `1.0.3`
- Frontend: C# / .NET 10 / WinUI 3
- Backends:
  - **Python SNI-Spoofing engine** (`engine\SpoofGUI.SniSpoofEngine.exe`) — bundled PyInstaller build of the vendored upstream tool; performs the WinDivert + fake ClientHello injection. Resilient bidirectional relay (a dropped connection never kills the engine) + optional Fast mode (low-latency sockets), adapted from [atarevals/SNI-Spoofing](https://github.com/atarevals/SNI-Spoofing)
  - **C# Xray manager** — generates config and starts `xray.exe` for Proxy / System Proxy modes
  - **C# sing-box manager** — runs `sing-box.exe` as a full core (tun inbound + proxy outbound) for Tunnel mode
- Core binaries (`xray.exe`, `sing-box.exe`, `wintun.dll`) are not committed; the release build fetches the architecture-correct ones automatically
- Runtime package: self-contained Windows build, no Python / Xray / sing-box / .NET install needed on target PCs
- Builds for **amd64** and **x86**; each ships a Setup installer and a clean Portable `.zip` (a launcher at the root + an `app\` folder). ARM64 is not shipped — WinDivert has no ARM64 driver
- Elevation: requested automatically on launch (the app relaunches itself as administrator); packet manipulation needs admin rights

## Features

- **Main** — start / stop the SNI-Spoof listener with live connection count.
- **Configs** — manage up to 100 SNI-spoof profiles (listen host/port, connect IP, connect port, fake SNI). One profile is active; switch with one click (animated highlight). **Fetch from repo** pulls a curated `sni.json` list and bulk-creates profiles.
- **V2Ray**
  - Import `vless://`, `vmess://`, `trojan://`, `ss://`, or raw configs — one or many at once (paste a whole subscription list, split on each scheme).
  - One **global connection mode** applied to every config (no per-config mode):
    - **Proxy** — local SOCKS5 / HTTP only; point apps at the ports manually.
    - **Tunnel** — `sing-box` full core routes all OS traffic via a wintun adapter; `auto_route` keeps the server connection off the tunnel (no loop). Private / LAN ranges bypass the tunnel so LAN sharing keeps working. Replaces the old tun2socks path that wiped network adapters.
    - **System Proxy** — flips Windows Internet Settings to route HTTP/HTTPS apps through xray's HTTP inbound; reverted on disconnect.
  - **Ping** — real-delay test: measures latency of an HTTP request routed through the selected config.
  - Real-time uptime, download / upload rate, total bytes.
- **SNI Scanner** — bulk-resolve hostnames, flag the ones fronted by Cloudflare (good Fake SNI targets), and create a Configs profile from a result in one click. Domain-check logic ported from [Rainman69/SNISPF](https://github.com/Rainman69/SNISPF) (MIT).
- **Settings** — dark / light theme, SOCKS / HTTP proxy ports, V2Ray mode, **DNS control** (remote / direct / bootstrap servers + strategy, used in Tunnel mode), **Fast mode** (low-latency engine sockets for gaming / real-time), allow-insecure-TLS, xray log level, check-for-updates-on-launch, open app-data folder, GitHub-based update check.
- **Logs** — auto-scrolling live output; copy + clear; full engine / core output captured.
- **Startup** — instant splash window while the app loads; ReadyToRun-compiled release builds; press feedback on buttons throughout.

## Layout

| Path | Purpose |
| --- | --- |
| [app/SpoofGUI/](app/SpoofGUI/) | WinUI app: UI, database, engine supervisor, system-proxy helper |
| [app/SpoofGUI/EngineSource/](app/SpoofGUI/EngineSource/) | Vendored Python SNI-Spoofing source (tracked directly), built into the bundled engine exe |
| [installer/](installer/) | Inno Setup script (Setup installer) |
| [launcher/](launcher/) | Native launcher that keeps the portable/installed root clean |
| [scripts/](scripts/) | Build orchestrator + PyInstaller wrapper |
| [docs/](docs/) | Build, product, and design documentation |

## Build Release

Close any running `SpoofGUI.exe`, then run:

```bat
build-release.bat
```

Outputs (one Setup installer and one clean-root Portable zip per architecture):

```text
dist\SpoofGUI-Setup-amd64.exe
dist\SpoofGUI-Setup-x86.exe
dist\SpoofGUI-Portable-amd64.zip
dist\SpoofGUI-Portable-x86.zip
```

The portable zip extracts to a clean root: just `SpoofGUI.exe` (a launcher) plus an `app\` folder holding everything else — download and run, no install.

The build needs `dotnet`, a Python matching each target architecture (a 32-bit Python for x86), Visual Studio C++ build tools, Inno Setup 6, and internet access (it fetches the architecture-correct `xray.exe`, `sing-box.exe`, and `wintun.dll`). ARM64 is not shipped — the WinDivert driver has no ARM64 build. See [docs/BUILD.md](docs/BUILD.md) for full notes.

## Documentation

- [Build guide](docs/BUILD.md)
- [Design tokens](docs/DESIGN.md)
- [Product brief](docs/PRODUCT.md)

## Star History

<a href="https://www.star-history.com/?repos=ZethRise%2FSpoofGUI&type=date&legend=top-left">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=ZethRise/SpoofGUI&type=date&theme=dark&legend=top-left" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=ZethRise/SpoofGUI&type=date&legend=top-left" />
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=ZethRise/SpoofGUI&type=date&legend=top-left" />
 </picture>
</a>

## License

GPL-3.0. SpoofGUI inherits licensing from upstream SNI-Spoofing.

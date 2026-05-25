# SpoofGUI

SpoofGUI is a native Windows GUI fork for [patterniha/SNI-Spoofing](https://github.com/patterniha/SNI-Spoofing), maintained at [ZethRise/SpoofGUI](https://github.com/ZethRise/SpoofGUI).

It is not a VPN. SpoofGUI runs the local SNI-Spoofing listener as administrator and bundles Xray for outbound proxying. Point your client at:

```text
127.0.0.1:40443    (SNI-Spoofing listener)
127.0.0.1:20882    (Xray SOCKS5, when V2Ray is connected)
127.0.0.1:20883    (Xray HTTP,    when V2Ray is connected)
```

## Current Status

- App version: `1.0.0`
- Frontend: C# / .NET 10 / WinUI 3
- Backends:
  - **Python SNI-Spoofing engine** (`engine\SpoofGUI.SniSpoofEngine.exe`) — bundled PyInstaller build of the upstream tool; performs the WinDivert + fake ClientHello injection
  - **C# Xray manager** — generates config and starts the bundled `xray.exe` directly from the WinUI app
- Runtime package: self-contained Windows build, no Python / Xray / .NET install needed on target PCs
- Elevation: required, because packet manipulation needs administrator rights

## Features

- Start / stop SNI-Spoof listener with live connection count
- Edit active profile: listen host, listen port, connect IP, connect port, fake SNI
- V2Ray page:
  - Import `vless://`, `vmess://`, `trojan://`, `ss://`, or raw configs
  - Three modes per profile:
    - **Proxy Mode** — manual app-by-app SOCKS5 / HTTP setup
    - **Tunnel Mode** — `tun2socks` + `wintun` send all OS traffic through xray; route to remote proxy IP pinned via real gateway to avoid loop
    - **System Proxy** — flips Windows Internet Settings to route HTTP/HTTPS apps through xray's HTTP inbound; reverted on disconnect
  - Real-time uptime, download / upload rate, total bytes
- Logs page with copy + clear
- Dark / light themes, GitHub-based update check

## Layout

| Path | Purpose |
| --- | --- |
| [app/SpoofGUI/](app/SpoofGUI/) | WinUI app: UI, database, engine supervisor, system-proxy helper |
| [app/SpoofGUI/EngineSource/](app/SpoofGUI/EngineSource/) | Python SNI-Spoofing source, built into the bundled engine exe |
| [installer/](installer/) | Inno Setup scripts |
| [scripts/](scripts/) | Build helper scripts (PyInstaller wrapper) |
| [docs/](docs/) | Build, product, and design documentation |

## Build Release

Close any running `SpoofGUI.exe`, then run:

```bat
build-release.bat
```

Outputs:

```text
dist\SpoofGUI-Portable.exe
dist\SpoofGUI-Setup.exe
```

The script needs `python`, `dotnet`, and Inno Setup 6 on `PATH`. See [docs/BUILD.md](docs/BUILD.md) for full notes.

## Documentation

- [Build guide](docs/BUILD.md)
- [Design tokens](docs/DESIGN.md)
- [Product brief](docs/PRODUCT.md)

## License

GPL-3.0. SpoofGUI inherits licensing from upstream SNI-Spoofing.

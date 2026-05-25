---
register: product
---

# SpoofGUI Product Brief

## Purpose

SpoofGUI is a native Windows GUI fork for SNI-Spoofing. It runs a local TCP listener that injects a fake ClientHello with an out-of-window TCP sequence number, so a DPI middlebox sees an allowed hostname while the user's real traffic continues over the same socket to their proxy provider.

SpoofGUI is not a VPN. The normal workflow:

1. Launch SpoofGUI as administrator.
2. Open the Main page and press **start** to spawn the Python SNI-Spoof engine bound to `127.0.0.1:40443`.
3. Open the V2Ray page, import a config (VLESS / VMess / Trojan / Shadowsocks / raw).
4. Pick a mode for that profile:
   - **Proxy Mode** — user points clients at SOCKS `127.0.0.1:20882` / HTTP `127.0.0.1:20883` manually.
   - **Tunnel Mode** — spawns `tun2socks.exe` against xray's local SOCKS5 inbound. A wintun virtual adapter receives all traffic at the IP layer; SpoofGUI pins a `/32` route to the upstream proxy host so xray's outbound bypasses the tunnel, then installs `0.0.0.0/0` via the wintun gateway. On disconnect, the routes are removed and tun2socks is killed.
   - **System Proxy** — on connect, SpoofGUI flips the Windows Internet Settings to route the whole system through the HTTP inbound; on stop, it reverts.
5. Press **connect**. The C# app starts the bundled `xray.exe` with a generated config.

## Users

Primary user: Windows power users and developers who already understand SNI, IP addresses, ports, and Xray/V2Ray configs, but want a clean GUI instead of repeatedly editing config files and running command-line tools manually.

The app is designed for constrained networks where reliability and clarity matter more than decoration.

## Core Jobs

- Start and stop the SNI-Spoofing listener; show live connection count.
- Edit the active SNI profile: listen host, listen port, connect IP, connect port, fake SNI.
- Import, edit, delete, and run VLESS / VMess / Trojan / Shadowsocks profiles through Xray.
- Switch a profile between Proxy / Tunnel / System Proxy mode.
- Show real-time upload / download rate and total bytes on the V2Ray page.
- Show runtime logs and make them easy to copy.
- Package the tool so end users do not need to install Python, .NET, Xray, or Windows App Runtime.

## Product Principles

1. **Be honest about admin.** The app needs elevation. This is not hidden.
2. **Do not pretend to be a VPN.** The UI says "Connect and use your X-Ray Client."
3. **Config is central.** Fake SNI and target IP settings are first-class UI, not buried preferences.
4. **No telemetry.** Local app state only. Update checks go to the GitHub releases channel.
5. **Logs matter.** Runtime failures should be visible and copyable.
6. **Ship self-contained.** Releases must work on a fresh Windows PC without installing runtimes.
7. **No silent state.** System proxy is only flipped by an explicit profile mode; it is reverted on disconnect.

## Anti-References

Avoid:

- Shield logos and VPN metaphors.
- Green/red consumer toggle UI.
- Animated globes.
- Cyberpunk neon visuals.
- Marketing copy.
- Hidden configuration.

## Release Channel

Updates point to:

[ZethRise/SpoofGUI](https://github.com/ZethRise/SpoofGUI)

The upstream project remains credited as:

[patterniha/SNI-Spoofing](https://github.com/patterniha/SNI-Spoofing)

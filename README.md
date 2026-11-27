<div align="center">

# Remo-Console

**Lightweight, Portable Windows RDP & VNC Remote Desktop Manager with Live Preview Cards**

[![CI](https://github.com/TinToSer/Remo-Console/actions/workflows/ci.yml/badge.svg)](https://github.com/TinToSer/Remo-Console/actions/workflows/ci.yml)
[![Latest Release](https://img.shields.io/github/v/release/TinToSer/Remo-Console?label=Download&color=2ea44f)](https://github.com/TinToSer/Remo-Console/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform: Windows x64](https://img.shields.io/badge/Platform-Windows%2010%2F11%20x64-0078D6.svg)](#requirements)
[![Zero Dependencies](https://img.shields.io/badge/Dependencies-None%20(Single%20EXE)-brightgreen.svg)](#quick-start)

*Manage all your Windows **RDP** and Linux/macOS **VNC** servers in one dark-themed, responsive grid of live and static preview tiles — distributed as a single ~150 KB portable `.exe` with zero install and zero runtime dependencies.*

[Download Latest Release](https://github.com/TinToSer/Remo-Console/releases/latest) · [Features](#features) · [Comparison](#why-remo-console) · [Build from Source](#build-from-source) · [FAQ](#faq)

</div>

---

## Why Remo-Console?

Whether you run a **homelab**, manage **production Windows/Linux servers**, or want a fast, modern **portable alternative to mRemoteNG, RDCMan, Royal TS, or standalone VNC Viewer**, Remo-Console gives you instant visual oversight of every machine without bloat:

| Feature | Remo-Console | mstsc.exe | RDCMan | mRemoteNG | Standalone VNC |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **RDP + VNC in one dashboard** | Yes | RDP only | RDP only | Yes | VNC only |
| **Live preview card grid** | Yes | No | Basic | No | No |
| **Static screenshot + TCP reachability probe** | Yes | No | No | No | No |
| **Single portable EXE (~150 KB, no install)** | Yes | Built-in | Portable | Heavy install | Install |
| **Zero-SDK build (built-in Windows `csc.exe`)** | Yes | N/A | No | No | No |
| **Crash-safe SHA-256 atomic storage + backups** | Yes | N/A | No | No | No |
| **Windows DPAPI password encryption** | Yes | CredMan | DPAPI | Master pwd | Varies |
| **Zero telemetry / 100% offline** | Yes | N/A | Yes | Yes | Varies |

---

## Quick Start

### Option 1: Download Prebuilt Binary (Recommended)
1. Download `Remo-Console.exe` (or `Remo-Console-vX.Y.Z-win-x64.zip`) from [**GitHub Releases**](https://github.com/TinToSer/Remo-Console/releases/latest).
2. Optionally verify the file against `SHA256SUMS.txt`.
3. Run `Remo-Console.exe` directly — no installer, no admin rights required.

### Option 2: Build from Source (No Visual Studio or .NET SDK Needed)
Remo-Console compiles directly using the C# compiler (`csc.exe`) pre-installed on Windows (.NET Framework 4.x):

```powershell
.\build.ps1                    # builds bin\Remo-Console.exe
.\build.ps1 -Release           # builds release binary (no local PDB path)
.\bin\Remo-Console.exe              # launch Remo-Console
.\bin\Remo-Console.exe --selftest   # run storage, DES, and RFB protocol loopback tests
.\bin\Remo-Console.exe --version    # print version
.\bin\Remo-Console.exe --data <dir> # use a custom data folder (portable USB mode)
```

---

## Features

- **Live & Static Preview Cards**: Every host is displayed as a card in a responsive matrix. Toggle individual cards — or all cards at once — between **LIVE** (real running session rendered in the tile) and **STATIC** (last captured thumbnail with zero active connection).
- **Native Microsoft RDP Engine**: Embeds Microsoft's `mstscax` control (the same engine behind `mstsc.exe`) with **NLA/CredSSP**, clipboard sync, drive redirection, printer redirection, and audio redirection.
- **Custom VNC (RFB) Client Built from Scratch**:
  - Protocol versions **RFB 3.3, 3.7, and 3.8** (plus 4.x banner negotiation)
  - Security types: **None** and **VNC Password** (custom DES implementation supporting all VNC keys)
  - Encodings: **ZRLE**, **Hextile**, **CopyRect**, **RRE**, **Raw**
  - Pseudo-encodings: **Cursor** (lag-free native Windows cursor), **DesktopSize**, **DesktopName**
  - Full mouse, keyboard, clipboard sync, **View-Only** mode, and **Ctrl+Alt+Del** button
- **Grid & Tabs Views**:
  - **Grid (default)**: Responsive auto-stretching tile matrix (`4 × 3` readout), smooth tile-size slider, **Fit** button to show all hosts without scrolling, and smooth wheel easing.
  - **Tabs**: Classic browser-style tab strip for juggling multiple active sessions.
- **Instant Organization**: Live search (`Ctrl+F`), host groups, duplicate, password-free JSON import/export, PNG screenshots, and fullscreen (`F11`).
- **Auto-Reconnect & Reachability Monitoring**: Live cards auto-reconnect with staggered backoff so 30+ hosts never spike CPU/network simultaneously. Static cards run lightweight TCP port probes once a minute (green/red status dot + latency tooltip, toggleable in the `☰` menu).
- **One-Click `mstsc.exe` Fallback**: Open any RDP host in Windows' stock Remote Desktop client when you need RD Gateway, smart cards, or multi-monitor spans.

---

## Performance Engineering

- **Buffered VNC I/O**: 64 KB buffered socket reads; decoders write directly into the 32bpp BGRX framebuffer without per-rectangle heap allocations.
- **Fast ZRLE Inflation**: Inflates through a reusable local buffer and decodes raw tiles in a single pass.
- **Adaptive Preview Throttling**: Live preview tiles throttle VNC frame requests to ~3 fps to keep CPU and bandwidth low across dozens of tiles; opened sessions stream at full frame rate.
- **Direct GDI Blitting**: VNC frames render via `StretchDIBits` (`HALFTONE` downscaling) directly from a pinned framebuffer rather than slower GDI+ bitmaps.
- **Native Hardware Cursors**: Opened VNC sessions convert server cursor shapes into real scaled Windows cursors for zero-lag pointer movement.

---

## Keyboard Shortcuts

| Shortcut | Action |
| :--- | :--- |
| `Ctrl+N` | Add a new RDP or VNC connection |
| `Ctrl+F` | Focus search bar |
| `F11` | Toggle fullscreen mode in an active session |
| `Double-click` on card | Open session in full interactive view |
| `Middle-click` on tab | Close session tab |

---

## Persistence, Encryption & Data Safety

All configuration is stored locally in `%APPDATA%\Remo-Console` (or any custom folder via `--data <dir>` for portable USB use):

- **Encrypted Credentials (`data.rc`)**: Passwords are encrypted at rest with **Windows DPAPI** (`DataProtectionScope.CurrentUser`) — decryptable only by your Windows user account on the same machine. Exports always strip passwords.
- **Crash-Safe Atomic Writes**: Every save writes to a temporary file with `FileOptions.WriteThrough`, flushes to physical disk, verifies the **SHA-256** checksum, and atomically swaps via `File.Replace`.
- **Automatic Rolling Backups & Quarantine**: The previous intact database is kept as `data.rc.bak`, plus up to 30 timestamped snapshots in `backups\`. Corrupted files are never deleted — they are quarantined as `*.corrupt-<timestamp>` and automatically recovered from the newest intact backup.
- **Thumbnails & Logs**: Cached static previews live in `thumbs\`; diagnostic logs rotate automatically in `remo-console.log`.

---

## Requirements

- **OS**: Windows 10 or Windows 11 (x64)
- **Runtime**: .NET Framework 4.x (pre-installed on Windows)

---

## FAQ

**Does Remo-Console collect telemetry or connect to external servers?**
No. Remo-Console is 100% offline and only connects to the RDP/VNC hosts you configure.

**Can I run Remo-Console portably from a USB drive?**
Yes. Launch with `Remo-Console.exe --data .\data` to keep all settings, thumbnails, and backups alongside the executable. Note that DPAPI-encrypted passwords are tied to the Windows user profile/machine that encrypted them.

**What are the current limitations?**
- RDP live preview thumbnail capture requires the Remo-Console window to be visible and active.
- VNC does not yet support VeNCrypt/TLS/ARD authentication; VNC clipboard uses Latin-1 (`ISO-8859-1`) per RFC 6143.

---

## Contributing & Security

- Contributions are welcome! See [CONTRIBUTING.md](CONTRIBUTING.md) for build and test instructions.
- To report a security issue, please follow [SECURITY.md](SECURITY.md).
- Release history is tracked in [CHANGELOG.md](CHANGELOG.md).

## License

MIT License — see [LICENSE](LICENSE).

If you find Remo-Console useful, consider giving the repository a **Star** to help others discover it!

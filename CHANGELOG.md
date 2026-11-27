# Changelog

All notable changes to Remo-Console will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-11-27

### Added
- Unified Windows remote-control dashboard supporting **RDP** (native `mstscax` control) and **VNC** (custom RFB 3.3/3.7/3.8 client with Raw, CopyRect, RRE, Hextile, ZRLE, Cursor, DesktopSize, and DesktopName encodings).
- Responsive **Grid** and **Tabs** view modes with per-card **Live** / **Static** preview toggling, tile size slider, Fit-to-window button, search, and host grouping.
- Background TCP reachability probing for static cards with latency tooltips.
- Fallback **Open in Remote Desktop (`mstsc`)** launcher for RDP hosts.
- Crash-safe atomic storage (`%APPDATA%\Remo-Console\data.rc`) with SHA-256 verification, rolling backups, quarantine of corrupted files, and Windows DPAPI password encryption.
- Built-in `--selftest`, `--version`, `--help`, and `--data <dir>` CLI options.
- Zero-SDK build script (`build.ps1`) using the built-in Windows .NET Framework 4.x C# compiler, plus automated GitHub Actions CI and Release workflows.

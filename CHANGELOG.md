# Changelog

All notable changes to Remo-Console will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.2] - 2026-11-27

### Fixed
- **RDP Credential Popup Suppressed via `IMsRdpClientNonScriptable5`**: Implemented the `IMsRdpClientNonScriptable5` COM interface (`mstscax.dll`) to explicitly configure `EnableCredSspSupport`, `NegotiateSecurityLayer`, `PromptForCredentials`, `PromptForCredsOnClient`, `AllowPromptingForCredentials`, `AllowCredentialSaving`, `WarnAboutSendingCredentials`, `ShowRedirectionWarningDialog`, and `ClearTextPassword`, preventing redundant Windows Security credential prompts when a password is saved.

## [1.1.1] - 2026-11-27

### Fixed
- **RDP Auto-Login / Unexpected Windows Credential Popup**:
  - Set `ClearTextPassword` directly on `AdvancedSettings9` (in addition to `IMsTscNonScriptable`) alongside `NegotiateSecurityLayer = true` and `PublicMode = false` for reliable CredSSP/NLA auto-login on Windows 10/11.
  - Automatically split `DOMAIN\username` entered in the Username field into `Domain` and `UserName` for `mstscax`.
  - Removed the invalid `AdvancedSettings.PromptForCredentials` COM call that silently threw on every RDP connect.
  - Added a warning in the Edit Connection dialog if a saved DPAPI password cannot be decrypted on the current PC/user profile (e.g. after moving data across machines).

## [1.1.0] - 2026-11-27

### Added
- **Pinned / Favorites Section**: Pin frequently used hosts to a dedicated `★ Pinned` section at the top of the dashboard (accessible from each card's `⋯` menu or the Edit dialog).
- **Collapsible Group Headers**: Click any group header to collapse or expand it (persisted across restarts; automatically expands during active search so results are never hidden).
- **Group-Level Bulk Actions & Live Counts**: Group headers now display total host count and active live count badges, plus a `⋯` / right-click menu to **Make all Live**, **Make all Static**, **Rename group…**, or **Collapse / Expand all**.
- **Quick "Move to group" Submenu**: Reassign any card to an existing group, Ungrouped, or a brand-new group directly from the card menu without opening the Edit dialog.
- **Protocol Filter & Sorting**: Filter the dashboard by **RDP only** or **VNC only**, filter by **★ Pinned** or **Ungrouped**, and sort connections by **Name (A–Z)**, **Recently connected**, **Protocol**, or **Live / connected first**.
- **Group Autocomplete**: The Add/Edit Connection dialog suggests existing group names as you type.

## [1.0.0] - 2026-11-27

### Added
- Unified Windows remote-control dashboard supporting **RDP** (native `mstscax` control) and **VNC** (custom RFB 3.3/3.7/3.8 client with Raw, CopyRect, RRE, Hextile, ZRLE, Cursor, DesktopSize, and DesktopName encodings).
- Responsive **Grid** and **Tabs** view modes with per-card **Live** / **Static** preview toggling, tile size slider, Fit-to-window button, search, and host grouping.
- Background TCP reachability probing for static cards with latency tooltips.
- Fallback **Open in Remote Desktop (`mstsc`)** launcher for RDP hosts.
- Crash-safe atomic storage (`%APPDATA%\Remo-Console\data.rc`) with SHA-256 verification, rolling backups, quarantine of corrupted files, and Windows DPAPI password encryption.
- Built-in `--selftest`, `--version`, `--help`, and `--data <dir>` CLI options.
- Zero-SDK build script (`build.ps1`) using the built-in Windows .NET Framework 4.x C# compiler, plus automated GitHub Actions CI and Release workflows.

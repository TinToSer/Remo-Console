# Security Policy

## Credential Storage & Privacy

- **Local-only**: Remo-Console makes no telemetry, analytics, or external network requests. The only network traffic it initiates is to the RDP/VNC hosts you explicitly configure (including optional TCP reachability probes for static cards).
- **Encrypted at rest**: Saved passwords are encrypted using Windows DPAPI (`DataProtectionScope.CurrentUser`) and can only be decrypted by the same Windows user account on the same machine.
- **Safe exports**: Exporting connections to JSON strips all saved passwords (`Pwd` is cleared).

## Reporting a Vulnerability

If you discover a security vulnerability in Remo-Console, please do **not** open a public issue with exploit details. Instead, report it privately via GitHub Security Advisories on this repository so it can be assessed and fixed before disclosure.

# Contributing to Remo-Console

Thank you for your interest in contributing to Remo-Console!

## Building Locally

Remo-Console builds on Windows with **no .NET SDK required** — it uses the C# compiler (`csc.exe`) included with Windows (.NET Framework 4.x):

```powershell
.\build.ps1
.\bin\Remo-Console.exe --version
.\bin\Remo-Console.exe --selftest
```

For a release build (without debug symbols):

```powershell
.\build.ps1 -Release
```

## Guidelines

- Keep the zero-dependency, single-executable design intact.
- Run `.\bin\Remo-Console.exe --selftest` and ensure all tests pass with zero compiler warnings (`/warn:4`).
- Never commit personal data, host lists, credentials, or compiled binaries (`bin/`).
- Keep pull requests focused and include a brief description of the change and how it was tested.

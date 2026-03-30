# Winix

Cross-platform CLI tool suite (Windows + *nix). .NET / C# / AOT.

## Build

```
dotnet build Winix.sln
```

## Test

```
dotnet test Winix.sln
```

## Publish (AOT native binary)

```
dotnet publish src/timeit/timeit.csproj -c Release -r win-x64
```

## Architecture

- **Class libraries** (`Winix.TimeIt`, etc.) contain all logic — testable without process spawning for formatting, integration tests for process execution
- **Console apps** (`timeit`, etc.) are thin entry points — arg parsing, call library, set exit code
- **Shared library** (`Yort.ShellKit`) — terminal detection, colour, display formatting. Referenced by all tool libraries.

## Conventions

- TDD: write failing test, implement, verify, commit
- AOT-compatible: no unconstrained reflection, use trim analyzers
- All output formatting in class library (testable), all I/O in console app
- Summary output goes to stderr by default (don't pollute piped command output)
- Respect `NO_COLOR` env var (no-color.org)
- Exit codes: pass through child process exit code; 125/126/127 for tool's own errors (POSIX convention)
- Full braces always, nullable reference types enabled, warnings as errors
- Console apps use proper `namespace`/`class Program`/`static Main` — no top-level statements
- Console apps are thin: arg parsing, validation, constructing library objects, error output. Stream orchestration, event loops, and domain logic belong in the class library.
- Each tool has a `README.md` in its console app directory (e.g. `src/timeit/README.md`). Keep these up to date when flags, options, or behaviour change. Follow the existing pattern: description, install, usage/examples, options table, exit codes, colour section.
- Versioning: `Directory.Build.props` holds the dev version. Release builds override via `/p:Version=X.Y.Z`. Tag format: `v0.1.0`.
- To release: push a tag `vX.Y.Z` to main, or use manual workflow dispatch in GitHub Actions.
- NuGet package IDs: `Winix.TimeIt`, `Winix.Squeeze`, `Winix.Peep`. Publishing requires `NUGET_API_KEY` secret in GitHub repo settings.

## Windows Defender false positive

The `Winix.Squeeze.Tests` project may trigger Windows Defender (`Trojan:MSIL/Formbook.NE!MTB`). This is a false positive caused by ZstdSharp.Port's compression byte patterns triggering ML-based heuristic detection. The library has 195M+ NuGet downloads and a clean ReversingLabs scan.

To resolve, add a Defender exclusion for the repo directory (elevated PowerShell):
```powershell
Add-MpPreference -ExclusionPath 'd:\projects\winix'
```

## Project layout

```
src/Yort.ShellKit/         — shared library (ConsoleEnv, AnsiColor, DisplayFormat)
src/Winix.TimeIt/          — class library (timing logic, formatting)
src/timeit/                — console app entry point
src/Winix.Squeeze/         — class library (compression, format detection, formatting)
src/squeeze/               — console app entry point
src/Winix.Peep/            — class library (command execution, scheduling, file watching, rendering)
src/peep/                  — console app entry point
tests/Yort.ShellKit.Tests/ — xUnit tests for shared library
tests/Winix.TimeIt.Tests/  — xUnit tests
tests/Winix.Squeeze.Tests/ — xUnit tests
tests/Winix.Peep.Tests/    — xUnit tests
```

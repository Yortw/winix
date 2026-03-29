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
- **Shared library** (`Yort.ShellKit`, future) — terminal detection, colour, path normalisation, process spawning. Currently inline in each tool as `ConsoleEnv`.

## Conventions

- TDD: write failing test, implement, verify, commit
- AOT-compatible: no unconstrained reflection, use trim analyzers
- All output formatting in class library (testable), all I/O in console app
- Summary output goes to stderr by default (don't pollute piped command output)
- Respect `NO_COLOR` env var (no-color.org)
- Exit codes: pass through child process exit code; 125/126/127 for tool's own errors (POSIX convention)
- Full braces always, nullable reference types enabled, warnings as errors

## Windows Defender false positive

The `Winix.Squeeze.Tests` project may trigger Windows Defender (`Trojan:MSIL/Formbook.NE!MTB`). This is a false positive caused by ZstdSharp.Port's compression byte patterns triggering ML-based heuristic detection. The library has 195M+ NuGet downloads and a clean ReversingLabs scan.

To resolve, add a Defender exclusion for the repo directory (elevated PowerShell):
```powershell
Add-MpPreference -ExclusionPath 'd:\projects\winix'
```

## Project layout

```
src/Winix.TimeIt/          — class library (timing logic, formatting, terminal detection)
src/timeit/                — console app entry point
src/Winix.Squeeze/         — class library (compression, format detection, formatting)
src/squeeze/               — console app entry point
tests/Winix.TimeIt.Tests/  — xUnit tests
tests/Winix.Squeeze.Tests/ — xUnit tests
```

# Winix

**Cross-platform CLI tools for the gaps between Windows and *nix.**

Winix is a suite of small, focused command-line tools built with .NET and compiled to native binaries via AOT. Each tool fills a gap where Linux utilities either don't exist on Windows, have poor/abandoned ports, or where a modern implementation can do better than the original.

## Tools

| Tool | What it does | *nix equivalent |
|------|-------------|-----------------|
| **timeit** | Time a command — wall clock, CPU time, peak memory, exit code | `time` |
| **peep** | Watch a command on interval + re-run on file changes | `watch` + `entr` |
| **squeeze** | Multi-format compression (gzip, brotli, zstd, zlib) | `gzip`, `brotli`, `zstd` |
| **treex** | Enhanced directory tree with colour, filtering, sizes, .gitignore | `tree` |
| **schedule** | Crontab + RRULE over Windows Task Scheduler | `crontab` |
| **xargs** | Build and execute commands from stdin with correct Windows quoting | `xargs` |

*More tools planned — see [design notes](docs/plans/2026-03-28-winix-design-notes.md).*

## Install

```bash
# Individual tool
dotnet tool install -g winix.timeit

# All tools (coming soon)
dotnet tool install -g winix
```

## Quick Start

```bash
# Time a build
timeit dotnet build

# JSON output for CI
timeit --json dotnet test

# One-line format for logs
timeit -1 dotnet publish -c Release
```

## Building from Source

```bash
git clone <repo-url>
cd winix
dotnet build Winix.sln
dotnet test Winix.sln

# AOT native binary
dotnet publish src/timeit/timeit.csproj -c Release -r win-x64
```

## Status

Early development. `timeit` is the first tool — functional and AOT-ready. More tools coming.

## License

MIT

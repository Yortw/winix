<p align="center">
  <img src="Winix.png" alt="Winix — a penguin peeking through Windows shutters" width="128" />
</p>

# Winix

**Cross-platform CLI tools for the gaps between Windows and *nix.**

Winix is a suite of small, focused command-line tools built with .NET and compiled to native binaries via AOT. Each tool fills a gap where Linux utilities either don't exist on Windows, have poor/abandoned ports, or where a modern implementation can do better than the original.

## Tools

| Tool | What it does | *nix equivalent | Status |
|------|-------------|-----------------|--------|
| [**timeit**](src/timeit/README.md) | Time a command — wall clock, CPU time, peak memory, exit code | `time` | Shipped |
| [**peep**](src/peep/README.md) | Watch a command on interval + re-run on file changes | `watch` + `entr` | Shipped |
| [**squeeze**](src/squeeze/README.md) | Multi-format compression (gzip, brotli, zstd) | `gzip`, `brotli`, `zstd` | Shipped |
| [**wargs**](src/wargs/README.md) | Build and execute commands from stdin | `xargs` | Shipped |
| [**files**](src/files/README.md) | Find files by name, size, date, type, and content | `find` | Shipped |
| [**treex**](src/treex/README.md) | Enhanced directory tree with colour, filtering, sizes | `tree` | Shipped |

### Planned

| Tool | What it does | *nix equivalent |
|------|-------------|-----------------|
| **schedule** | Crontab + RRULE over Windows Task Scheduler | `crontab` |

*See [design notes](docs/plans/2026-03-28-winix-design-notes.md) for more ideas.*

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/timeit    # individual tool
scoop install winix/winix     # all tools
```

### Winget (Windows, stable releases)

```bash
winget install Winix.TimeIt
winget install Winix.Squeeze
winget install Winix.Peep
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.TimeIt
dotnet tool install -g Winix.Squeeze
dotnet tool install -g Winix.Peep
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).
Available for Windows (x64), Linux (x64), and macOS (x64, ARM64).

## Quick Start

```bash
# Time a build
timeit dotnet build

# Watch a command, re-run on file changes
peep -w "src/**/*.cs" dotnet test

# Compress with zstd
squeeze --zstd largefile.bin

# JSON output for CI
timeit --json dotnet test
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

Six tools shipped (`timeit`, `peep`, `squeeze`, `wargs`, `files`, `treex`) — all functional, tested, and AOT-ready. More tools planned.

## License

MIT

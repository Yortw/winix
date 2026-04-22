<p align="center">
  <img src="Winix.png" alt="Winix — a penguin peeking through Windows shutters" width="128" />
</p>

# Winix

[![CI](https://github.com/Yortw/winix/actions/workflows/ci.yml/badge.svg)](https://github.com/Yortw/winix/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![GitHub release](https://img.shields.io/github/v/release/Yortw/winix?include_prereleases)](https://github.com/Yortw/winix/releases)

**Small, native, cross-platform CLI tools — the same behaviour everywhere.**

Winix is a suite of focused command-line tools compiled to native binaries via .NET AOT. One `.exe` per tool: no runtime to install, no POSIX emulation layer, no shell dependency. Each tool works identically in `cmd.exe`, PowerShell, bash, zsh, CI agents, File Explorer "Open with", and scheduled tasks, on Windows, Linux, and macOS — same flags, same output, same exit codes. Scripts and muscle memory transfer across platforms without adaptation.

On Windows, many of these tools simply don't exist natively — there's no built-in `find`, `xargs`, `watch`, `man`, `less`, or `netcat`. On Unix, Winix papers over BSD-vs-GNU flag differences, adds structured output to tools that were never designed to be parsed, and provides the same interface that's available on Windows. Output is colourful and readable for humans in the terminal by default; every tool also has a `--json` mode for scripts and agents.

## Tools

| Tool | What it does |
|------|-------------|
| [**timeit**](src/timeit/README.md) | Time a command — wall clock, CPU time, peak memory, exit code |
| [**squeeze**](src/squeeze/README.md) | Compress and decompress files (gzip, brotli, zstd) |
| [**peep**](src/peep/README.md) | Watch a command on interval + re-run on file changes |
| [**wargs**](src/wargs/README.md) | Build and execute commands from stdin |
| [**files**](src/files/README.md) | Find files by name, size, date, type, and content |
| [**treex**](src/treex/README.md) | Enhanced directory tree with colour, filtering, size rollups |
| [**man**](src/man/README.md) | Man page viewer with colour, hyperlinks, and pager |
| [**less**](src/less/README.md) | Terminal pager with ANSI colour, search, and follow mode |
| [**whoholds**](src/whoholds/README.md) | Find which processes hold a file lock or bind a port |
| [**schedule**](src/schedule/README.md) | Cross-platform task scheduling with cron expressions |
| [**nc**](src/nc/README.md) | TCP/UDP send-receive, port checks, TLS clients |
| [**winix**](src/winix/README.md) | Suite installer — installs and updates all tools via native package managers |

## Why each tool, on each platform

The biggest value is on **Windows**, where many of these tools simply don't exist. On Linux and macOS the tools still earn their keep by combining multiple utilities, improving defaults, or providing a consistent interface across platforms. Even where a native equivalent exists, there's value in a single set of tools that behaves identically everywhere — scripts, CI pipelines, and muscle memory transfer across platforms without adaptation.

| Tool | Windows | Linux / macOS |
|------|---------|---------------|
| **timeit** | Nothing built-in. `Measure-Command` exists but doesn't show memory, doesn't pass exit codes, can't be piped. | Improves on `time` with peak memory, machine-readable JSON, and proper exit-code passthrough. |
| **squeeze** | No native CLI for compression. Users install 3+ separate tools or reach for 7-Zip. | One tool instead of separate `gzip`, `brotli`, `zstd` CLIs. Auto-detects format on decompression. |
| **peep** | No `watch`. No `entr`. PowerShell loops are verbose and don't handle file-watching. | Combines `watch` (interval) + `entr` (file-change trigger) in one binary with a TUI. |
| **wargs** | No `xargs`. `ForEach-Object` is PowerShell-only and doesn't handle parallel execution or line-delimited input well. | Sane defaults over `xargs`: line-delimited, no `-0` needed, correct quoting on all platforms. |
| **files** | No `find`. `Get-ChildItem` exists but is slow, verbose, and PowerShell-only. | Cleaner than `find`, with glob patterns, gitignore support, text/binary detection, and JSON output. |
| **treex** | Built-in `tree` is bare-bones (no colour, no sizes, no filtering). | Adds colour, size rollups, gitignore, clickable hyperlinks, and filtering over standard `tree`. |
| **man** | No `man` at all. | Alternative renderer with colour and hyperlinks. Renders groff natively — no groff install needed. Bundled pager. |
| **less** | No pager. `more` exists but can't scroll back, search, or handle ANSI colour. | Better defaults than system `less`: ANSI colour on by default, mouse scroll, follow mode. |
| **whoholds** | No built-in file-lock query. `handle.exe` (Sysinternals) requires a separate download + admin rights. | Wraps `lsof` with cleaner output, structured JSON, and a unified syntax for both files and ports. |
| **schedule** | `schtasks.exe` exists but is notoriously hard to script. No cron syntax. | Unified interface over `crontab` with the same flags and output format as Windows. |
| **nc** | No `netcat`. `Test-NetConnection` is verbose and limited. | Consistent behaviour across BSD/GNU/ncat forks. Adds TLS client support and port-range checking. |
| **winix** | Suite installer via Scoop or winget. | Suite installer via brew or dotnet tool. |

## Prior art — Cygwin, WSL, Git Bash

Unix tools have been available on Windows for decades via [Cygwin](https://www.cygwin.com/), [MSYS2](https://www.msys2.org/) / [Git Bash](https://git-scm.com/), and [WSL](https://learn.microsoft.com/windows/wsl/). These are excellent at what they do — they bring a full POSIX environment (bash, coreutils, rsync, ssh, a few thousand packages) to Windows so real Linux binaries can run there.

Winix takes a different approach and is complementary rather than competing:

- **No emulation layer.** Tools are native `.exe` files, not POSIX binaries calling `cygwin1.dll`. Paths are native (`C:\foo`, not `/cygdrive/c/foo`). No fork-emulation overhead, no path translation surprises.
- **Redistributable on their own.** Copy a single `.exe` to a machine and it works. No Cygwin base install, no `cygwin1.dll` alongside it, no `$PATH` gymnastics.
- **First-class outside bash.** Works from `cmd.exe`, PowerShell, File Explorer "Open with", Windows Task Scheduler actions, IIS hooks, SSIS packages — not just inside a `mintty` + bash session.
- **Package-manager installable per tool.** `scoop install winix/timeit`, `winget install Winix.TimeIt`, `dotnet tool install -g Winix.TimeIt` — seconds on a CI runner, no base environment required.
- **Cross-platform source, not a Windows port.** Linux and macOS builds aren't afterthoughts — the same source compiles to native binaries on every platform, with identical behaviour and flags everywhere.

If you want the entire GNU/Unix userland on Windows and you're happy working inside a bash shell, Cygwin or WSL is the right tool — and Winix runs fine alongside either of them. If you want a smaller, focused set of modern CLI utilities that integrate with whatever environment you're already in, that's what Winix is for.

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
winget install Winix.Wargs
winget install Winix.Files
winget install Winix.TreeX
winget install Winix.Man
winget install Winix.Less
winget install Winix.WhoHolds
winget install Winix.Schedule
winget install Winix.NetCat
winget install Winix.Winix
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.TimeIt
dotnet tool install -g Winix.Squeeze
dotnet tool install -g Winix.Peep
dotnet tool install -g Winix.Wargs
dotnet tool install -g Winix.Files
dotnet tool install -g Winix.TreeX
dotnet tool install -g Winix.Man
dotnet tool install -g Winix.Less
dotnet tool install -g Winix.WhoHolds
dotnet tool install -g Winix.Schedule
dotnet tool install -g Winix.NetCat
dotnet tool install -g Winix.Winix
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).
Available for Windows (x64), Linux (x64), and macOS (x64, ARM64). Windows binaries are [Authenticode-signed](https://github.com/Yortw/winix/releases).

## Quick Start

```bash
# Time a build
timeit dotnet build

# Watch tests, re-run on file changes
peep -w "src/**/*.cs" dotnet test

# Compress with zstd
squeeze --zstd largefile.bin

# Find files and batch-process them
files src --ext cs | wargs dotnet format

# Browse a directory tree with sizes
treex --size --gitignore --no-hidden

# Read a man page on any platform
man timeit

# Find what's locking a file
whoholds myapp.dll

# Quick port check
nc -z db.internal 5432

# Schedule a recurring job (cross-platform cron)
schedule add "0 9 * * 1-5" -- backup.sh /data

# JSON output for CI
timeit --json dotnet test

# AI agent metadata
files --describe
```

## For AI agents

Winix tools are designed to be invoked by scripts and agents, not just humans:

- **`--describe`** on every tool returns structured JSON metadata — flags, types, defaults, examples, composability hints, exit codes, and the `--json` output schema.
- **`--json`** on every tool produces machine-parseable output with standard, documented fields.
- **Consistent exit codes** across the suite (0 = success, 125 = usage error).
- **[llms.txt](llms.txt)** at the repo root plus per-tool agent guides in [`docs/ai/`](docs/ai/) give agents a single entry point to discover and use the suite.

## Building from source

```bash
git clone https://github.com/Yortw/winix.git
cd winix
dotnet build Winix.sln
dotnet test Winix.sln

# AOT native binary (single tool)
dotnet publish src/timeit/timeit.csproj -c Release -r win-x64
```

## License

MIT

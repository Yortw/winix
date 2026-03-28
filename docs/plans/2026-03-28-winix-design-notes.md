# Winix — Design Notes

**Date:** 2026-03-28
**Status:** Brainstorming / Pre-design
**Project:** `D:\projects\winix`

## Vision

**Winix** (Windows + \*nix) is a suite of cross-platform CLI tools that fill gaps where Linux utilities either don't exist on Windows, have poor/abandoned ports, or where a modern .NET implementation can do better than the original.

The tools share a common platform-abstraction library that handles the hard problems (argument quoting, glob expansion, path normalisation, pipe encoding) correctly on every platform. Built with .NET 8+ and AOT-compiled to native single-file binaries — no runtime dependency, fast startup, works everywhere.

### Why .NET?

- AOT compiles to small native binaries (single file, sub-50ms startup)
- Cross-platform by default (Windows, Linux, macOS)
- Built-in support for compression (GZip, Brotli, Zlib), file watching, Task Scheduler COM interop
- Rich ecosystem for anything else (Zstandard via ZstdSharp, iCalendar via Ical.Net)
- Troy already knows it deeply — faster to build, easier to maintain

### Why not just use Rust tools?

The Rust CLI renaissance (ripgrep, fd, bat, eza) has filled *some* gaps excellently. Winix targets the gaps that remain — tools the Rust community hasn't built or where Windows is a second-class citizen. The shared library angle also means each new tool is cheaper to build than the last.

---

## Tool Catalogue

### Tier 1 — High priority, build first

#### `peep` — Watch + File Watcher (watch + entr combined)

**What it does:** Two modes that combine naturally:
- **Watch mode:** Run a command every N seconds, show output in-place (`peep -- git status`)
- **File-watch mode:** When files matching a glob change, run a command (`peep -w "src/**/*.cs" -- dotnet build`)
- **Combined:** Watch on interval AND re-run immediately on file change

**Why it's needed:** No native `watch` on Windows at all. No native `entr` equivalent. The few ports are unmaintained Node/Python wrappers. Windows has `FileSystemWatcher` / `ReadDirectoryChangesW` which is actually better than Linux inotify (recursive watching is native).

**Opportunity to exceed the originals:**
- Diff-highlighting between runs (even Linux `watch` does this poorly)
- Colour output preservation
- Exit-on-change or exit-on-success modes
- `.gitignore`-aware file watching

**Effort:** Small. High daily utility. Natural first tool.

#### `squeeze` — Multi-format Compression

**What it does:** Single tool for gzip, brotli, zstd, and zlib compression/decompression.

```
squeeze file.csv                  # → file.csv.gz (gzip default)
squeeze -b file.css               # → file.css.br (brotli)
squeeze -z file.log               # → file.log.zst (zstd)
squeeze -d file.csv.gz            # → file.csv (auto-detect format)
cat dump.sql | squeeze > dump.gz  # pipe mode
```

**Why it's needed:** Windows ships no compression CLI tools at all. Git Bash has `gzip` (MSYS2 port) but nothing else. 7-Zip works but has its own syntax world. No standalone `brotli` CLI is widely installed on *any* platform.

**Tech:** .NET built-in `GZipStream`, `BrotliStream`, `ZLibStream` + `ZstdSharp` NuGet (pure managed).

**Unique angle:** Brotli support makes this more useful than a straight gzip port — brotli is the HTTP content-encoding standard but has no easy CLI tooling anywhere.

**Effort:** Small-Medium.

#### `timeit` — Command Timer

**What it does:** Time how long a command takes, showing wall clock, CPU time, peak memory, and exit code.

```
timeit dotnet build
  ... normal build output streams through ...

  real    12.4s
  user     9.1s (CPU)
  peak    482 MB
  exit    0
```

**Why it's needed:** PowerShell `Measure-Command` is verbose, doesn't stream stdout, requires script block syntax. Git Bash `time` measures inside MSYS2 overhead. No simple native option.

**Tech:** Win32 `GetProcessTimes` and `GetProcessMemoryInfo` (cross-platform equivalents via .NET `Process` class).

**Effort:** Tiny. Almost trivially small. Could be the very first tool built.

#### `tree+` — Enhanced Tree

**What it does:** Directory tree display with colour, filtering, size rollups, and `.gitignore` awareness.

**Why it's needed:** Windows native `tree` is DOS-era — no colour, no filtering, no size info, no gitignore. Rust `eza --tree` exists but tree is a secondary feature.

**Effort:** Small. Satisfying, immediately visible improvement.

### Tier 2 — Medium priority

#### `schedule` — Crontab for Windows Task Scheduler + RRULE

**What it does:** Familiar crontab-style interface over Windows Task Scheduler, with RRULE (RFC 5545 iCalendar recurrence) support for complex schedules.

```
schedule add "backup" "0 2 * * *" -- robocopy C:\data E:\backup /mir
schedule add "reports" "FREQ=MONTHLY;BYDAY=FR;BYSETPOS=-1;BYHOUR=17" -- dotnet run --project ReportGen
schedule add "standup" --every weekday --at 9:15 -- start https://teams.microsoft.com/...
schedule ls
schedule rm "backup"
```

**Why it's needed:** `schtasks.exe` is spectacularly bad UX. Task Scheduler GUI buries features behind wizard dialogs. The engine supports complex triggers (every other week, last weekday of month, repetition intervals) but the CLI exposes ~10% of it.

**RRULE advantage:** More expressive than cron — "every other Tuesday", "last Friday of the month", "run 10 times then stop", skip-dates (EXDATE). Same format Outlook uses internally, so calendar-to-task import is theoretically possible.

**Tech:** Task Scheduler COM API via `Microsoft.Win32.TaskScheduler` NuGet. RRULE parsing via `Ical.Net` or subset implementation.

**Platform note:** This is inherently Windows-specific (wraps Task Scheduler). On Linux it could generate crontab entries instead, but that's a stretch goal not a requirement.

**Effort:** Medium.

#### `xargs` — Native Windows xargs

**What it does:** Build and execute commands from stdin input, with batching and parallelism.

```
grep -rl "TODO" --include="*.cs" | xargs code
git diff --name-only --diff-filter=M -- "*.cs" | xargs git add
ls *.png | xargs -P4 -I{} magick {} -resize 50% thumbs/{}
```

**Key features:**
- `-P N` — parallel execution (killer feature for build/CI tasks)
- `-I{}` — placeholder substitution
- `-0` — null-delimited input (safe filenames)
- `-n N` — N arguments per invocation

**Why it's needed:** Git Bash xargs fights with Windows paths (MSYS2 path conversion), quoting (`"` vs `'`), UNC paths, and command names. A native version using `CreateProcess` directly eliminates the shell mangling layer.

**Hard part:** Getting quoting/escaping right for Windows `CommandLineToArgvW` semantics. Fiddly but well-documented.

**Effort:** Medium.

#### `rsync` — Delta File Sync

**What it does:** Efficient file synchronisation with delta transfer (only send changed bytes), checksumming, SSH transport, and filter syntax.

**Why it's needed:** The #1 gap on Windows. cwRsync is commercial, DeltaCopy is dead, WSL rsync has path-mangling issues. Robocopy covers some use cases but lacks delta transfer, checksumming, and the filter syntax people know.

**The algorithm:** Rolling checksum (Adler-32 variant) + MD4/MD5 block matching. Well-documented in Andrew Tridgell's thesis, but non-trivial to implement correctly, especially with the edge cases around sparse files, symlinks, permissions, and resume.

**Effort:** Large. Hardest tool in the suite but highest impact. Consider as a later project once the shared library and simpler tools are proven.

### Tier 3 — Lower priority / niche

#### `pv` — Pipe Viewer (progress for piped data)

**Honest assessment:** Very useful on Linux where everything is a pipe. On Windows, pipe-centric workflows are much less common. Use cases exist (docker image loads, large CSV processing, streaming between custom CLI tools) but they're maybe 10% of Linux frequency.

**Effort:** Small. Low priority due to limited Windows utility.

#### `htop` — TUI Process Viewer

**Opportunity beyond a port:** A Windows-native TUI process viewer that understands services, IIS app pools, handle counts, port bindings, .NET runtime info (GC heap, threadpool). None of that is in btop even on Linux.

**Effort:** Large. Big Win32 API surface. "If you'd enjoy it" project.

#### `nc` (netcat) — TCP/UDP Swiss Army Knife

Quick port checks, temporary TCP listeners, simple file transfer between machines. Useful for dev/ops debugging. `Test-NetConnection` in PowerShell is verbose and limited.

**Effort:** Medium. Niche audience.

---

## Shared Library: ShellKit (working name)

A cross-platform library encoding the platform-specific knowledge every CLI tool needs. Solves five specific, well-defined problems:

### Components

| Component | Problem | .NET Helps? |
|-----------|---------|-------------|
| **ArgBuilder** | Build correctly-quoted command lines from `string[]`. Windows `CreateProcess` takes a single string re-parsed by each program via `CommandLineToArgvW`. POSIX `execv` passes an actual argv array. | `ProcessStartInfo.ArgumentList` handles basics; edge cases remain |
| **GlobExpander** | On Linux, the shell expands `*.cs` before the program sees it. On Windows, the program gets literal `*.cs`. | `Microsoft.Extensions.FileSystemGlobbing` exists but lacks `.gitignore` awareness |
| **PathNorm** | Drive letters, UNC, long paths (`\\?\`), `~` expansion, forward/back slash, case-insensitive matching | `System.IO.Path` handles basics; no UNC normalisation or `~` |
| **PipeReader** | Newline vs null delimited, UTF-8 vs console code pages, BOM handling, `\r\n` vs `\n` | `Console.InputEncoding` + `StreamReader` cover most cases |
| **ProcessRunner** | Spawn processes with correct argv, environment, stdio piping, timing/memory capture | `System.Diagnostics.Process` works but needs careful wrapping |
| **ConsoleEnv** | Detect pipe vs terminal, encoding, ANSI support, terminal width | Partial — needs platform-specific detection |

### Open Question: Separate Repo?

The library could be:
1. **Separate repo + NuGet package** — consumed by Winix as a dependency. Better for open-source discovery, independent versioning, other people building Windows CLI tools could use it.
2. **In the Winix repo** — published as a separate NuGet package but developed alongside the tools. Simpler to iterate, avoids cross-repo coordination overhead during early development.
3. **Start in Winix, extract later** — pragmatic middle ground. Build it inline, extract when/if there's external demand.

**Current leaning:** Option 1 (separate repo) feels right given the library has independent value. But option 3 is more pragmatic for getting started. Decision can be deferred until after the first 2-3 tools prove the library's shape.

---

## Architecture

### Repo Structure (tentative)

```
winix/
├── src/
│   ├── ShellKit/              ← shared library (NuGet: ShellKit)
│   ├── Winix.Peep/            ← class library (tool logic)
│   ├── Winix.Squeeze/         ← class library (tool logic)
│   ├── Winix.TimeIt/          ← class library (tool logic)
│   ├── Winix.TreePlus/        ← class library (tool logic)
│   ├── Winix.Schedule/        ← class library (tool logic)
│   ├── Winix.Xargs/           ← class library (tool logic)
│   ├── peep/                  ← thin console app (NuGet tool: winix.peep)
│   ├── squeeze/               ← thin console app (NuGet tool: winix.squeeze)
│   ├── timeit/                ← thin console app (NuGet tool: winix.timeit)
│   ├── treex/                 ← thin console app (NuGet tool: winix.treex)
│   ├── schedule/              ← thin console app (NuGet tool: winix.schedule)
│   ├── xargs/                 ← thin console app (NuGet tool: winix.xargs)
│   └── winix/                 ← multi-call binary (NuGet tool: winix)
│                                references all Winix.* libraries
│                                dispatches by argv[0] or subcommand
├── tests/
│   ├── ShellKit.Tests/
│   ├── Winix.Peep.Tests/
│   └── ...
├── docs/
│   └── plans/
└── Winix.sln
```

### Packaging Strategy

**Individual tools** — each published as a separate dotnet tool NuGet package:
- `dotnet tool install -g winix.peep` — installs just `peep`
- `dotnet tool install -g winix.squeeze` — installs just `squeeze`
- CI pipelines can install only what they need

**All-in-one** — a multi-call binary published as `winix`:
- `dotnet tool install -g winix` — one install, every tool available
- `winix timeit dotnet build` — subcommand invocation works immediately
- `winix --install-links` — creates hardlinks for standalone `peep`, `timeit`, etc.
- Multi-call dispatch: checks `Path.GetFileNameWithoutExtension(Environment.ProcessPath)` then falls back to first arg as subcommand

**Architecture for this:** Each tool's logic lives in a class library (`Winix.TimeIt`), with a thin console app (`timeit`) that calls into it. The `winix` multi-call binary references all libraries and dispatches. No code duplication — same logic whether invoked as `timeit` or `winix timeit`.

### Build & Publish

- .NET 8+ (or .NET 9/10 as available)
- AOT compilation for native single-file binaries
- Target: `win-x64`, `linux-x64`, `osx-x64` (and arm64 variants)
- `<PackAsTool>true</PackAsTool>` from day one on all console apps
- ShellKit published as NuGet package (whether from this repo or separate)
- GitHub Release automation for standalone binaries (CI build matrix)

### Cross-Platform Strategy

- All tools target all platforms by default
- Platform-specific features (Task Scheduler in `schedule`, `GetProcessTimes` in `timeit`) use conditional compilation or runtime detection
- `schedule` is the exception — inherently platform-specific (Task Scheduler on Windows, cron on Linux), but the RRULE parsing is shared

### Terminal Quality

All tools must be good terminal citizens. This is a differentiator — most CLI ports treat terminal output as an afterthought. Winix tools should feel native in modern terminals.

**OSC 8 hyperlinks** — clickable links in supporting terminals:
- `tree+` — every filename is a clickable `file:///` link
- `peep` — changed files link to themselves
- `timeit` — link to build logs or project files
- Error/help messages — link to online docs
- `squeeze` — output filename is clickable

**Colour** — meaningful, not decorative:
- Colour should convey information (file types, diff changes, pass/fail, compression quality)
- Not just "make it pretty" — every colour should mean something

**Automatic detection** — correct behaviour without flags:
- Piped output → raw text, no colour, no OSC 8
- Dumb terminal → plain text
- Modern terminal → full colour + hyperlinks
- `--color=always|never|auto` override on all tools
- `NO_COLOR` env var respected (no-color.org standard)
- Terminal width awareness for responsive layout

**Implementation** — this lives in ShellKit's `ConsoleEnv` component:
- `IsPiped` — is stdout a pipe or a terminal?
- `SupportsColor` — ANSI colour detection (checks `TERM`, `COLORTERM`, `WT_SESSION`, etc.)
- `SupportsOsc8` — hyperlink support (assume yes for known-modern terminals: Windows Terminal, iTerm2, VTE-based; fall back to plain text otherwise)
- `TerminalWidth` — columns available for responsive layout
- `Hyperlink(url, text)` — emit OSC 8 or plain text depending on support
- `Colorize(text, color)` — emit ANSI or plain text depending on support
- `NoColor` — `NO_COLOR` env var check

Every tool uses these helpers — no per-tool detection logic. One correct implementation, shared everywhere.

**Note:** OSC 8 detection is imperfect (no terminal query for it). Practical approach is a known-terminal allowlist + opt-in flag. CLIo supports OSC 8 natively.

---

## Build Order (suggested)

1. **timeit** — Trivially small, proves the AOT pipeline, delivers immediate value
2. **peep** — Small, high daily utility, exercises FileSystemWatcher + process spawning
3. **squeeze** — Small-medium, exercises pipe I/O, brotli gives unique angle
4. **tree+** — Small, satisfying, exercises glob/path handling
5. **Extract ShellKit** — By this point, the shared patterns are clear
6. **xargs** — Medium, exercises the full ShellKit (quoting, globs, process spawning, pipes)
7. **schedule** — Medium, exercises platform-specific APIs
8. **rsync** — Large, tackle once everything else is proven

---

## Naming

- **Suite:** Winix (Windows + *nix)
- **Shared library:** ShellKit (working name — alternatives: TermKit, CliKit, ShellCore)
- **Tool names:** Short, memorable, slightly distinct from originals to avoid confusion
  - `peep` (not `watch` — avoids conflict with Windows `watch` if one ever appears)
  - `squeeze` (not `gzip` — covers multiple formats)
  - `timeit` (not `time` — avoids shell builtin conflict)
  - `tree+` or `treex` (TBD — `tree` itself might be fine if installed to a different PATH location)
  - `schedule` (not `crontab` — more descriptive of what it actually does)
  - `xargs` (keep the name — muscle memory is too strong)

---

## Distribution & CI Accessibility

A key goal is making Winix tools **trivially easy to use in CI pipelines** — Azure DevOps Pipelines primarily, but also GitHub Actions and other hosted runners.

### Installation Channels (priority order)

1. **`dotnet tool install`** — .NET global tools. Works anywhere the .NET SDK is installed, which is already true on most hosted build agents. `dotnet tool install -g winix.timeit` or `dotnet tool install -g winix.peep`. This is the lowest-friction path for CI.

2. **Standalone native binaries** — AOT-compiled, no runtime dependency. Downloadable from GitHub Releases. CI pipelines can `curl` + `chmod` (or equivalent) in one step. This is the path for environments without the .NET SDK.

3. **GitHub Actions marketplace** — A `winix/setup` action that installs the tools onto the runner. Familiar pattern for GH Actions users.

4. **Azure DevOps Pipelines task** — Custom task extension for AzDO marketplace. `- task: Winix@1` with inputs for which tools to install. Native experience for AzDO users.

5. **Package managers** — Chocolatey (Windows), Homebrew (macOS), apt/snap (Linux), winget. These are slower to set up but matter for developer workstation installs.

6. **Pre-installed on hosted runners** — The aspiration. GitHub-hosted runners and Microsoft-hosted agents include tools when they're popular enough. This requires genuine adoption — having the other channels working and the tools being useful drives this organically.

### Build Implications

- AOT compilation is non-negotiable — CI runners shouldn't need .NET runtime for standalone binaries
- Each tool must be independently installable (not a monolith)
- `dotnet tool` packaging is easy to add and should be there from day one
- GitHub Release automation (build matrix for win-x64, linux-x64, osx-x64, osx-arm64) should be set up early
- Version all tools from a single version source (Directory.Build.props) for simplicity

### What Makes Tools Get Pre-Installed

Looking at what's already on hosted runners: tools get included when they're genuinely used by a critical mass of pipelines. The path is:
1. Tools work well and solve a real problem
2. People install them in their CI scripts
3. Enough people do this that runner maintainers notice
4. Tool gets added to the runner image

Winix's best candidates for this path: `timeit` (build performance measurement is universal), `squeeze` (compression in CI is universal), and `tree+` (debugging build outputs).

---

## Open Questions

1. **ShellKit repo strategy** — separate repo now, or extract later? (Leaning: start inline, extract after 2-3 tools)
2. **Tool naming** — finalise names, especially tree variant
3. **Packaging** — individual tool installs? Or a single `winix` meta-package/installer?
4. **License** — MIT (consistent with Troy's other OSS projects)
5. **GitHub org** — under `yortw`? Or a new `winix-cli` org?
6. **rsync compatibility** — wire-compatible with rsync protocol? Or just same UX, different protocol? (Wire compatibility is dramatically harder)

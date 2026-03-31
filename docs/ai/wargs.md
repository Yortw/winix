# wargs — AI Agent Guide

## What This Tool Does

`wargs` reads items from stdin (one per line) and executes a command for each one. It is the `xargs` replacement for Windows and for Unix environments where `xargs` has path-mangling or quoting bugs (notably Git Bash on Windows). Use it to build command pipelines from file lists, server lists, or any line-delimited input.

## Platform Story

Cross-platform, but especially valuable on **Windows**. Git Bash's `xargs` mangles Windows paths (converting `/c/` prefixes, breaking backslash paths), making the classic `find | xargs` pattern unreliable. `wargs` handles Windows paths correctly and falls back through the platform shell automatically for builtins (`echo`, `del`, `type` on Windows). On **Unix/macOS**, `wargs` offers improvements over `xargs`: line-delimited input by default (no whitespace-splitting surprises), `{}` auto-detection without `-I{}`, `--dry-run`, `--fail-fast`, and JSON/NDJSON output.

## When to Use This

- Running a command against every file from `files`: `files src --ext cs | wargs dotnet format {}`
- Deleting all matching files: `files . --glob '*.tmp' | wargs rm`
- Parallel processing: `files . --ext cs | wargs -P4 dotnet format {}`
- Batch operations with confirmation: `files . --glob '*.bak' | wargs -p rm`
- Previewing what would run: `files . --ext log | wargs --dry-run rm`
- Processing server lists: `cat servers.txt | wargs ssh {} uptime`

Prefer `wargs` over shell `for` loops — it handles parallelism, batching, dry-run, and structured output that loops cannot provide.

## Common Patterns

**The core pattern — files + wargs (replaces find | xargs):**
```bash
files src --ext cs | wargs dotnet format {}
```

**Parallel execution — 4 concurrent jobs:**
```bash
files . --ext cs | wargs -P4 dotnet format {}
```

**Dry run — print what would be executed without running it:**
```bash
files . --glob '*.log' | wargs --dry-run rm
```

**Confirm before each job:**
```bash
files . --glob '*.bak' | wargs -p rm
```

**Batch mode — pass multiple items per invocation:**
```bash
cat urls.txt | wargs -n10 curl
```

**Fail fast — stop on first error:**
```bash
cat servers.txt | wargs --fail-fast ssh {} "systemctl restart app"
```

**Verbose — print each command to stderr before running:**
```bash
cat hosts.txt | wargs -v ping -c1 {}
```

## Composing with Other Tools

**files + wargs** — the primary composition pair:
```bash
# Compress all JSON files with zstd
files . --glob '*.json' | wargs squeeze --zstd

# Delete all log files older than 7 days
files . --ext log --older 7d | wargs rm

# Run dotnet format on all C# files, 4 at a time
files src --ext cs --gitignore | wargs -P4 dotnet format {}
```

**git diff + wargs** — run a tool on only changed files:
```bash
git diff --name-only HEAD | wargs dotnet format {}
```

**wargs --ndjson + jq** — parse streaming results:
```bash
cat servers.txt | wargs --ndjson ssh {} uptime 2>&1 >/dev/null | jq 'select(.exit_code != 0) | .item'
```

**wargs + squeeze** — compress files found by files:
```bash
files . --ext log --older 7d | wargs squeeze --gzip --remove
```

## Gotchas

**Line-delimited by default, not whitespace.** Unlike `xargs`, `wargs` splits input on newlines only. A filename with spaces is treated as a single item. Use `--compat` to switch to POSIX whitespace-splitting with quote handling if you need the classic `xargs` behaviour.

**`{}` is auto-detected — no `-I{}` required.** If the command template contains `{}`, each item replaces the placeholder. If not, items are appended as trailing arguments. This differs from `xargs` which requires `-I{}` to enable placeholder mode.

**Shell fallback for builtins.** On Windows, commands like `echo`, `del`, `type` do not exist as standalone executables. `wargs` automatically retries via `cmd /c` if a command fails to launch. On Unix, it retries via `sh -c`. Use `--no-shell-fallback` to disable this and require standalone executables only.

**NDJSON goes to stderr.** Both `--json` (summary) and `--ndjson` (per-job streaming) write to stderr to keep stdout clean for piped child output. Redirect `2>&1` when you need to capture them.

**Parallel output is buffered by default.** With `-P`, each job's stdout and stderr are buffered and printed atomically after the job completes, preventing interleaved output. Use `--line-buffered` to have children inherit stdio directly — output appears immediately but may interleave.

**Exit code 123 means child failures, not wargs failures.** If one or more child processes exit non-zero, `wargs` exits 123. Exit 124 means `--fail-fast` triggered. Exit 125/126/127 are wargs-own errors (usage, not executable, not found).

## Getting Structured Data

**JSON summary** (`--json`, stderr) — aggregate results after all jobs complete:
```bash
cat servers.txt | wargs --json ssh {} uptime 2>results.json
```

Fields: `tool`, `version`, `exit_code`, `exit_reason`, `job_count`, `success_count`, `failure_count`.

**NDJSON** (`--ndjson`, stderr) — one JSON object per job as it completes:
```bash
cat servers.txt | wargs --ndjson ssh {} uptime
```

Each line contains: `tool`, `version`, `item`, `exit_code`, `exit_reason`, `command`, `stdout`, `stderr`.

**--describe** — machine-readable flag reference:
```bash
wargs --describe
```

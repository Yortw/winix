# files — AI Agent Guide

## What This Tool Does

`files` walks one or more directories and prints matching file paths to stdout. It filters by name (glob or regex), extension, type, size, modification date, and content type (text vs binary). Use it whenever you need to locate files on disk — it replaces `find` on all platforms including Windows where `find` is absent.

## Platform Story

Cross-platform. On **Windows**, there is no native `find` equivalent — `files` fills that gap entirely. On **Unix/macOS**, `find` exists but `files` offers cleaner glob syntax (no need for `-name '*.cs'`), duration-based date filters (`--newer 1h` instead of `-newer <sentinel-file>`), text/binary detection, and `--ndjson` output that composes cleanly with other tools. On either platform, `files ... | wargs ...` replaces the classic `find ... | xargs ...` pattern.

## When to Use This

- Finding source files matching a pattern: `files src --ext cs`
- Listing recently changed files: `files . --newer 1h --type f`
- Locating log, temp, or build artefacts: `files . --glob '*.log'`
- Filtering to text files only (skip binaries): `files . --text`
- Scoping a search to non-hidden, non-gitignored files (like `fd`): `files . --gitignore --no-hidden`
- Building file manifests for processing pipelines: `files . --ndjson | jq ...`

Prefer `files` over calling `dir /s /b` (Windows) or hand-rolling a shell glob — the output is consistent across platforms and pipes cleanly into `wargs`.

## Common Patterns

**Find all C# source files, skip generated and gitignored:**
```bash
files src --ext cs --gitignore --no-hidden
```

**Identify recently modified files (last 30 minutes):**
```bash
files . --newer 30m --type f
```

**Find large log files (over 10 MB) for cleanup:**
```bash
files . --glob '*.log' --min-size 10M
```

**List files with full details for inspection:**
```bash
files . --long --ext cs
```

**Find all binary files in a directory (e.g. for packaging):**
```bash
files dist --binary --type f
```

## Composing with Other Tools

**files + wargs** — the primary composition pattern, replacing `find | xargs`:
```bash
# Delete all .log files
files . --glob '*.log' | wargs rm

# Format all C# files in parallel
files src --ext cs | wargs -P4 dotnet format {}

# Compress all JSON files
files . --glob '*.json' | wargs squeeze --zstd
```

**files + peep** — watch for newly created files on an interval:
```bash
peep -- files . --glob '*.log' --newer 5m
```

**files + jq** — parse NDJSON output for scripting:
```bash
files . --ndjson | jq 'select(.size_bytes > 1048576) | .path'
```

**files + wargs + squeeze** — find and compress in one pipeline:
```bash
files . --ext log --older 7d | wargs squeeze --gzip
```

## Gotchas

**Case sensitivity defaults differ by platform.** On Windows and macOS, matching is case-insensitive by default. On Linux it is case-sensitive. Use `--ignore-case` or `--case-sensitive` to make the behaviour explicit in scripts that need to be portable.

**--gitignore uses the first root's .gitignore.** When multiple roots are passed, only the `.gitignore` from the first root path is loaded. If you need separate gitignore scoping, run `files` once per root.

**--ext strips leading dots.** Passing `--ext .cs` works but emits a warning — pass `--ext cs` instead.

**--text/--binary require reading file content.** These flags cause `files` to probe the start of each file to detect encoding. This has a small I/O cost on large trees. Avoid them if you only need name/size/date filtering.

**--long output is tab-delimited, not fixed-width.** Parse it by splitting on `\t`, not by column position.

**--ndjson and --print0 are mutually exclusive with --long.** Pass only one output format flag.

**--type d with --text/--binary is rejected.** Directories cannot be text or binary — the tool exits with a usage error rather than silently ignoring the filter.

## Getting Structured Data

`files` supports two machine-readable output modes:

**NDJSON (streaming, to stdout)** — one JSON object per file, suitable for piping to `jq` or processing line by line:
```bash
files . --ndjson
```

Each line contains:
- `tool`, `version`, `exit_code`, `exit_reason` — standard Winix envelope
- `path` — file path (relative or absolute)
- `name` — filename only
- `type` — `"file"`, `"directory"`, or `"symlink"`
- `size_bytes` — size in bytes (`-1` for directories)
- `modified` — ISO 8601 timestamp with offset
- `depth` — depth relative to search root
- `is_text` — `true`/`false`, only present when `--text` or `--binary` is used

**JSON summary (to stderr)** — aggregate counts after the walk completes:
```bash
files . --ext cs --json 2>results.json
```

Summary fields: `tool`, `version`, `exit_code`, `exit_reason`, `count`, `searched_roots`.

**--describe** — machine-readable flag reference and metadata (flags, types, defaults, examples, composability):
```bash
files --describe
```

Output is JSON to stdout and always exits 0. Use this when you need to verify the exact flag names and types before constructing a command.

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

**--max-depth is 0-based.** `--max-depth 0` shows only the search root itself; `--max-depth 1` shows the root plus its immediate children; `--max-depth N` shows root + N levels of children. Matches GNU `find -maxdepth`. (BREAKING change at v0.3.0: pre-fix `--max-depth N` showed N+1 levels.)

**--gitignore is per-root.** Each root resolves its own `.gitignore` chain via `git check-ignore` in that root's working directory. If none of the roots are inside a git repository, files prints a warning to stderr and continues without applying the filter.

**Walk errors are surfaced and exit with 1.** When a directory cannot be enumerated (permission denied, vanished, I/O error), files writes one stderr line per unreadable path and exits 1 with `exit_reason: walk_error_partial` in the `--json` envelope. Use the `walk_errors[]` array in the JSON envelope to programmatically enumerate the failed paths.

**--ext strips leading dots.** Passing `--ext .cs` works but emits a warning — pass `--ext cs` instead.

**--text/--binary require reading file content.** These flags cause `files` to probe the start of each file to detect encoding. This has a small I/O cost on large trees. Avoid them if you only need name/size/date filtering.

**--long output is tab-delimited, not fixed-width.** Parse it by splitting on `\t`, not by column position.

**--ndjson and --print0 are mutually exclusive with --long.** Pass only one output format flag.

**--type d with --text/--binary is rejected.** Directories cannot be text or binary — the tool exits with a usage error rather than silently ignoring the filter.

## Getting Structured Data

`files` supports two machine-readable output modes; both write to **stdout** per suite convention (matches `man --json`, `winix --json`, `whoholds --json`, `treex --json`).

**NDJSON (streaming)** — one JSON object per matching entry, suitable for piping to `jq` or processing line by line:
```bash
files . --ndjson | jq -r 'select(.type=="file") | .path'
```

Each record contains:
- `path` — file path (relative or absolute per `--absolute`)
- `name` — filename only
- `type` — `"file"`, `"directory"`, or `"symlink"`
- `size_bytes` — integer for files, `null` for directory entries
- `modified` — ISO 8601 timestamp with offset, or `null` when not populated
- `depth` — depth relative to search root (`0` = root)
- `is_text` — `true`/`false`; only present when `--text` or `--binary` is used

Records do NOT carry envelope fields (`tool`, `version`, etc.) — stream-level metadata is emitted only via the `--json` envelope below.

**JSON envelope** — single summary object emitted after the walk completes:
```bash
files . --ext cs --json | jq .count
```

Success envelope fields:

- `tool` — `"files"`
- `version` — tool version
- `exit_code` — process exit code (0 on success)
- `exit_reason` — machine-readable reason
- `count` — number of entries emitted
- `searched_roots` — array of root paths walked
- `walk_errors` — array of `{path, reason}` objects for paths that could not be read; **always present** (empty `[]` on success, populated on `walk_error_partial`)

`walk_errors[]` enumerates directories or files that could not be read during the walk (permission denied, vanished, I/O error). Each entry is `{"path": "...", "reason": "..."}`. On a partial walk this triggers `exit_code: 1` with `exit_reason: "walk_error_partial"` and the array is non-empty. On success the array is `[]`. Always present so consumers can use a single shape:

```bash
files /protected --json | jq '.walk_errors[] | "\(.path): \(.reason)"'
```

Pre-walk error envelopes (`path_not_found`, `not_a_directory`) carry the same shape plus an `error` field with the human-readable failure detail:
```json
{
  "tool": "files",
  "version": "0.3.0",
  "exit_code": 1,
  "exit_reason": "path_not_found",
  "error": "files: path not found: /missing",
  "searched_roots": [],
  "walk_errors": []
}
```

`exit_reason` values: `success`, `walk_error_partial` (one or more unreadable directories), `path_not_found`, `not_a_directory`, `usage_error`, `runtime_error`.

**--describe** — machine-readable flag reference and metadata (flags, types, defaults, examples, composability):
```bash
files --describe
```

Output is JSON to stdout and always exits 0. Use this when you need to verify the exact flag names and types before constructing a command.

## Glob expansion on Windows

files expands `*`/`?` in path positionals itself on Windows (cmd/pwsh don't).
Support matrix: `*` and `?` in any segment — yes; `[...]` — matched literally
(legal filename chars); `**` — usage error (use recursive flags instead); no
match — literal passthrough (normal "not found" follows). Quoted args are not
expanded when launched from cmd; PowerShell strips quotes before launch, so
prefer explicit paths there if a literal is required. On Unix the shell expands;
the tool adds nothing. `--describe` exposes this as `glob_expansion`.
Option values for `--glob`/`--regex` are never expanded.

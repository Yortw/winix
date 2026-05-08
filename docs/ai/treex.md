# treex — AI Agent Guide

## What This Tool Does

`treex` walks one or more directories and renders a tree-formatted listing to stdout. It supports filtering by name (glob, regex, or extension), type, size, and modification date. Use it whenever you need a visual overview of a directory structure — it replaces `tree` on all platforms and adds filtering, size rollups, gitignore awareness, and clickable terminal hyperlinks.

## Platform Story

Cross-platform. On **Windows**, the built-in `tree` is a DOS-era command with no colour, no filtering, and no sizing — `treex` fills that gap entirely. On **Linux/macOS**, `tree` exists (when installed) but lacks clickable hyperlinks, gitignore support, size rollups, and structured JSON output. On either platform, `treex` gives a consistent, modern experience.

## When to Use This

- Getting a visual overview of a project: `treex src`
- Finding large files in a tree: `treex --size --sort size`
- Reviewing what changed recently: `treex --newer 1h`
- Showing only source files with their directory context: `treex src --ext cs`
- Clean view of tracked files only: `treex --gitignore --no-hidden`
- Scoping to a fixed depth: `treex -d 2`

Prefer `treex` over `tree` for any situation involving filtering or formatting. Prefer `files` over `treex` when the goal is piping file paths into another tool — `treex` is for visual display, `files` is for programmatic use.

## Common Patterns

**Show a clean project tree (skip hidden and gitignored files):**
```bash
treex --gitignore --no-hidden
```

**Find the largest files in a directory:**
```bash
treex --size --sort size
```

**Show only C# source files:**
```bash
treex src --ext cs
```

**Inspect recent changes:**
```bash
treex --newer 1h --date
```

**Limit depth (0-based — `-d 2` shows root + 2 levels of children):**
```bash
treex -d 2
```

**Show directory skeleton only:**
```bash
treex --dirs-only
```

## Composing with Other Tools

**treex + jq** — parse NDJSON for scripting:
```bash
treex --ndjson | jq 'select(.type == "file" and .size_bytes > 1048576) | .path'
```

**treex + peep** — live-refresh tree view:
```bash
peep -- treex --gitignore --no-hidden
```

**treex vs files + wargs** — use `files` for processing pipelines, `treex` for display:
```bash
# Visual display
treex --size --sort size

# Piping file paths for processing
files . --ext log --older 7d | wargs rm
```

## Gotchas

**--gitignore is per-root.** When multiple roots are passed, each root resolves its own `.gitignore` chain via `git check-ignore` in that root's working directory. If none of the roots are inside a git repository, treex prints a warning to stderr and continues without applying the filter.

**--size triggers directory rollups.** Without `--size`, directory size columns are omitted. With `--size`, the tool computes rolled-up sizes for each directory node — this adds a second pass over the collected tree and has a small extra cost on very large trees.

**--sort size requires --size.** Sorting by size without `--size` will sort all directories as if they have no size. Pass `--size --sort size` together.

**Case sensitivity defaults differ by platform.** On Windows and macOS, matching is case-insensitive by default. On Linux it is case-sensitive. Use `--ignore-case` or `--case-sensitive` to make the behaviour explicit in scripts that need to be portable.

**--ext strips leading dots.** Passing `--ext .cs` works but emits a warning — pass `--ext cs` instead.

**Clickable hyperlinks are terminal-dependent.** Links are enabled automatically when colour is on and the terminal supports OSC 8 hyperlinks (Windows Terminal, iTerm2, most modern terminals). Pass `--no-links` to disable them if they appear as escape sequences in your output.

**--ndjson suppresses the rendered tree.** When `--ndjson` is active, all output goes to stdout as NDJSON lines; the visual tree is not drawn.

**Tree output is visual, not line-per-file.** Each line of standard output is a rendered tree node (with box-drawing characters and indentation). Do not parse standard output programmatically — use `--ndjson` instead.

## Getting Structured Data

`treex` supports two machine-readable output modes; both write to **stdout** per suite convention (matches `man --json`, `winix --json`, `whoholds --json`).

**NDJSON (streaming)** — one JSON object per node, suitable for piping to `jq` or processing line by line:
```bash
treex --ndjson | jq -r 'select(.type=="file") | .path'
```

Each record contains:
- `path` — file path relative to the search root (forward-slash separated)
- `name` — filename only
- `type` — `"file"`, `"dir"`, or `"link"`
- `size_bytes` — integer for files, `null` for directories without `--size` rollup
- `modified` — ISO 8601 timestamp with offset
- `depth` — depth relative to the root node (`0` for the root)

Records do NOT carry envelope fields (`tool`, `version`, etc.) — stream-level metadata is emitted only via the `--json` envelope below.

**JSON envelope** — single summary object emitted after the walk completes:
```bash
treex --json | jq .
```

Success envelope fields: `tool`, `version`, `exit_code`, `exit_reason`, `directories`, `files`, and (when `--size` is on) `total_size_bytes`, plus a `walk_errors` array (empty on success).

`walk_errors[]` enumerates directories or files that could not be read during the walk (permission denied, vanished, I/O error). Each entry is `{"path": "...", "reason": "..."}`. On a partial walk this triggers `exit_code: 1` with `exit_reason: "walk_error_partial"` and the array is non-empty. On success the array is `[]`. Always present so consumers can use a single shape:

```bash
treex --json /some/dir | jq '.walk_errors[] | "\(.path): \(.reason)"'
```

Error envelope (exit 1) carries the same shape plus an `error` field with the human-readable failure detail:
```json
{
  "tool": "treex",
  "version": "0.4.0",
  "exit_code": 1,
  "exit_reason": "path_not_found",
  "directories": 0,
  "files": 0,
  "error": "treex: path not found: ..."
}
```

`exit_reason` values: `success`, `walk_error_partial` (one or more unreadable directories), `path_not_found`, `not_a_directory`, `usage_error`, `runtime_error`.

**--describe** — machine-readable flag reference and metadata (flags, types, defaults, examples, composability):
```bash
treex --describe
```

Output is JSON to stdout and always exits 0. Use this when you need to verify exact flag names and types before constructing a command.

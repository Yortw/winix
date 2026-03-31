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

**Limit to two levels deep:**
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

**--gitignore uses the first root's .gitignore.** When multiple roots are passed, only the `.gitignore` from the first root path is loaded. Run `treex` once per root if separate gitignore scoping is needed.

**--size triggers directory rollups.** Without `--size`, directory size columns are omitted. With `--size`, the tool computes rolled-up sizes for each directory node — this adds a second pass over the collected tree and has a small extra cost on very large trees.

**--sort size requires --size.** Sorting by size without `--size` will sort all directories as if they have no size. Pass `--size --sort size` together.

**Case sensitivity defaults differ by platform.** On Windows and macOS, matching is case-insensitive by default. On Linux it is case-sensitive. Use `--ignore-case` or `--case-sensitive` to make the behaviour explicit in scripts that need to be portable.

**--ext strips leading dots.** Passing `--ext .cs` works but emits a warning — pass `--ext cs` instead.

**Clickable hyperlinks are terminal-dependent.** Links are enabled automatically when colour is on and the terminal supports OSC 8 hyperlinks (Windows Terminal, iTerm2, most modern terminals). Pass `--no-links` to disable them if they appear as escape sequences in your output.

**--ndjson suppresses the rendered tree.** When `--ndjson` is active, all output goes to stdout as NDJSON lines; the visual tree is not drawn.

**Tree output is visual, not line-per-file.** Each line of standard output is a rendered tree node (with box-drawing characters and indentation). Do not parse standard output programmatically — use `--ndjson` instead.

## Getting Structured Data

`treex` supports two machine-readable output modes:

**NDJSON (streaming, to stdout)** — one JSON object per node, suitable for piping to `jq` or processing line by line:
```bash
treex --ndjson
```

Each line contains:
- `tool`, `version`, `exit_code`, `exit_reason` — standard Winix envelope
- `path` — file path (relative to the root passed on the command line)
- `name` — filename only
- `type` — `"file"`, `"dir"`, or `"link"`
- `size_bytes` — size in bytes (`-1` for directories when `--size` is not used)
- `modified` — ISO 8601 timestamp with offset
- `depth` — depth relative to the root node

**JSON summary (to stderr)** — aggregate counts after the walk completes:
```bash
treex --json 2>summary.json
```

Summary fields: `tool`, `version`, `exit_code`, `exit_reason`, `directories`, `files`, `total_size_bytes` (only present when `--size` is active).

**--describe** — machine-readable flag reference and metadata (flags, types, defaults, examples, composability):
```bash
treex --describe
```

Output is JSON to stdout and always exits 0. Use this when you need to verify exact flag names and types before constructing a command.

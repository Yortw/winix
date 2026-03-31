# AI Discoverability for Winix CLI Tools

**Date:** 2026-03-31
**Status:** Proposed
**Project:** Winix (`D:\projects\winix`)

---

## Overview

Make Winix tools discoverable and usable by AI coding agents — particularly on Windows, where the CLI tool landscape is fragmented and poorly represented in LLM training data. Three complementary features, each serving a different discovery scenario:

| Feature | Serves | Discovery path |
|---------|--------|----------------|
| `--describe` flag | Agent has the tool installed, wants to learn how to use it | `timeit --describe` |
| `llms.txt` + AI guides | Agent is browsing the repo or docs site, wants to understand the suite | Read `llms.txt`, follow links |
| Per-tool AI guides | Agent needs workflow guidance beyond flag reference | Read `docs/ai/timeit.md` |

**Strategy:** Build all three for the new `files` tool first, prove the patterns, then retrofit to existing tools (timeit, squeeze, peep, wargs).

---

## 1. `--describe` Flag

### Purpose

Machine-readable structured metadata about a tool's complete interface. An AI agent runs `toolname --describe` and gets everything it needs to use the tool correctly — flags, types, defaults, examples, I/O behaviour, composability.

### Output Format

JSON to stdout. Example for `files` (illustrative):

```json
{
  "tool": "files",
  "version": "0.2.0",
  "description": "Find files by name, size, date, and type. A cross-platform find replacement.",
  "platform": {
    "scope": "cross-platform",
    "replaces": ["find"],
    "value_on_windows": "No native find equivalent; fills a major gap",
    "value_on_unix": "Cleaner glob syntax, --json output, composes with wargs"
  },
  "usage": "files [options] [paths...]",
  "options": [
    {
      "long": "--glob",
      "short": "-g",
      "type": "string",
      "placeholder": "PATTERN",
      "description": "Match filenames against glob pattern",
      "repeatable": false
    },
    {
      "long": "--newer",
      "type": "string",
      "placeholder": "DURATION",
      "description": "Files modified within duration (e.g. 1h, 30m, 7d)",
      "repeatable": false
    },
    {
      "long": "--type",
      "short": "-t",
      "type": "string",
      "placeholder": "TYPE",
      "description": "Filter by type: f (file), d (directory), l (symlink)",
      "repeatable": false
    },
    {
      "long": "--json",
      "type": "flag",
      "description": "JSON output to stderr"
    },
    {
      "long": "--help",
      "short": "-h",
      "type": "flag",
      "description": "Show help"
    }
  ],
  "exit_codes": [
    { "code": 0, "description": "Success" },
    { "code": 1, "description": "Runtime error (permission denied, invalid path)" },
    { "code": 125, "description": "Usage error" }
  ],
  "io": {
    "stdin": "not used",
    "stdout": "One file path per line (default). Null-delimited with --print0.",
    "stderr": "Errors and diagnostics. JSON/NDJSON output when --json/--ndjson."
  },
  "examples": [
    {
      "command": "files src --glob '*.cs'",
      "description": "Find all C# source files under src/"
    },
    {
      "command": "files . --newer 1h --type f",
      "description": "Files modified in the last hour"
    },
    {
      "command": "files . --glob '*.log' | wargs rm",
      "description": "Delete all log files (compose with wargs)"
    },
    {
      "command": "files . --glob '*.cs' --json 2>manifest.json",
      "description": "Build a JSON file manifest"
    }
  ],
  "composes_with": [
    {
      "tool": "wargs",
      "pattern": "files ... | wargs <command>",
      "description": "Find files then execute a command for each one (find | xargs pattern)"
    },
    {
      "tool": "peep",
      "pattern": "peep -- files . --glob '*.log' --newer 5m",
      "description": "Watch for recently created log files on an interval"
    }
  ],
  "json_output_fields": [
    { "name": "tool", "type": "string", "description": "Always \"files\"" },
    { "name": "version", "type": "string", "description": "Tool version" },
    { "name": "exit_code", "type": "int", "description": "0 = success" },
    { "name": "exit_reason", "type": "string", "description": "Machine-readable reason (success, permission_denied, usage_error)" },
    { "name": "path", "type": "string", "description": "Absolute or relative file path" },
    { "name": "size_bytes", "type": "int", "description": "File size in bytes" },
    { "name": "modified", "type": "string", "description": "ISO 8601 last modified timestamp" },
    { "name": "type", "type": "string", "description": "file, directory, or symlink" }
  ]
}
```

### Design Decisions

**Stdout, not stderr.** `--describe` is the primary output — it's data, not diagnostics. An agent runs `files --describe` and pipes/captures stdout.

**Mutually exclusive with other operations.** `--describe` emits metadata and exits. It does not combine with `--json`, `--help`, or normal operation. If both `--describe` and `--help` are passed, `--describe` wins (an agent is more likely to pass `--describe` deliberately than `--help`).

**Exit code 0 always.** `--describe` cannot fail (the metadata is compiled in). Always exits 0.

**No `--describe-format` variants.** JSON only. No YAML, no TOML, no XML. One format, universally parseable. If a future need arises, a `--describe-format` flag can be added without breaking the JSON default.

### ShellKit Implementation

The `CommandLineParser` already knows all registered flags, options, types, and descriptions. The `--describe` metadata extends this with fields the parser doesn't currently track.

New fluent builder methods on `CommandLineParser`:

```csharp
// Platform story
parser.Platform("cross-platform",
          replaces: ["find"],
          valueOnWindows: "No native find equivalent; fills a major gap",
          valueOnUnix: "Cleaner glob syntax, --json output, composes with wargs");

// I/O behaviour
parser.StdinDescription("not used")
      .StdoutDescription("One file path per line. Null-delimited with --print0.")
      .StderrDescription("Errors and diagnostics. JSON/NDJSON with --json/--ndjson.");

// Examples
parser.Example("files src --glob '*.cs'", "Find all C# source files under src/")
      .Example("files . --newer 1h --type f", "Files modified in the last hour");

// Composability
parser.ComposesWith("wargs",
          "files ... | wargs <command>",
          "Find files then execute a command for each one (find | xargs pattern)")
      .ComposesWith("peep",
          "peep -- files . --glob '*.log' --newer 5m",
          "Watch for recently created log files on an interval");

// JSON output field descriptions (for tools that support --json)
parser.JsonField("path", "string", "Absolute or relative file path")
      .JsonField("size_bytes", "int", "File size in bytes")
      .JsonField("modified", "string", "ISO 8601 last modified timestamp")
      .JsonField("type", "string", "file, directory, or symlink");
```

**`--describe` handling** follows the same pattern as `--help`/`--version`: the parser detects it during `Parse()`, sets `IsHandled = true`, writes the JSON to stdout, and the console app returns `result.ExitCode` (0). No tool-specific code needed — it's entirely automatic from the registered metadata.

**`StandardFlags()` updated** to include `--describe` alongside `--help`, `--version`, `--color`, `--no-color`, `--json`.

### What the Parser Already Knows vs What's New

| Data | Source | Already tracked? |
|------|--------|-----------------|
| Tool name, version | Constructor args | Yes |
| Description | `.Description()` | Yes |
| Usage line | `.CommandMode()` / `.Positional()` | Yes (can be derived) |
| Flags: long, short, description | `.Flag()` | Yes |
| Options: long, short, placeholder, description, type | `.Option()` / `.IntOption()` / `.DoubleOption()` | Yes |
| List options: long, short, placeholder, description | `.ListOption()` | Yes |
| Exit codes | `.ExitCodes()` | Yes |
| Platform scope / replaces | New `.Platform()` | **No — new** |
| I/O behaviour | New `.StdinDescription()` etc. | **No — new** |
| Examples | New `.Example()` | **No — new** |
| Composability | New `.ComposesWith()` | **No — new** |
| JSON output fields | New `.JsonField()` | **No — new** |

The `--describe` serialiser walks all registered definitions and the new metadata to produce the JSON. No duplication — the flag/option data used by `--help` and `--describe` comes from the same registration.

---

## 2. `llms.txt`

### Purpose

A well-known file at the repo root that gives AI agents a structured entry point to the Winix suite. Follows the [llms.txt](https://llmstxt.org/) convention: plain text/markdown, machine-readable, human-readable, describes what's here and where to find details.

### Location

`llms.txt` at the repository root (`D:\projects\winix\llms.txt`).

### Content Structure

```markdown
# Winix

Cross-platform CLI tool suite for the gaps between Windows and *nix. Native binaries (AOT-compiled .NET), no runtime required.

## Tools

- [timeit](docs/ai/timeit.md): Time a command — wall clock, CPU, memory, exit code. Replaces POSIX `time`.
- [squeeze](docs/ai/squeeze.md): Multi-format compression/decompression (gzip, brotli, zstd). Replaces `gzip`/`brotli`/`zstd`.
- [peep](docs/ai/peep.md): Watch a command on interval + re-run on file changes. Replaces `watch` + `entr`.
- [wargs](docs/ai/wargs.md): Build and execute commands from stdin. Replaces `xargs` with sane defaults.
- [files](docs/ai/files.md): Find files by name, size, date, type. Replaces `find` with glob patterns and clean output.

## Key Features for AI Agents

- Every tool supports `--describe` for structured JSON metadata (flags, types, examples, composability, platform scope)
- Every tool supports `--json` for machine-parseable output with standard fields
- Consistent exit codes across all tools (0 = success, 125 = usage error)
- Tools compose via pipes: `files ... | wargs ...` replaces `find ... | xargs ...`
- All current tools are cross-platform. Each tool's `--describe` output includes a `platform` section explaining what it replaces and its value on each OS.

## Quick Reference

Run `<tool> --describe` to get full structured metadata for any tool.
Run `<tool> --help` for human-readable help.

## Install

Available via Scoop (Windows), winget (Windows), .NET tool (cross-platform), or direct download.
See: https://github.com/Yortw/winix
```

### Maintenance

Updated whenever a tool is added. The format is simple enough to maintain by hand. Could be generated, but the content is short and the value is in curation — an agent reads this once and gets the whole picture.

---

## 3. AI Guide Docs

### Purpose

Per-tool markdown guides in `docs/ai/` that provide workflow-oriented context beyond what `--describe` metadata covers. These answer "when should I use this tool?" and "how do I combine it with other tools?" — the kind of knowledge an agent needs to make good tool choices.

### Location

`docs/ai/<toolname>.md` — one file per tool.

### Content Structure (template)

```markdown
# <toolname> — AI Agent Guide

## What This Tool Does

<2-3 sentence description focused on when an agent should reach for this tool.>

## Platform Story

<Cross-platform or Windows-only? What does it replace? Why use it on Windows vs Unix?>

## When to Use This

<Scenarios where this tool is the right choice, including "instead of X" comparisons.>

## Common Patterns

<3-5 real-world usage patterns with commands and explanations.>

## Composing with Other Tools

<How this tool pipes into/out of other Winix tools and standard Unix tools.>

## Gotchas

<Platform differences, surprising behaviour, common mistakes.>

## Getting Structured Data

<How to use --json/--ndjson and what fields to expect.>
```

### Relationship to `--describe`

| Concern | `--describe` | AI guide |
|---------|-------------|----------|
| What flags exist | Yes (authoritative) | No (don't duplicate) |
| What the JSON output looks like | Yes (field list) | No |
| When to use this vs another tool | No | Yes |
| Common workflow recipes | Partially (examples) | Yes (richer, contextual) |
| Platform gotchas | No | Yes |
| Composition patterns | Partially (composes_with) | Yes (detailed, with explanations) |

The guide complements `--describe` — it doesn't repeat it. An agent that only reads the guide gets enough to choose and use the tool. An agent that reads `--describe` gets the precise interface. An agent that reads both is well-equipped.

---

## Implementation Plan

### Phase 1: ShellKit `--describe` support

1. Add new fluent builder methods to `CommandLineParser` (stdin/stdout/stderr descriptions, examples, composability, JSON field descriptions)
2. Add `--describe` to `StandardFlags()` registration
3. Implement `GenerateDescribe()` JSON serialiser in `CommandLineParser` (walks all registered metadata)
4. Handle `--describe` in `Parse()` like `--help`/`--version` (detect, output, set `IsHandled`)
5. Tests for `GenerateDescribe()` output structure and content

### Phase 2: `files` tool uses `--describe` (proving ground)

6. `files` console app registers all metadata via the new fluent API
7. Verify `files --describe` output is correct and useful
8. Write `docs/ai/files.md` AI guide
9. Write `llms.txt` with `files` entry

### Phase 3: Retrofit existing tools

10. Add `--describe` metadata to timeit, squeeze, peep, wargs console apps
11. Write `docs/ai/` guides for each existing tool
12. Update `llms.txt` with all tool entries

### Phase 4: Ongoing

- New tools include `--describe` metadata and AI guide from day one
- `llms.txt` updated as tools are added

---

## Exit Codes

`--describe` always exits 0. It cannot fail.

---

## Testing

- Unit tests for `GenerateDescribe()` — verify JSON structure, field presence, types
- Verify `--describe` is mutually exclusive with normal operation (parser handles it before tool logic runs)
- Verify `--describe` output is valid JSON (deserialise in test)
- Verify standard fields (`tool`, `version`, `description`, `options`) are always present
- Verify optional sections (`examples`, `composes_with`, `json_output_fields`) are present only when registered

---

## Differences from `--help`

| Aspect | `--help` | `--describe` |
|--------|----------|-------------|
| Audience | Humans | Machines (AI agents, scripts) |
| Format | Formatted text | JSON |
| Output stream | stdout | stdout |
| Content | Flags, usage, exit codes | Everything in --help plus examples, I/O, composability, JSON fields |
| Stability | Display may change | Schema is versioned (fields may be added, never removed) |

---

## Open Questions

None — design is self-contained. Suite-level discovery (`winix --list --describe`) is deferred to the multi-call binary effort.

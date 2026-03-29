# Winix CLI Conventions

**Date:** 2026-03-29
**Status:** Accepted
**Project:** Winix (`D:\projects\winix`)

This document defines the conventions all Winix tools must follow. It is the single reference for "I'm adding a new tool — what rules apply?"

---

## 1. Exit Codes

### Tool's Own Errors (universal)

| Code | Meaning |
|------|---------|
| 0 | Success |
| 125 | Usage error (bad arguments, missing required input) |
| 126 | Command not executable / permission denied (tools that spawn processes) |
| 127 | Command not found (tools that spawn processes) |

### Operational Errors (tool-specific)

For tools replacing existing utilities, operational exit codes must match the original tool. For example, if `gzip` returns 1 for corrupt input, `squeeze` returns 1 for corrupt input.

For tools with no predecessor, use:
- 1 = runtime error
- 125 = usage error

For tools replacing originals that use exit code 2 for usage errors, use 2 instead of 125 to match the original's convention.

### Requirements

- Every tool's design doc must include an explicit exit code table.
- Exit codes are part of the compatibility contract — changing them is a breaking change.

---

## 2. Compatibility Strategy

### Principle

Winix tools are *better replacements*, not bug-for-bug reimplementations. Compatibility is a migration bridge, not a permanent constraint.

### Flag Compatibility

- Accept the 10-15 most common flags from the original tool as aliases to Winix's clean flags.
- Compat flags map to Winix behaviour — if the original's `-d` and Winix's `--decompress` differ in an edge case, Winix behaviour wins.
- Don't implement obscure flags nobody remembers.
- Each tool's design doc includes a compatibility table mapping original flags to Winix equivalents.

### Exit Code Compatibility

Strict. Operational exit codes match the original tool's semantics.

### Output Compatibility

- **Default:** Winix's own clean output format.
- **`--compat`:** Match the original tool's stdout/stderr text format where practical. This is a per-tool decision — only for tools where scripts parse text output (e.g. `gzip`, `tar`). Not every tool needs it (e.g. `peep` replacing `watch` — nobody parses watch output).
- **`--json`:** Always available, always Winix's schema.

### Documentation Pattern

- `--help` documents the clean interface first.
- Compatibility section lists original-tool flags and their Winix equivalents.
- Each tool's README has a "Differences from [original]" section.

---

## 3. JSON Output

### Standard Fields (every tool, every JSON response)

| Field | Type | Always present | Description |
|-------|------|----------------|-------------|
| `tool` | string | Yes | Executable name (e.g. `"timeit"`, `"squeeze"`) |
| `version` | string | Yes | From `Directory.Build.props` |
| `exit_code` | int | Yes | Tool's own status (0 = tool succeeded) |
| `exit_reason` | string | Yes | Machine-readable snake_case reason (e.g. `"success"`, `"corrupt_input"`, `"usage_error"`) |

### Conditional Standard Fields

| Field | When present | Type | Description |
|-------|-------------|------|-------------|
| `child_exit_code` | Tools that spawn subprocesses | int or null | Child process exit code. `null` if child wasn't spawned (e.g. tool errored before starting). Absent from schema for tools that don't spawn processes. |

### Value Conventions

- Field names: `snake_case`
- Durations: seconds as float, 3 decimal places (e.g. `9.100`)
- Sizes: bytes as integer (e.g. `505413632`)
- Unavailable metrics: `null` (field present, value null — consistent schema). Zero is a valid measurement, distinct from `null`.
- Booleans: JSON `true`/`false`, not strings

### NDJSON (Newline-Delimited JSON)

`--ndjson` enables streaming JSON output — one JSON object per line, emitted as results become available. Each line is a complete, self-contained JSON object following the same field conventions as `--json`.

**When to use which:**
- `--json` — complete result after the tool finishes. Best for single-result tools (timeit, squeeze) and scripts that want one parseable object.
- `--ndjson` — one line per result, streamed incrementally. Best for multi-result or long-running tools (peep, xargs, tree+) and pipe chains with `jq`.

**For single-result tools** (timeit, squeeze), `--ndjson` produces identical output to `--json` — one line. The flag exists for consistency so pipe chains work regardless of which tool is upstream.

**For multi-result tools** (peep, xargs, tree+), `--ndjson` streams each result as it happens:
```
{"tool":"peep","exit_code":0,"run":1,"child_exit_code":0,"output":"..."}
{"tool":"peep","exit_code":0,"run":2,"child_exit_code":0,"output":"..."}
{"tool":"peep","exit_code":0,"run":3,"child_exit_code":1,"output":"..."}
```

Each line includes the standard fields (`tool`, `version`, `exit_code`, `exit_reason`) plus tool-specific data. This enables incremental processing: `peep --ndjson -- kubectl get pods | jq 'select(.child_exit_code != 0)'`.

**Stream destination:** `--ndjson` writes to the same stream as `--json` (typically stderr for tools that produce data on stdout). For tools where the primary output IS the NDJSON stream (e.g. a future `tree+ --ndjson`), it goes to stdout.

**Per-tool decision:** not every tool needs `--ndjson` in v1. Single-result tools can defer it. The convention is that when `--ndjson` is offered, it follows these rules.

### Schema Stability

- Fields may be added in minor versions. Consumers should ignore unknown fields.
- Fields are never removed or change type within a major version.
- New nullable fields default to `null` (backwards-compatible addition).

### Examples

Tool that spawns a subprocess (timeit):
```json
{"tool":"timeit","version":"0.1.0","exit_code":0,"exit_reason":"success","child_exit_code":1,"wall_seconds":12.400,"user_cpu_seconds":9.100,"sys_cpu_seconds":0.300,"cpu_seconds":9.400,"peak_memory_bytes":505413632}
```

Tool that operates on data (squeeze):
```json
{"tool":"squeeze","version":"0.1.0","exit_code":0,"exit_reason":"success","input_bytes":1048576,"output_bytes":524288,"ratio":0.500,"format":"gz"}
```

Error case:
```json
{"tool":"squeeze","version":"0.1.0","exit_code":1,"exit_reason":"corrupt_input"}
```

---

## 4. CLI Argument Conventions

### Flag Naming

- Long flags: `--kebab-case` (e.g. `--no-color`, `--output-format`)
- Short flags: single character, `-x` (e.g. `-d`, `-k`, `-1`)
- Boolean flags: no value needed (`--verbose`, not `--verbose=true`)
- Value flags: space-separated (`--format gz`), though `=` form (`--format=gz`) should also be accepted

### Universal Flags (every tool)

| Flag | Short | Description |
|------|-------|-------------|
| `--help` | `-h` | Show help text |
| `--version` | | Show tool name and version |
| `--json` | | JSON output (complete object after tool finishes) |
| `--ndjson` | | Streaming NDJSON output (one JSON line per result, where applicable) |
| `--color` | | Force colour on |
| `--no-color` | | Force colour off |
| `--compat` | | Original tool output format (where applicable, per-tool decision) |

### Argument Parsing Rules

- `--` stops flag parsing — everything after is positional.
- First unrecognised argument stops flag parsing (so `squeeze file.txt` works without `--`).
- Unknown flags: error with suggestion if close match, otherwise usage error (exit 125).

### Colour Resolution Precedence

1. Explicit flag (`--color` / `--no-color`) — highest priority
2. `NO_COLOR` environment variable (no-color.org)
3. Auto-detection (is output stream a terminal?)

---

## 5. Help Text and Documentation

### `--help` Template

Every tool's `--help` follows this structure. Sections with no content are omitted.

```
Usage: <tool> [options] [args...]

<One-line description of what the tool does.>

Options:
  <clean Winix flags, grouped logically>

Compatibility:
  These flags match <original tool> for muscle memory:
  <compat flag mappings>

Exit Codes:
  0    Success
  <tool-specific codes>
  125  Usage error
```

### Required Documentation Formats

Every tool must provide:

1. **`--help`** — built-in, follows template above
2. **Man page** — generated, installable via standard `man` on Unix
3. **Machine-readable flag metadata** — structured data for CLIo intellisense and LLM consumption

The metadata format and generation pipeline are a ShellKit design concern, decided when ShellKit is extracted. The convention is that these three formats must exist.

### Per-Tool README

Located in the console app directory (e.g. `src/squeeze/README.md`):

- What it does
- Installation
- Usage and examples
- Options reference
- Differences from original tool (where applicable)
- Exit codes

---

## 6. Output Stream Conventions

### Standard Rule

Data goes to stdout. Status, diagnostics, and summaries go to stderr.

### Per-Tool Guidance

| Tool type | stdout | stderr |
|-----------|--------|--------|
| Produces data (squeeze, tar) | Compressed/processed data | Status, progress, errors |
| Wraps a command (timeit, peep) | Child inherits stdout | Tool's summary + errors |
| Displays information (tree+) | Display output | Errors |

### `--stdout` Flag

Tools that write their summary to stderr (like timeit) offer `--stdout` to redirect that summary to stdout instead. Useful when capturing the tool's output in a pipe.

### JSON Output Stream

`--json` writes to the same stream as the tool's normal summary output:
- timeit: JSON to stderr (or stdout with `--stdout`)
- squeeze: JSON status to stderr, compressed data still to stdout

This means `squeeze --json file.csv > file.csv.gz` works — JSON status on stderr, compressed bytes on stdout.

---

## 7. Project Structure

### File Layout

```
src/
  Winix.<ToolName>/              ← class library (all logic, testable)
    Winix.<ToolName>.csproj
  <toolname>/                    ← thin console app (arg parsing, call library, exit code)
    <toolname>.csproj
    Program.cs
tests/
  Winix.<ToolName>.Tests/        ← xUnit tests
    Winix.<ToolName>.Tests.csproj
```

### Rules

- All logic in the class library — formatters, core operations, platform-specific code.
- Console app is thin — argument parsing, call library, write output, set exit code.
- Class library: `IsAotCompatible`, `IsTrimmable`.
- Console app: `PublishAot`, `PackAsTool`.
- All projects reference `Directory.Build.props` for shared version, nullable, warnings-as-errors.
- Each tool added to `Winix.sln`.

### Naming Conventions

| Thing | Pattern | Example |
|-------|---------|---------|
| Class library | `Winix.<PascalCase>` | `Winix.Squeeze` |
| Console app | `<lowercase>` | `squeeze` |
| NuGet package | `winix.<lowercase>` | `winix.squeeze` |
| Test project | `Winix.<PascalCase>.Tests` | `Winix.Squeeze.Tests` |

### ShellKit Dependency

Once extracted, all class libraries reference `Yort.ShellKit` for `ConsoleEnv`, colour handling, and shared utilities. Until then, each tool inlines what it needs (as timeit does with `ConsoleEnv`).

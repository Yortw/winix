# ShellKit Argument Parser — Design Spec

**Date:** 2026-03-30
**Status:** Approved
**Related:** [CLI conventions](2026-03-29-winix-cli-conventions.md)

## Overview

Add a declarative argument parser to Yort.ShellKit that eliminates manual arg-parsing boilerplate across all Winix tools. Tools define flags/options via a fluent builder, call `Parse(args)`, and get an immutable `ParseResult` with typed access. Help text, error reporting, and standard flags (help, version, color, json) are handled automatically.

**Goals:**
- Pit of success: new tools get correct arg parsing by default
- Auto-generated help pages that stay in sync with registered flags
- Enforce Winix CLI conventions (exit codes, color precedence, JSON errors)
- Fully AOT-safe — no reflection, no trim warnings
- Migrate all three existing tools (timeit, squeeze, peep) to validate the design

**Non-goals:**
- Subcommand routing (tools are single-purpose)
- Shell completion generation (can add later)
- Config file / environment variable binding

## Why not existing parsers?

- **System.CommandLine** — effectively abandoned by Microsoft (stalled at preview for years, no path to stable release). Unstable API, heavy allocation model, complex middleware pipeline designed for large CLI frameworks.
- **Spectre.Console.Cli** — pulls in the full Spectre.Console rendering library (~500KB+ in AOT). Overkill when you only need arg parsing — Winix tools do their own ANSI output.
- **McMaster.Extensions.CommandLineUtils** — relies on reflection and attributes for model binding. Hostile to AOT trimming without extensive annotation work.
- **General** — all three target large CLI apps with subcommand trees, dependency injection, and middleware. Winix tools are single-purpose with flat flag sets. A purpose-built parser is smaller (~one file), fully AOT-safe by construction, and enforces Winix CLI conventions (exit codes, color precedence, JSON error format, output stream rules) by default.

This rationale should be documented as a comment in the parser source file.

## Core Types

### CommandLineParser (builder)

Fluent builder that registers flags, options, and metadata. Immutable after `Parse()` is called.

```csharp
var parser = new CommandLineParser("peep", version)
    .Description("Run a command repeatedly and display output on a refreshing screen")
    .StandardFlags()
    .Flag("--differences", "-d", "Highlight changed lines between runs")
    .Flag("--once", "Run once, display, and exit")
    .DoubleOption("--interval", "-n", "N", "Seconds between runs (default: 2)",
        validate: v => v > 0 ? null : "must be positive")
    .IntOption("--history", null, "N", "Max history snapshots (default: 1000, 0=unlimited)",
        validate: v => v >= 0 ? null : "must be non-negative")
    .ListOption("--watch", "-w", "GLOB", "Re-run on file changes matching glob")
    .Section("Compatibility", "...")
    .Section("Interactive", "...")
    .CommandMode()
    .ExitCodes(ExitCode.Success, ExitCode.UsageError, ExitCode.NotExecutable, ExitCode.NotFound);
```

#### Flag/option registration methods

| Method | Purpose | Value type |
|--------|---------|------------|
| `Flag(long, short?, description)` | Boolean flag, no value | bool |
| `Option(long, short?, placeholder, description, validate?)` | Single string value | string |
| `IntOption(long, short?, placeholder, description, validate?)` | Single int value, validated at parse time | int |
| `DoubleOption(long, short?, placeholder, description, validate?)` | Single double value, validated at parse time | double |
| `ListOption(long, short?, placeholder, description)` | Repeatable, collects into list | string[] |
| `FlagAlias(alias, targetOption, value)` | Short flag that expands to option+value (e.g. `-9` → `--level 9`) | - |
| `StandardFlags()` | Registers --help, -h, --version, --color, --no-color, --json | - |

#### Metadata methods

| Method | Purpose |
|--------|---------|
| `Description(text)` | Tool description for help output |
| `CommandMode()` | First non-flag arg stops parsing (for tools that run child commands) |
| `Positional(label)` | Label for positional args in usage line (e.g. "files...") |
| `Section(title, body)` | Free-form text section in help output (e.g. "Compatibility", "Interactive") |
| `ExitCodes(...)` | Exit codes shown in help and used for error reporting |

### ParseResult (immutable)

Returned by `parser.Parse(args)`. Provides typed access to parsed values.

```csharp
ParseResult result = parser.Parse(args);

if (result.IsHandled) return result.ExitCode;     // --help/--version printed
if (result.HasErrors) return result.WriteErrors(Console.Error);  // returns 125

bool diff = result.Has("--differences");
double interval = result.GetDouble("--interval", defaultValue: 2.0);
int history = result.GetInt("--history", defaultValue: 1000);
string[] watchPatterns = result.GetList("--watch");
string[] command = result.Command;                  // args after flag boundary
string[] positionals = result.Positionals;          // non-command positional args
bool useColor = result.ResolveColor();              // full precedence chain
```

#### Access methods

| Method | Returns | Notes |
|--------|---------|-------|
| `Has(name)` | bool | True if flag was present |
| `GetString(name, default?)` | string | Value of string option. Throws if not provided and no default. |
| `GetInt(name, default?)` | int | Value of int option (already validated). Throws if not provided and no default. |
| `GetDouble(name, default?)` | double | Value of double option (already validated). Throws if not provided and no default. |
| `GetList(name)` | string[] | All values for a list option (empty array if none) |
| `Command` | string[] | Args after command boundary (CommandMode only) |
| `Positionals` | string[] | Non-flag args (non-CommandMode) |
| `ResolveColor()` | bool | Applies precedence: --color/--no-color > NO_COLOR env > terminal detection |
| `IsHandled` | bool | True if --help or --version was handled (output already printed) |
| `ExitCode` | int | Exit code when IsHandled is true |
| `HasErrors` | bool | True if parse errors were detected |
| `Errors` | IReadOnlyList\<string\> | Parse error messages |
| `WriteErrors(TextWriter)` | int | Writes errors to writer, returns exit code (125). If --json is set, writes JSON error instead. |

### ExitCode (constants)

```csharp
public static class ExitCode
{
    public const int Success = 0;
    public const int UsageError = 125;
    public const int NotExecutable = 126;
    public const int NotFound = 127;
}
```

Tools with non-standard exit codes (squeeze uses 1 for compression error, 2 for usage) can use their own constants alongside these. The `usageErrorCode` parameter on `ExitCodes()` lets the tool override what `WriteErrors()` returns — squeeze passes `2`, others use the default `125`.

### StandardFlags

`StandardFlags()` registers:

| Flag | Short | Description |
|------|-------|-------------|
| `--help` | `-h` | Show help |
| `--version` | | Show version |
| `--color` | | Force colored output |
| `--no-color` | | Disable colored output |
| `--json` | | JSON output to stderr |

`ResolveColor()` on ParseResult calls `ConsoleEnv.ResolveUseColor(Has("--color"), Has("--no-color"), ConsoleEnv.IsNoColorEnvSet(), ConsoleEnv.IsTerminal(checkStdErr: false))`.

When `--help` or `--version` is encountered during parsing, the parser handles it immediately: prints help/version to stdout, sets `IsHandled = true` and `ExitCode = 0`.

## Parsing Rules

1. Iterate args left to right
2. `--` stops flag parsing; everything after goes to `Command` (CommandMode) or `Positionals`
3. If the arg matches a registered long or short flag/option: consume it (and its value if applicable)
4. If the arg starts with `-` and is not registered: add error "unknown option: --foo"
5. If `CommandMode()` is set and the arg doesn't start with `-`: stop parsing, this and all remaining args go to `Command`
6. If `CommandMode()` is not set and the arg doesn't start with `-`: add to `Positionals`
7. `FlagAlias` expansions happen during parsing — `-9` is silently treated as `--level 9`
8. For value options, if the next arg is missing: add error "--interval requires a value"
9. For typed options (int/double), if parsing fails: add error "--interval: 'abc' is not a valid number"
10. For typed options with a validate function, if validation fails: add error "--interval: must be positive"

## Help Text Generation

Auto-generated from registered metadata. Structure:

```
Usage: <tool> [options] [--] <command> [args...]    (CommandMode)
Usage: <tool> [options] [files...]                  (Positional)
Usage: <tool> [options]                             (neither)

<description>

Options:
  -s, --long-name VALUE  Description text                (auto-aligned)
  --flag-only            Another description
  ...
  --no-color             Disable colored output           (standard flags last)
  --color                Force colored output
  --json                 JSON output to stderr
  --version              Show version
  -h, --help             Show help

<custom sections in registration order>

Exit Codes:
  0    Success
  125  Usage error
  126  Command not executable
  127  Command not found
```

**Alignment:** The description column starts at a consistent position based on the widest flag+placeholder combination, with a minimum of 2 spaces gap.

**Repeatable options:** "(repeatable)" is appended to the description automatically for `ListOption`.

**Standard flags:** Always rendered last in the options block, in a consistent order.

## FlagAlias — Gzip Compat Shortcuts

Squeeze supports `-1` through `-9` as shortcuts for `--level N` (gzip compatibility). These are registered as:

```csharp
.FlagAlias("-1", "--level", "1")
.FlagAlias("-2", "--level", "2")
// ...
.FlagAlias("-9", "--level", "9")
```

During parsing, encountering `-3` is treated identically to `--level 3`. The alias is not shown in the main options table (it's a compat shortcut, not a primary flag). Aliases can be documented in a `Section("Compatibility", ...)` block.

## Error Reporting

Parse errors are collected during `Parse()` and reported via `WriteErrors()`:

**Plain text mode** (default):
```
squeeze: unknown option: --foo
squeeze: --level requires a value
```

**JSON mode** (when `--json` is set):
```json
{"tool":"squeeze","version":"0.1.0","exit_code":125,"exit_reason":"usage_error"}
```

`WriteErrors()` returns the usage error exit code (125 by default, overridable per tool). Errors are written to the provided TextWriter (typically `Console.Error`).

## Migration Plan

All three existing tools (timeit, squeeze, peep) will be migrated to the new parser:

- **timeit** — simplest case. Flags only + command mode. ~150 lines of manual parsing → ~20 lines of declaration.
- **squeeze** — value options, positional files, gzip compat aliases (-1..-9, -d, -c, -k, -v, -f). Tests the alias system and positional arg handling.
- **peep** — repeatable options, typed numeric values with validation, command mode, custom sections. Tests the full feature set.

Existing tool behaviour must be preserved exactly — same flags, same error messages (or improved), same exit codes, same help structure. The migration is a refactor, not a behaviour change. Backward compatibility with existing unix tool flags (gzip for squeeze, watch for peep) must be maintained.

## File Structure

```
src/Yort.ShellKit/
    CommandLineParser.cs     — builder + Parse() method
    ParseResult.cs           — immutable result with typed access
    ExitCode.cs              — standard exit code constants
    ConsoleEnv.cs            — existing (unchanged)
    AnsiColor.cs             — existing (unchanged)
    DisplayFormat.cs         — existing (unchanged)
```

## Testing

**CommandLineParser tests (new file in Yort.ShellKit.Tests):**
- Flag registration and Has() access
- Option with value (string, int, double)
- ListOption repeatable collection
- FlagAlias expansion
- StandardFlags registration and ResolveColor
- CommandMode: `--` boundary, first-non-flag boundary
- Positional args (non-CommandMode)
- Error: unknown flag
- Error: missing value
- Error: invalid type (int, double)
- Error: validation failure
- WriteErrors plain text and JSON mode
- Help text generation: alignment, sections, exit codes, standard flags ordering
- IsHandled for --help and --version

## Out of Scope

- Subcommand routing
- Shell completion generation
- Config file / environment variable binding
- Mutually exclusive flag groups (can add later if needed)
- Flag deprecation warnings

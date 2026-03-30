# ADR: ShellKit Argument Parser

**Date:** 2026-03-30
**Status:** Accepted
**Related:** [Design spec](2026-03-30-shellkit-arg-parser-design.md), [CLI conventions](2026-03-29-winix-cli-conventions.md)

---

## Decision 1: Build a custom parser instead of using existing libraries

### Context

Three Winix tools share identical arg-parsing boilerplate (manual for-loop, switch statement, color flags, version/help, error reporting). Several mature .NET CLI parsing libraries exist.

### Decision

Build a purpose-built declarative parser in Yort.ShellKit rather than adopting System.CommandLine, Spectre.Console.Cli, or McMaster.Extensions.CommandLineUtils.

### Rationale

- **System.CommandLine** — stalled at preview for years with no path to stable. API churn, heavy middleware pipeline.
- **Spectre.Console.Cli** — pulls in the full Spectre rendering library (~500KB+ AOT). Overkill for arg parsing alone.
- **McMaster.Extensions.CommandLineUtils** — reflection-based model binding, hostile to AOT trimming.
- All three target large CLI apps with subcommand trees. Winix tools are single-purpose with flat flag sets.
- A custom parser enforces Winix CLI conventions (exit codes, color precedence, JSON error format) by default — external libraries would require wrapping to achieve the same.

### Trade-offs Accepted

- Maintenance burden of a custom parser (mitigated by small scope — no subcommands, no middleware)
- No shell completion generation out of the box (can add later)

### Options Considered

- **Helper functions only** (keep manual loops, share utilities): doesn't enforce consistency, doesn't eliminate boilerplate. Rejected.
- **Attribute-based with source generators**: build complexity, harder to debug, overkill for 3-5 tools. Rejected.

---

## Decision 2: Fluent builder + immutable ParseResult

### Context

Need a programming model for defining and consuming parsed arguments.

### Decision

`CommandLineParser` fluent builder to define flags/options, returns an immutable `ParseResult` with typed access methods.

### Rationale

- Fluent builder is readable and discoverable (IDE autocomplete shows available methods)
- Immutable result prevents mutation bugs and is easy to test
- Typed access (`GetInt`, `GetDouble`, `GetList`) validates at parse time, not access time — errors surface early as clean usage messages
- No reflection — all types resolved at compile time, fully AOT-safe

### Trade-offs Accepted

- Multiple method variants (`Flag`, `Option`, `IntOption`, `DoubleOption`, `ListOption`) instead of a single generic method. Acceptable for 4-5 types.
- String-based name lookups on ParseResult (`Has("--json")`) rather than typed properties. Trade-off is flexibility over compile-time safety — refactoring a flag name requires updating all access sites.

---

## Decision 3: Migrate all three existing tools

### Context

Three tools already work with manual parsing. Could build the parser and only use it for new tools.

### Decision

Migrate timeit, squeeze, and peep to the new parser as part of this work.

### Rationale

- Tools haven't shipped yet — migration risk is minimal
- Existing tools are the best test cases for design validation
- Delaying migration creates permanent exceptions that grow harder to change
- Three diverse tools (flags-only, positional+aliases, repeatable+command-mode) exercise the full feature set

### Trade-offs Accepted

- Larger initial scope (parser + 3 migrations vs parser alone)
- Risk of subtle behaviour changes during migration (mitigated by existing test suites)

---

## Decision 4: FlagAlias for backward compatibility shortcuts

### Context

Squeeze supports `-1` through `-9` as gzip-compatible compression level shortcuts. These are flags that map to an option+value pair.

### Decision

`FlagAlias(alias, targetOption, value)` — registers a short flag that expands to a target option with a fixed value during parsing.

### Rationale

- Keeps compat shortcuts explicit and declarative
- Aliases don't appear in the main options table (documented via custom Section instead)
- Parser handles the expansion — tool code never sees the alias, only the resolved `--level N`

### Trade-offs Accepted

- Aliases are hidden from auto-generated help (must be documented manually in a Section). This is intentional — compat shortcuts are secondary UI, not primary.

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|-------|-------------|
| Subcommand routing | Tools are single-purpose; no current need |
| Shell completion generation | Nice-to-have, not blocking |
| Config file / env var binding | No tool currently needs it |
| Mutually exclusive flag groups | No current case requires it |
| Flag deprecation warnings | No flags to deprecate yet |

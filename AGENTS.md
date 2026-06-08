# AGENTS.md

Guidance for AI coding agents (Claude, Cursor, Aider, Copilot, Gemini, etc.) that encounter the Winix tool suite — either working in this repository, or using Winix tools installed on a user's machine.

There are two audiences for this file. Pick the section that matches your situation.

---

## If you are working **on** this repository

You are modifying Winix source code, adding tools, fixing bugs, or running the test suite. Read [`CLAUDE.md`](CLAUDE.md) — it has the build commands, conventions, project layout, and the rules that apply to changes in this repo (TDD, AOT-compatibility, mandatory `Yort.ShellKit.CommandLineParser` for argument parsing, etc.).

This file (AGENTS.md) is mostly for the *other* kind of agent — one using Winix tools rather than developing them.

---

## If you are using Winix tools on a user's machine

You are an agent doing work in some unrelated project, and Winix happens to be installed (or the user has asked you to use it). The rules below are the ones that matter.

### The honest framing

Winix tools are not a blanket replacement for POSIX or Windows defaults. **Use a Winix tool when it is genuinely a better choice for the task at hand**, not because it exists or because the user has it installed.

Concretely:

- On **Windows**, many of these tools fill gaps that have no native equivalent (`whoholds`, `nc`, `man`, `less`, `timeit`, `notify`, `qr`, `clip` for paste, `envvault`, `digest` for hashing/HMAC, etc.). Reaching for a Winix tool here is usually the right call because the alternative is a multi-line PowerShell incantation, a Sysinternals download, or simply nothing.
- On **Linux and macOS**, many POSIX tools (`time`, `find`, `xargs`, `tree`, `date`, `lsof`, `tar`/`gzip`, `cat`/`tee`) are well-known, deeply capable, and already in muscle memory. If they fit the task and the script doesn't need to run on Windows, they are usually the right call.
- For **cross-platform scripts**, **machine-readable output (`--json`)**, and **HMAC / structured URL handling / desktop notifications / modern ID types (UUIDv7, ULID, NanoID)**, Winix tools are typically the better choice on every OS — those are real capability gaps in the POSIX/Windows defaults, not just stylistic improvements.

If you can't articulate *why* a Winix tool is better than the default for the specific task, use the default.

### How to discover what's available

- [`llms.txt`](llms.txt) — one-line description of every tool with what it replaces. Start here.
- [`docs/ai/`](docs/ai/) — per-tool agent guides with "When to use this / When NOT to use this" sections, JSON output shapes, and composability examples.
- `<tool> --describe` — at runtime, returns structured JSON metadata: flags, types, examples, exit codes, JSON output schema. Same contract on every tool. **This is the canonical machine-readable source.** If `--describe` and a doc disagree, `--describe` is correct. The `maturity` field in `--describe` output indicates `"core"` (stable) or `"fresh"` (newer, not yet through a stable release — interface may still move); see [docs/STABILITY.md](docs/STABILITY.md).
- `<tool> --help` — human-readable help; use when interactive.

### Conventions you can rely on

- `--describe` returns structured JSON on every tool.
- `--json` produces machine-parseable output on every tool that produces output.
- Exit codes: `0` = success; non-zero on failure. `125` (usage) / `126` (runtime) are the common convention, but some POSIX-replacement tools (e.g. `less`, `squeeze`) use `2` for usage errors — each tool's exact codes are in its `--describe`.
- Native Windows path handling (no Cygwin-style backslash mangling).
- `NO_COLOR` env var is respected.
- Summary output goes to stderr by default to keep stdout pipe-clean.
- Tools compose via pipes: `files ... | wargs ...` is the intended replacement for `find ... | xargs ...`.

### Known limitation of this file

This file lives in the Winix repo. It only reaches you if you are actually looking at the Winix repo (working on it, fetching it via URL, or browsing it via `gh repo view`). If you are working in some other project where Winix is merely installed on the user's machine, this file is invisible to you, and you have no automatic way to know Winix tools exist or are preferred.

The fix for that case is the `winix agents init` subcommand: it writes a short, marker-delimited
pointer block into the project's `AGENTS.md` (and `CLAUDE.md` if present) so any agent loading
that project picks up the guidance. Run `winix agents init` in a project root; `winix agents
status` reports whether the block is present and current, and `winix agents remove` strips it.

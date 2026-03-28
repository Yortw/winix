# ADR: Winix CLI Conventions

**Date:** 2026-03-29
**Status:** Accepted
**Related design:** `2026-03-29-winix-cli-conventions.md`

---

## 1. Compatibility flags are aliases to Winix behaviour, not original behaviour

**Context:** Winix tools replace existing Unix utilities. Users expect familiar flags. The question is whether compat flags should match original behaviour exactly or just map to Winix's clean equivalents.

**Decision:** Compat flags are aliases. `-d` maps to `--decompress` with Winix's semantics, not the original tool's edge-case behaviour.

**Rationale:** Winix's value is being better, not identical. Bug-for-bug compatibility is expensive to maintain and defeats the purpose. The 90% case works the same; the 10% edge cases are where Winix improves.

**Trade-offs Accepted:** Scripts that depend on edge-case behaviour of original flags may see different results. Mitigated by documenting differences and providing `--compat` for output format.

**Options Considered:**
- *Strict drop-in:* rejected — too expensive, carries forward bad design decisions.
- *Best-effort per flag:* rejected — inconsistent; some flags strict, some not, hard to predict.

## 2. Output compatibility via `--compat` flag, per-tool decision

**Context:** Existing scripts may parse text output from original tools. Winix wants cleaner default output but can't break these scripts.

**Decision:** `--compat` flag matches original tool's output format. Offered on a per-tool basis — only where scripts actually parse text output (e.g. gzip, tar). Not mandated for every tool.

**Rationale:** Default output shouldn't be constrained by decades-old format decisions. `--compat` is a migration bridge. `--json` is the long-term answer for machine consumers.

**Trade-offs Accepted:** Per-tool decision means inconsistency in whether `--compat` exists. Acceptable — forcing it on tools where nobody parses output adds implementation burden for no value.

**Options Considered:**
- *Every tool gets `--compat`:* rejected — unnecessary for tools like peep/watch.
- *No `--compat`, just `--json`:* rejected — doesn't help existing scripts that parse text.

## 3. JSON uses flat schema with standard metadata fields

**Context:** JSON output needs consistency across tools for machine consumers.

**Decision:** Flat JSON (no envelope), with standard fields: `tool`, `version`, `exit_code`, `exit_reason`. `child_exit_code` for subprocess tools. Tool-specific fields alongside standard ones.

**Rationale:** Flat structure keeps `jq` usage simple (`jq .exit_code` not `jq .data.exit_code`). Standard fields give machine consumers consistent metadata without an envelope's nesting overhead.

**Trade-offs Accepted:** Tool-specific fields are mixed with standard fields. Mitigated by consistent `snake_case` naming and documented standard field list.

**Options Considered:**
- *Envelope (`{"data":{...}}`:* rejected — adds nesting friction for `jq` consumers.
- *No standard fields:* rejected — loses cross-tool consistency.

## 4. Separate `exit_code` and `child_exit_code` in JSON

**Context:** For tools that spawn subprocesses (timeit), the process exit code is ambiguous — is it the tool's status or the child's?

**Decision:** `exit_code` is always the tool's own status. `child_exit_code` is the subprocess exit code, only present in tools that spawn processes.

**Rationale:** Consistent semantics for `exit_code` across all tools. Machine consumers can always check `exit_code == 0` to know if the tool itself succeeded, regardless of what the child did.

**Trade-offs Accepted:** The process exit code (`$?`) still passes through the child's code for tools like timeit, so `$?` and `exit_code` in JSON may differ. This is documented and intentional.

## 5. Exit codes are strict compatibility

**Context:** Exit codes could follow Winix's own scheme or match original tools.

**Decision:** Operational exit codes match the original tool exactly. Tool infrastructure errors use the POSIX 125/126/127 scheme.

**Rationale:** Exit codes are the #1 thing CI scripts depend on. They're cheap to match and expensive to get wrong. Unlike output format, there's no "better" exit code scheme — the original's codes are fine.

**Trade-offs Accepted:** Each tool must research and document the original's exit codes. Small effort per tool.

## 6. Prescribed `--help` template and three documentation formats

**Context:** Help text consistency across tools matters for learnability. Multiple consumers need the information in different formats.

**Decision:** Standard `--help` template (Usage, Options, Compatibility, Exit Codes). Three required formats: `--help`, man pages, machine-readable metadata (for CLIo intellisense and LLMs).

**Rationale:** Once someone learns one Winix tool's help, they know them all. Multiple output formats serve different audiences without compromise.

**Trade-offs Accepted:** Man page generation and metadata format are deferred to ShellKit extraction. Tools built before ShellKit will need retrofitting.

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|-------|-------------|
| Machine-readable metadata format for flag data | Depends on ShellKit design and CLIo intellisense requirements |
| Man page generation pipeline | Depends on ShellKit extraction |
| `--color=always\|never\|auto` vs `--color`/`--no-color` | Current two-flag approach works; single flag with values could be added later as alias |
| Multi-call binary conventions | Deferred until individual tools are proven |

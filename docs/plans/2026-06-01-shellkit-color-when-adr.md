# ADR: ShellKit `--color=when` via general `--key=value` parsing

**Date:** 2026-06-01
**Status:** Accepted
**Context:** A 2026-06-01 suite-wide audit found 9 tools / 15 doc surfaces document `--color WHEN` (auto/always/never) while ShellKit's `--color` is a plain boolean `Flag` that doesn't parse a value. Decision taken to implement the valued surface in ShellKit (the convention git/ls/grep/fd/ripgrep use) rather than down-edit docs to the boolean form.
**Related design doc:** `docs/plans/2026-06-01-shellkit-color-when-design.md`

---

## D1 — General `--key=value`, not a color-only mechanism

- **Context:** `--color=when` needs `=`-attached value parsing; ShellKit has none today (all options are space-separated).
- **Decision:** Add GNU-style `--key=value` to the core `Parse` loop so any long option accepts it; `--color` is one consumer.
- **Rationale:** Convention-complete (GNU `=` across all flags), purely additive (space form still works), benefits every tool; `--output=foo.txt` etc. start working for free.
- **Trade-offs accepted:** Larger blast radius (core loop) and a broader — but light, behaviour-preserving — retest.
- **Options considered:** Color-only optional-value flag (rejected: one-off, leaves `--output=foo` broken, inconsistent with the emulated convention).

## D2 — Distinct `OptionalValueOptionDef` type for `--color`

- **Context:** `--color` is valid bare (=always) *and* with an enum value; `OptionDef` always requires a value.
- **Decision:** Introduce a small `OptionalValueOptionDef(LongName, ShortName, Description, AllowedValues, DefaultWhenBare)`.
- **Rationale:** Clean separation; lets `--describe` advertise allowed values without special-casing; keeps `OptionDef` semantics intact.
- **Trade-offs accepted:** One more def type + lookup branch in the parser.
- **Options considered:** Extend `OptionDef` with `Optional`/`AllowedValues`/`DefaultWhenBare` (rejected: muddies the "always takes a value" contract and the help/describe rendering).

## D3 — Keep `ResolveColor(bool)` signature; preserve exact precedence

- **Context:** All 27 tools call `ParseResult.ResolveColor(checkStdErr)`.
- **Decision:** Public signature and every call site unchanged; only internals read `--color`'s value. Precedence preserved exactly: `always`/bare → on (overrides `NO_COLOR`); `never`/`--no-color` → off; `NO_COLOR` → off; `auto`/absent → auto-detect. Tie `--color=always --no-color` → on.
- **Rationale:** Zero per-tool code changes; no behaviour regression for the 15 already-colouring tools.
- **Trade-offs accepted:** `--color`/always continues to override `NO_COLOR` env (pre-existing behaviour, deliberately kept — explicit flag beats env).
- **Options considered:** New tri-state return type / changed signature (rejected: forces 27 call-site edits and a full retest for no user benefit).

## D4 — Long options only for `=`

- **Context:** `-c=x` is ambiguous and non-standard.
- **Decision:** Only `--key=value` is `=`-split; short flags stay space-separated.
- **Rationale:** Matches GNU; avoids short-flag ambiguity.
- **Trade-offs accepted:** `-c=never` is not supported (use `--color=never` or `-c never`-style space form where a short exists). A short token like `-o=x` is a single unmatched token → `unknown option: -o=x` (pinned by test, not silently reinterpreted).
- **Options considered:** Split `=` on short flags too (rejected: ambiguous, unconventional).

## D5 — `=value` on a boolean flag is an error

- **Context:** `--verbose=true` is meaningless for a valueless flag.
- **Decision:** Reject with a usage error, except the optional-value `--color`.
- **Rationale:** Flags are boolean; silent acceptance would hide a user mistake.
- **Trade-offs accepted:** None material.
- **Options considered:** Accept `--flag=true/false` (rejected: scope creep; flags stay valueless).

## D6 — Allowed values `auto`/`always`/`never` only

- **Decision:** `--color` accepts exactly `auto`, `always`, `never` — **case-sensitive** (lowercase only; `--color=Always` is rejected). Duplicate occurrences → last-wins.
- **Rationale:** Covers the suite; matches the common subset across git/ls/grep/fd, which are also case-sensitive on the WHEN value.
- **Trade-offs accepted:** No `ansi`/forced-ANSI variant; `--color=Always` (mixed case) is a usage error rather than leniently accepted.
- **Options considered:** git's extra values (rejected: unneeded).

## D7 — `--no-color` kept permanently as a boolean alias for `never`

- **Decision:** Retain `--no-color` as a plain boolean flag (= `never`).
- **Rationale:** Back-compat for every existing usage + `NO_COLOR` muscle memory; harmless alongside `--color=never`.
- **Trade-offs accepted:** Two ways to say "off" (`--no-color`, `--color=never`) — acceptable redundancy.

## D8 — ShellKit-first rollout; `--help`/`--describe` auto-update; only the 15 `--color WHEN` surfaces get a finite doc fix

- **Decision:** Ship the ShellKit change first (surface only — emits no colour itself). `--help`/`--describe` regenerate the canonical `--color[=auto|always|never]` for all tools on rebuild. Hand-written docs: the ~18 bare-`--color`/`--no-color` READMEs are **already correct and untouched**; the **15 `--color WHEN` surfaces (9 tools)** get a finite `WHEN` → `=WHEN` fix (they imply a space-separated value, but the implementation is equals-only) done **in the colour sweep**. README/man edits are repo commits, **not** tool re-ships. No open-ended "migrate over time" debt and no 27-tool reship.
- **Rationale:** Decouples the parser surface from emit work; the only hand edits are 15 finite, scheduled clarifications, not a vague rolling migration the team might forget.
- **Trade-offs accepted:** Shipped nuget/scoop README copies for un-rebuilt v0.3.0 tools stay ahead of their shipped (boolean-only) binary until that tool next releases — self-healing, pre-existing drift, no new debt.
- **Options considered:** (a) Lazy rebuild-time doc migration of all tools (rejected — open-ended, forgettable, and unnecessary since bare-`--color` docs are already correct). (b) Mass reship of all 27 (rejected — user constraint, and unneeded).

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|---|---|
| Short-flag `=` support (`-c=x`) | Ambiguous, non-standard, not needed (D4). |
| `--flag=true/false` for boolean flags | Flags stay valueless (D5). |
| Mass re-ship / mass doc-edit of all 27 tools | Not needed — only the 15 `--color WHEN` surfaces get a finite `=WHEN` fix in the sweep; bare-`--color` READMEs already correct; `--help`/`--describe` auto-update (D8). |
| Emit-fixes for trash/hcat/wargs + end-to-end colour regression tests | Separate colour-sweep plan; this is the parser surface only. |
| `--color=ansi` / forced-ANSI value | Not needed for the suite (D6). |

# ShellKit `--color=when` via general `--key=value` parsing — design

**Date:** 2026-06-01
**Status:** Approved (brainstorm complete; pending implementation plan)
**Component:** `Yort.ShellKit` — `CommandLineParser`, `ParseResult.ResolveColor`, `ConsoleEnv.ResolveUseColor`
**Blast radius:** all 27 Winix tools (shared infrastructure) — but designed to be purely additive with **zero per-tool code changes**.

## 1. Motivation

A suite-wide audit (2026-06-01) found two colour problems:
- **Doc drift:** 9 tools / 15 surfaces document `--color WHEN` (auto/always/never), but ShellKit's `--color` is a plain boolean `Flag` — the valued form does not parse.
- **(Separate, handled elsewhere)** three tools (trash, hcat, wargs) claim colour but emit none — fixed in the follow-up colour sweep, not here.

`--color[=auto|always|never]` is the convention used by every tool Winix emulates (git, ls, GNU grep, fd, ripgrep). The decision (user, 2026-06-01) is to **implement that surface in ShellKit** rather than down-edit the docs to the boolean form. This makes the existing `--color WHEN` docs *correct* and gives the whole suite the GNU `=` syntax users expect.

The three colour *behaviours* already exist today (auto = default/TTY-detect, always = `--color`, never = `--no-color`/`NO_COLOR`). This change is about the **surface syntax**, not new capability.

## 2. Scope decision: general `--key=value`, not color-only

ShellKit currently has **no** `=`-attached parsing anywhere — every option takes its value from the next arg, space-separated. We add GNU-style `--key=value` to the **core parse loop** so *any* long option accepts it (`--output=foo.txt`, `--field=3`, `--ext=.cs`, `--color=never`), while space-separated forms keep working unchanged. `--color` is then just one consumer of this general capability.

Rationale: convention-complete (matches GNU across all flags, not a one-off), purely additive, benefits every tool. Rejected alternative: a narrow optional-value mechanism for `--color` only — leaves `--output=foo` broken and inconsistent with the convention the suite emulates.

## 3. Parser change — `CommandLineParser.Parse`

When an arg `StartsWith("--")` **and** contains `=`, split on the **first** `=` into `key` + `attachedValue` before the existing flag/option routing:

- **`Option` / `IntOption` / `DoubleOption` / `ListOption`** matched by `key`: use `attachedValue` instead of consuming `args[i+1]`. Same type-validation/validator path as the space-separated branch (so `--field=abc` → "not a valid integer", `--field=3` validated, etc.). The value may itself contain `=` (split on first only): `--output=a=b.txt` → value `a=b.txt`.
- **Boolean `Flag`** matched by `key` with an attached value → usage error: `<flag> takes no value`. (Exception: the optional-value `--color`, see §4.)
- **Unknown** `--key=…` → existing `unknown option: --key` error (report the key, not the whole token).
- **Long options only.** A short token like `-c=x` is **not** `=`-split — short flags stay space-separated (`-c x`). Avoids the `-c=x` ambiguity; matches GNU (short opts use `-cvalue`/`-c value`, long use `--key=value`).
- `--` separator and `CommandMode` are unaffected (the `=` check is only reached for args starting with `--` that are not the bare `--`).

Empty attached value (`--output=`) is permitted and passed through as `""` — equivalent to a space-separated empty value; the option's own validator (if any) decides. For `--color=` the enum validation rejects it (§4).

**Edge contracts (pinned by tests):** `--=x` (empty key) → `unknown option: --`. A value with a leading `=` (`--key==v`) splits on the first `=` → value `=v`, passed through verbatim. **Duplicate options → last-wins** (`--output=a --output=b` → `b`); list options **append** (both kept). A value that looks like a flag (`--output=--foo`) is taken literally as the value — a deliberate advantage of the `=` form over the space form.

## 4. `--color` as an optional-value flag

A new lightweight definition type, `OptionalValueOptionDef` (distinct from `OptionDef`, which always requires a value):

```
OptionalValueOptionDef(string LongName, string? ShortName, string Description,
                       string[] AllowedValues, string DefaultWhenBare)
```

Behaviour for `--color` (`AllowedValues = {auto, always, never}`, `DefaultWhenBare = always`):
- bare `--color` → resolved value `always`
- `--color=auto` / `--color=always` / `--color=never` → that value
- `--color=<other>` or `--color=` (empty) → usage error: `--color: '<x>' is not one of: auto, always, never`
- absent → unset (ResolveColor treats unset as `auto`)
- **case-sensitive:** `--color=Always` is rejected (matches GNU `ls`/`grep`); values must be lowercase `auto|always|never`.
- **duplicate → last-wins:** `--color=always --color=never` resolves to `never`.

`ParseResult` exposes the resolved value (e.g. `GetOptionalValue("--color")` returning `string?`: the explicit value, `DefaultWhenBare` if bare, or `null` if absent). Bare-vs-absent is distinguishable. `Has("--color")` continues to return `true` whenever the flag is present (bare or valued), so the standard-flag detection in `GenerateHelp`/`GenerateDescribe` and any `Has`-based checks keep working.

Rationale for a distinct type over extending `OptionDef`: "bare-allowed + default + enum-validated" doesn't fit `OptionDef`'s "always takes a value" contract; a focused type keeps `--describe` able to advertise allowed values without special-casing. Rejected alternative: add `Optional`/`AllowedValues`/`DefaultWhenBare` fields to `OptionDef` — fewer types but muddies existing option semantics and the describe/help rendering.

## 5. `ResolveColor` rework — public signature unchanged (the back-compat keystone)

All 27 tools call `ParseResult.ResolveColor(bool checkStdErr)`. **That signature and every call site stay exactly as-is.** Only ShellKit internals change: `ResolveColor` reads `--color`'s resolved value and applies the current precedence, preserved exactly:

1. `--color=always` **or** bare `--color` → **on** (continues to override `NO_COLOR`, matching today's `colorFlag` precedence)
2. `--color=never` **or** `--no-color` → **off**
3. `NO_COLOR` env set → **off**
4. `--color=auto` **or** `--color` absent → **auto-detect** (`ConsoleEnv.IsTerminal(checkStdErr)`)

Tie `--color=always --no-color` → **on** (preserves current "colorFlag wins" behaviour in `ConsoleEnv.ResolveUseColor`). `--no-color` remains a plain boolean `Flag` meaning `never`, kept permanently for back-compat and `NO_COLOR`-style muscle memory.

`ConsoleEnv.ResolveUseColor` either gains a tri-state-aware overload or `ResolveColor` maps the value to the existing `(colorFlag, noColorFlag, …)` bools (`always|bare → colorFlag=true`; `never → noColorFlag=true`; `auto|absent → both false`). Mapping to the existing helper is preferred (smaller change, reuses tested precedence logic).

## 6. `--describe` / `--help` representation — fixes the AI-discoverability gap for free

`StandardFlags()` registers `--color` via the new optional-value type. `GenerateHelp` renders it as `--color[=auto|always|never]`; `GenerateDescribe` emits `type: "optional-value"` with an `allowed_values` array (`["auto","always","never"]`) and a `default_when_bare` (`"always"`) field. Because both are generated from `StandardFlags`, **every tool's `--help` and `--describe` show the correct form on next rebuild with no per-tool edits**, and the 15 hand-written `--color WHEN` README/man surfaces become accurate rather than needing a down-edit.

## 7. Error handling

- Invalid `--color` value → exit 125, clear message listing allowed values.
- `=value` on a boolean flag → exit 125 (`<flag> takes no value`).
- Unknown `--key=value` → exit 125 (`unknown option: --key`).
- Empty value on a plain string option → permitted (`""`), parity with space-separated empty value.
- `--help` / `--version` / `--describe` with an attached value (`--help=x`) → `takes no value` error (exit 125), same as any boolean flag — the malformed token does **not** trigger help/version/describe. The handled-flag-vs-error ordering is otherwise unchanged from today (a well-formed `--help` is handled before error reporting).

## 8. Testing (`Yort.ShellKit.Tests`)

- **`=`-parsing per option kind:** string (`--output=x`), int (`--field=3` ok; `--field=abc` → error; validator still runs), double, list (`--ext=.cs --ext=.txt` collects both; mixed with space form), value-containing-`=` (`--output=a=b`), empty value (`--output=`).
- **Boolean flag with `=value`** → error.
- **Unknown `--key=value`** → unknown-option error naming the key.
- **`--color` matrix:** bare→always; `=auto|always|never`; `=bad`→125; `=`(empty)→125; absent→unset; `GetOptionalValue` semantics.
- **Precedence:** always>never>NO_COLOR>auto; `--color=always --no-color` tie → on; bare `--color` overrides `NO_COLOR`; `--color=never` and `--no-color` agree.
- **Back-compat regression (critical):** existing space-separated forms (`--output x`, `--field 3`, `--ext .cs`, bare `--color`, `--no-color`) parse identically to before. A representative existing tool's arg-parsing tests must still pass unchanged.

## 9. Rollout

This change is ShellKit-only and ships first. It does **not** by itself emit any colour — it only fixes the flag *surface*.

Hand-written doc surfaces after this change:
- The **~18 tools using the bare `--color` / `--no-color` form** are **already correct** (bare `--color` = always still works; `--no-color` still works) — **no edits, ever**.
- The **15 surfaces / 9 tools that say `--color WHEN`** (READMEs: digest, ids, hcat, trash, notify, url, mksecret; man: digest, ids, notify, protect, hcat, mksecret, trash, unprotect) need a small `WHEN` → **`=WHEN`** fix: they imply a space-separated value, but the implementation is **equals-only** (`--color=never`), so a user typing `--color never` literally would get `always` + a stray positional. This is a **bounded, definite** edit done **in the colour sweep** (task #15) — and it is a repo doc commit, **not** a tool re-ship.
- `--help` / `--describe` regenerate the canonical `--color[=auto|always|never]` for **every** tool automatically on rebuild (the authoritative/AI-discoverable surface).

There is **no 27-tool re-ship and no open-ended "migrate over time"** debt: the only hand edits are the 15 finite `WHEN`→`=WHEN` clarifications. Shipped nuget/scoop README copies for un-rebuilt v0.3.0 tools self-heal on that tool's next release; the GitHub repo docs are correct as soon as the sweep commits land.

The follow-up **colour sweep** (separate design/plan) additionally wires emit for trash/hcat/wargs and adds end-to-end colour regression tests. The `wargs` emit-fix (a v0.3.0-shipped defect) rides v0.4.0.

## 10. Out of scope / deferred

| Topic | Why deferred |
|---|---|
| Short-flag `=` (`-c=x`) | Ambiguous; GNU short opts don't use `=`; not needed. Long-options-only. |
| `--flag=true/false` for boolean flags | Flags stay valueless; `=value` on a boolean is an error. |
| Mass re-ship / mass doc-edit of all 27 tools | Not needed. Only the 15 `--color WHEN` surfaces get a finite `=WHEN` fix (in the sweep); the ~18 bare-`--color` READMEs stay correct untouched; `--help`/`--describe` auto-update covers AI discovery. README/man edits are repo commits, not re-ships. |
| Emit-fixes (trash/hcat/wargs) + colour regression tests | Separate colour-sweep plan; this design is the parser surface only. |
| `--color=ansi`/`always-ansi` (git's extra value) | Not needed; `auto/always/never` covers the suite. |

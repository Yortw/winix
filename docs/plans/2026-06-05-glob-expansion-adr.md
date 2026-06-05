# ADR: Windows Glob Expansion for Winix Positional Arguments

**Date:** 2026-06-05
**Status:** Accepted
**Context:** cmd.exe and PowerShell do not expand glob patterns before launching
native executables, so multi-file Winix tools receive literal `*.dll` and fail.
Suite-wide fix in `Yort.ShellKit` (matcher half landed 2026-05-31 via the
`GlobMatcher` move, commit `c26344f`).
**Design doc:** `2026-06-05-glob-expansion-design.md`

---

## Decision 1: Parser-integrated opt-in (`ExpandGlobPositionals()` on `CommandLineParser`)

**Context.** Expansion must be opt-in (subcommand verbs and cron expressions
legitimately contain `*`), and quoting attribution requires knowing each
positional's original argv index — matching by string value is ambiguous when a
positional duplicates an option value (`-o foo foo`).

**Decision.** A fluent builder method on `CommandLineParser`; `Parse()` performs
expansion; `ParseResult.Positionals` arrives pre-expanded. Optional
`skipFirst: n` parameter reserved for subcommand tools.

**Rationale.** The suite convention is that the parser owns cross-cutting CLI
behaviour precisely so tools cannot drift (`--color`, `--describe`, error
formats, exit codes all live there). Glob expansion is the same class. Only the
parser has the positional→argv-index mapping needed for correct quoting
attribution. One-line adoption; impossible to forget the call after opting in;
`--help`/`--describe` advertise the capability automatically.

**Trade-offs accepted.** `Parse()` gains a filesystem dependency. Contained: the
engine is a separate class reached through injectable seams (existing
`ResolveColorCore` seam precedent), and the parser already performs I/O
(help/describe printing).

**Options considered.**
- *Post-parse static helper* (`GlobArgs.Expand(parsed.Positionals)`): parser stays
  pure, but quoting attribution is ambiguous (no argv indices), six call sites
  can drift or be forgotten — the defect class ShellKit exists to kill — and
  `--describe` can't know about it. Rejected.
- *Hybrid (parser records indices, extension method expands)*: correct
  attribution and a pure parser, but a two-piece forgettable API and
  `ParseResult` metadata serving a single feature. Rejected.

---

## Decision 2: Syntax surface — `*`/`?` any segment; `[...]` always literal; `**` loud error

**Context.** Concern raised that partial glob support ("some globs work, others
surprise you") could be worse than none.

**Decision.** Support `*` and `?` in any path segment. Never treat `[...]` as a
pattern. Reject `**` with a specific usage error pointing at alternatives.

**Rationale.** `*`/`?` are illegal in Windows filenames, so any arg containing
them is unambiguously a pattern and is guaranteed to fail today — expansion is
therefore monotonic (only always-failing inputs change behaviour; nothing that
works can regress). `[`/`]` are legal filename chars (`report[1].txt` — browser
download dedup), so auto-detecting character classes would misread real paths: a
false-positive class with no safe detection; exclusion is correct permanently.
Any-segment support kills the "wildcard in a directory segment" surprise cliff.
`**` from argv means unbounded tree walks (bash gates it behind `globstar`);
agents type `**/*.cs` reflexively, so a loud documented error beats a confusing
silent "not found".

**Trade-offs accepted.** Not full bash parity; the boundary must be documented
(README/man/docs-ai/--help/--describe). `**` users get an error rather than
results.

**Options considered.**
- *Last-segment-only `*`/`?`*: simpler walker, but leaves a documented-only cliff
  at directory segments. Rejected — the walker cost is modest.
- *Full bash-ish incl. `[...]` and `**`*: `[...]` is actively unsafe on Windows;
  `**` cost/surprise unjustified for v1. Rejected.
- *`**` silent passthrough*: one rule, but agents get an unhelpful failure.
  Rejected in favour of the loud error.

---

## Decision 3: Honour quoting via raw-command-line tokenizer; fail open on misalignment

**Context.** On Unix, quoting is how users suppress globbing. .NET argv cannot
see quoting; `Environment.CommandLine` (the raw line) can. Prior art: Rust's
`wild` crate (ripgrep).

**Decision.** Tokenize the raw command line with CRT-compatible rules; expand
only positionals whose token was unquoted. If tokenizer output does not align
with `args.Length`, fail open: expand everything.

**Rationale.** True bash-equivalence where the shell makes it possible
(cmd passes the typed line verbatim), and the cleanest doc story. Retrofitting
quoting later would be a silent behaviour change in shipped tools — doing it now
avoids that. Fail-open is chosen because a quoted glob falling back to expansion
is benign (a `*`-bearing literal can never name a real file; no-match passes the
literal through), whereas silently disabling expansion is an invisible feature
outage — the worse failure mode.

**Trade-offs accepted.** PowerShell re-builds the command line and strips
unneeded quotes, so quote suppression does not work from pwsh (only `--%`);
documented, with the nil-practical-impact argument. Git Bash may re-expand a
bash-quoted literal; same argument. Tokenizer must track CRT rules exactly —
pinned by a self-referential integration test (our tokens == .NET's argv).

**Options considered.**
- *Ignore quoting entirely*: simpler, but a hard-to-retrofit asymmetry. Rejected.
- *Seam-only (design for quoting, wire "never quoted" in v1)*: defers the
  asymmetry without removing it. Rejected.
- *Fail closed (no expansion) on misalignment*: preserves the quoting contract
  but converts tokenizer gaps into silent feature outages. Rejected.

---

## Decision 4: No-match → literal passthrough (bash nullglob-off)

**Context.** What happens when a pattern matches nothing?

**Decision.** Pass the literal token through unchanged.

**Rationale.** Matches bash's default; preserves today's "not found" error
exactly (monotonicity); guarantees `Positionals` never silently shrinks, so
existing "no files given" validation in tools keeps working.

**Trade-offs accepted.** Tools report "not found: *.tmp" rather than a
glob-specific message. Acceptable — same as bash.

**Options considered.** *nullglob (drop the token)*: silently changes positional
arity; breaks validation. *failglob (error)*: stricter than bash default;
hostile to "delete if present" scripting (`trash *.tmp`). Both rejected.

---

## Decision 5: bash/Git-Bash parity for match semantics (dotfiles, attributes, dirs+files)

**Context.** cmd and bash disagree about hidden files and dotfiles.

**Decision.** Leading-dot entries are not matched by `*`/`?` at segment start
(bash rule); hidden/system **attributes** are ignored (attribute'd files match);
both files and directories match; trailing separator restricts to directories.

**Rationale.** The suite's promise is "same script, same result on both
platforms" — Git Bash on Windows already behaves this way, so this is the
consistent target. Attribute filtering would be a silent data-dependent
divergence.

**Trade-offs accepted.** cmd users may be surprised that `*` matches
`desktop.ini`-class hidden files (cmd's `dir *` hides them). Documented.

**Options considered.** *cmd parity (filter hidden attributes)*: diverges from
the Unix side and from Git Bash; data-dependent surprises. Rejected.

---

## Decision 6: Windows-only gate

**Context.** Unix shells expand before we run; expanding again could double-apply
to quoted literals deliberately passed through.

**Decision.** The entire feature is gated on `OperatingSystem.IsWindows()`
(injectable for tests). On Unix the parser is byte-identical with or without the
opt-in.

**Rationale.** Never double-expand; never alter Unix behaviour. Idempotency on
Windows-under-Git-Bash falls out (already-expanded args contain no metachars).

**Trade-offs accepted.** None of substance.

**Options considered.** *Expand everywhere*: breaks Unix quoted-literal
semantics. Rejected.

---

## Decision 7: v1 adoption scope — digest, squeeze, trash, less, treex, files

**Context.** Per-tool adoption costs ~30–60 min each (opt-in + cmd/pwsh smokes +
docs). Positional shapes vary wildly across the suite.

**Decision.** Adopt the six path-positional tools in this pass; subcommand tools
and command-mode tools do not adopt.

**Rationale.** These six are where users actually hand multiple files. `files`
specifically called out as important (Troy). Subcommand tools (schedule/qr/url/
winix) have verb positionals (and cron `*` args!) — they shape the API
(`skipFirst`) but must not adopt blindly. Command-mode tools' child args are not
ours to touch. Closes open trash finding #6 as a side effect.

**Trade-offs accepted.** Single-path tools (man, whoholds, hcat) don't benefit
yet; opportunistic later.

**Options considered.** *Core four (no treex/files)*: cheaper, but files was
explicitly wanted. *ShellKit-only, adopt later*: ships a feature nothing uses.
Both rejected.

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|---|---|
| `**` recursive expansion | Unbounded tree walks from argv; bash gates behind `globstar`; loud error reserves the syntax for a future opt-in if demand appears |
| Option/list-option value expansion | Values like `--include "*.cs"` are tool-interpreted patterns; expanding them would corrupt semantics. Revisit only with a concrete case |
| Subcommand-tool adoption (`skipFirst`) | API parameter reserved; no subcommand tool has a compelling multi-file positional today |
| Single-path tool adoption (man, whoholds, hcat serve) | Little value now; one-line opt-in whenever next touched |
| pwsh quote-suppression beyond `--%` | Not fixable from our side — pwsh discards the user's original quoting before we run |
| `[...]` character classes | Permanent exclusion by design (legal filename chars), recorded here so it isn't re-proposed as a "gap" |

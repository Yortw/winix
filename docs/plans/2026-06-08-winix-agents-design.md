# Design: `winix agents <verb>` — project-level agent discoverability

**Date:** 2026-06-08
**Status:** Accepted (brainstorm complete; ready for implementation plan)
**Author/context:** Brainstormed from Recommendation 4 of the agent-adoption hardening review.
**Related:**
- [Agent-adoption hardening design](2026-06-06-agent-adoption-hardening-design.md) — Rec 4 is the parent
- [AI Discoverability ADR](2026-03-31-ai-discoverability-adr.md)
- [CLI Conventions](2026-03-29-winix-cli-conventions.md)
- Companion ADR: [2026-06-08-winix-agents-adr.md](2026-06-08-winix-agents-adr.md)

---

## TL;DR

`winix agents <verb>` writes a short, clearly-delimited **managed pointer block** into a
project's agent-instruction file(s) so that an AI agent working in *that* project — not the
Winix repo — discovers that the Winix suite is installed, learns the honest "when to prefer
it" stance, and is told where to get authoritative, version-matched detail. It is the
productised version of the manual pointer a consumer otherwise has to hand-write.

Three verbs: `init` (write/refresh), `remove` (strip), `status` (report drift). The block is
idempotent (marker-delimited, replace-or-append) and carries the binary's version so it can
emit a **version-pinned URL** to the repo's `AGENTS.md` that matches exactly what is installed.

---

## Background — the gap this closes

The suite's own `AGENTS.md` already states the problem (line 52–54): it "only reaches you if
you are actually looking at the Winix repo." An agent doing work in some unrelated project
where Winix is merely *installed* has no automatic way to know Winix tools exist or are
preferred. Every such project needs a manual "use Winix here" step.

`AGENTS.md` names the intended fix literally — a `winix init` subcommand — and flags it as
"on the roadmap but not yet shipped." This design is that fix, with the naming refined to
`winix agents <verb>` (a noun-group, so `remove`/`status` can be real subcommands and `init`
is free for any future non-AI setup).

### The visibility constraint (the design's crux)

The naive content — "see `llms.txt` and the `docs/ai/` guides" — **reproduces the very gap
the feature exists to close.** Those files live in the Winix repo; an agent sitting in
`some-customer-app/` cannot read them. The only Winix surfaces such an agent can reliably
reach are:

1. **The installed binaries** — `winix list`, `<tool> --help`, `<tool> --describe`.
2. **A URL** it can fetch over the network (most coding agents have web access).

So the block must (a) carry the behaviour-changing essentials *inline* (no fetch, no run
required — works even for an offline agent), and (b) point onward to authoritative detail via
a runtime command **and** a fetchable URL, never via a repo-relative file path.

---

## The managed block

Canonical content. `{version}` is the running binary's version; the URL path component is
`v{version}` for a stable release and `main` when the version string contains `-` (dev /
pre-release — see "Version-pinned URL" below).

```markdown
<!-- winix:start v={version} — managed by `winix agents init`; edits between markers are overwritten -->
## Winix CLI tools (installed on this machine)

Prefer a Winix tool only when it's genuinely the better choice for the task — not by
default. If you can't say why it beats the platform default (`find`, `time`, `tree`,
`date`, PowerShell, …), use the default.

- **What's installed, flags, JSON shapes:** `winix list` and `<tool> --describe`
  (structured JSON — authoritative for this machine).
- **Full guidance (when to prefer each tool, what it replaces):**
  https://github.com/Yortw/winix/blob/v{version}/AGENTS.md
- Conventions: every tool has `--describe` + `--json`; exit 0 = success, 125 = usage
  error, non-zero on failure (per-tool codes in `--describe`); summaries go to stderr so
  stdout stays pipe-clean; `NO_COLOR` respected.
<!-- winix:end -->
```

> **Exit-code wording (adversarial-review F4):** the block states the convention that holds
> for *every* tool, so it must not enumerate a specific runtime code. `winix` itself uses a
> richer scheme (126 = no package manager, 127 = internal error — see `WinixExitCode`); the
> generic "non-zero on failure, per-tool codes in `--describe`" line is true for winix and
> every other tool. Pin this exact line with a `RenderBlock` test.

Design notes:

- **Inline core changes behaviour without any fetch or command.** The "prefer the default
  unless you can articulate why" restraint is the same honest framing `AGENTS.md` is built
  on — the block must not become a blanket "always use Winix."
- **The runtime pointer (`winix list` / `--describe`) is ground truth for *this machine*** —
  it reflects exactly what is installed, never stale.
- **The URL is authoritative for *judgement detail*** (when to prefer each tool) and is
  fetchable by a web-capable agent. It targets `AGENTS.md` (prose "when to prefer what"),
  not `llms.txt` (terse one-line catalogue), because the block's job is conveying judgement.
- **HTML-comment markers** are invisible in rendered Markdown, trivially greppable for
  in-place replacement, and the `v=` token lets `status` detect drift by parsing the marker
  alone — no need to regenerate-and-diff the whole block.

---

## Verbs

### `winix agents init`

Write or refresh the managed block.

- **Idempotent.** If a block (identified by the markers) already exists in a target file,
  replace everything between the markers in place. If no block exists, append one (separated
  by a blank line from existing content).
- **Always emits the current binary's version** in the marker and URL, so re-running after a
  `winix update` refreshes the version-pinned URL.
- `--dry-run` prints what would be written (per file) without touching disk.

### `winix agents remove`

Strip the managed block (markers and everything between) from every target file.

- Leaves the rest of each file untouched.
- If a file is left completely empty, **leave it as an empty file — never delete a user's
  file.** The cost (a stray empty `AGENTS.md`) is harmless and far cheaper than the
  least-surprise violation of deleting a file we cannot prove we created.
- `--dry-run` previews.

### `winix agents status`

Report, per target file: block present? at what version? current vs stale vs absent.

- **Evaluates the same target set `init` would write** (per the option-B rules + `--claude`):
  every applicable file must carry a current block. If `CLAUDE.md` exists but lacks a block
  while `AGENTS.md` has a current one, that is **drift** — `status` reports per-file detail
  and the overall exit code is the worst case across the set.
- **Exit 0 = a block is present *and* matches the current binary version in every applicable
  target.**
- **Exit 1 = absent or stale** in any applicable target (present at a different version, or
  missing from a file that `init` would write). Enables the idiomatic bootstrap line:
  ```bash
  winix agents status || winix agents init   # ensure the pointer is present & current
  ```
- `--json` emits the structured form for tooling.

---

## Write targets

Default behaviour (option B — "write the standard file, plus any agent-instruction file the
project already uses"):

- **`AGENTS.md` always** — created if absent. It is the emerging cross-vendor standard
  (Cursor, Codex, Aider, etc.) and thematically matches the `agents` subcommand.
- **`CLAUDE.md` additionally, *only if it already exists*** in the target directory. This
  guarantees the pointer lands in a file the project's agents actually read, without
  manufacturing a `CLAUDE.md` in projects that deliberately don't have one.
- **`--claude`** forces `CLAUDE.md` even when absent.

Rationale: it is reliably known that Claude Code loads `CLAUDE.md`; whether current Claude
Code also auto-loads `AGENTS.md` when a `CLAUDE.md` is present is *not* assumed. Option B is
robust either way — a Claude-centric project (has `CLAUDE.md`) gets the block in `CLAUDE.md`;
a cross-tool project gets it in `AGENTS.md`; most get both.

---

## Flags & exit codes

Flags live on the single `winix` `CommandLineParser` and are `agents`-scoped (mirrors how
`--via` and `--dry-run` are install-scoped on the same parser today):

| Flag | Verbs | Meaning |
|---|---|---|
| `--path <dir>` | all | Operate on a project directory other than CWD (default: CWD). |
| `--dry-run` | init, remove | Preview the change without writing. |
| `--claude` | init, remove, status | Include `CLAUDE.md` even when absent. |
| `--json` | status (and init/remove result) | Structured output (suite convention). |
| `--help` / `--version` / `--describe` / `--no-color` | — | Standard, via `StandardFlags()`. |

`--file <name>` (arbitrary target file) was considered and **cut as YAGNI** — `--claude`
plus the auto-detect cover the realistic cases; `.cursorrules` / `GEMINI.md` support can be
a later additive change if demand appears.

Exit codes (reuse `WinixExitCode`):

| Code | Constant | Meaning |
|---|---|---|
| 0 | `Success` | `init`/`remove` succeeded; `status` present & current. |
| 1 | `ToolFailure` | `status` drift: block absent or stale. |
| 125 | `UsageError` | Bad verb, unknown flag, invalid argument. |
| 127 | `InternalError` | File I/O failure (permission denied, etc.). |

---

## Version-pinned URL — invariants & guards

The block embeds `https://github.com/Yortw/winix/blob/v{version}/AGENTS.md`. For that to
always resolve:

| # | Invariant | Enforcement |
|---|---|---|
| A | Every stable binary's reported version `X.Y.Z` has a matching tag `vX.Y.Z`. | **Free by construction** — stable binaries are built *by* the tag-triggered `release.yml` (`/p:Version=X.Y.Z`). Version ⇔ tag automatically. |
| B | `AGENTS.md` exists at repo root in every tagged commit (the URL's target path). | Standing requirement; add a guard test asserting the file exists. |
| C | The URL-builder maps version → URL correctly: stable → `v{version}`; pre-release/dev (`-` in string) → `main`. | New code + unit test. Reuses the existing "no `-` = stable" predicate that winget manifest generation already keys on. |

The binary knows its own version (`Cli.GetVersion()`), so it can emit a version-matched URL
that a hand-written pointer never could. The `main` fallback covers dev builds whose version
(`0.4.0-dev`) has no tag.

**Live-resolution guard (pipeline):** a `post-publish.yml` step that, after the tag is
pushed, fetches `https://raw.githubusercontent.com/Yortw/winix/v{version}/AGENTS.md` and
asserts HTTP 200. (The block shows the human-friendly `blob/` URL; the pipeline checks the
equivalent `raw` URL, which returns clean bytes for a status check. WebFetch tolerates the
`blob/` form for agents.) This fails the release loudly if the URL contract ever breaks.

---

## Architecture

- **`src/Winix.Winix/AgentsManager.cs`** — the testable core. Responsibilities: resolve
  target files (CWD/`--path` + the option-B detection), parse markers, replace-or-append
  merge, render the block from the version-interpolated template, and the drift comparison
  for `status`. Pure logic with injected file I/O (a small read/write/exists seam) so the
  merge and drift logic are unit-testable without touching disk, and the disk-touching paths
  are exercised by a temp-dir integration test.
- **`Cli.cs`** — add `"agents"` to the command whitelist; a new `RunAgentsAsync` dispatches
  on `positionals[1]` ∈ {`init`, `remove`, `status`} (unknown verb → `UsageError` with a
  message listing the valid verbs). The block template lives as a `const`/static in the
  class library (static prose + `{version}` substitution — **no manifest coupling**, since
  the tool list is delegated to the runtime `winix list` pointer rather than embedded).
- **`Program.cs`** — unchanged; the existing shim already forwards `args`/stdout/stderr into
  `Cli.RunAsync`.

`winix` stays single-parser, so `winix agents` does **not** get its own `--describe` surface
(consistent with `install`/`update`/etc.). The existing `winix --describe` envelope expands
to cover the new command, flags, and examples; its contract snapshot is regenerated.

---

## Error handling & edge cases

- **Unwritable target** (permission denied, read-only FS): catch the I/O exception, emit a
  one-line `winix: …` message (never a raw framework `ex.Message` under
  `InvariantGlobalization` — use the type discriminator / `SafeError.Describe` per suite
  convention), return `InternalError` (127).
- **Malformed/partial markers** (start without end, or nested): treat as "no valid block" —
  `status` reports absent; `init` appends a fresh block rather than corrupting the file;
  document this in the AI guide. (A start-without-end is the realistic hand-edit failure;
  appending is safe and the user can delete the stray start marker.)
- **Atomic per-file write (adversarial-review F1):** the file-I/O seam writes via a sibling
  temp file on the same volume followed by `File.Move(overwrite)`, so a crash or `Ctrl+C`
  mid-write leaves the user's existing file intact (a plain `File.WriteAllText` truncates in
  place and would lose their content). Multi-file `init`/`remove` is still **not**
  transactional across files — each file is individually safe, but a failure on the second
  target leaves the first updated; the failure message names the file that failed (F3).
- **First-match marker semantics (adversarial-review F6):** the *first* `<!-- winix:start … -->`
  … `<!-- winix:end -->` pair is the managed block. A literal start marker that a user places
  in their own prose (e.g. a fenced code block documenting this feature) will be treated as
  the block; a second real block is left orphaned. This is a documented limitation, pinned by
  a test so the behaviour is intentional, not accidental. Full code-fence-aware disambiguation
  is out of scope for v1.
- **Malformed version token (adversarial-review F5):** the `v=` parser terminates the version
  at whitespace or at `--` (the start of `-->`), so a hand-mangled marker like `v=-->` yields a
  clean version (or null) rather than a garbage non-null value that `status` would render as
  `stale (v-->)`. Worst case is a benign re-`init`; no data loss.
- **Existing block at a *newer* version than the binary** (a downgrade): `status` reports
  stale (drift in either direction); `init` rewrites to the binary's version.
- **`--path` pointing at a non-directory / non-existent dir:** `UsageError` (125) with a
  clear message.
- **CRLF vs LF:** preserve the target file's existing line endings when merging; default to
  the platform convention when creating a new file.
- **Trailing newline / spacing:** ensure exactly one blank line separates the appended block
  from prior content; idempotent re-runs must not accumulate blank lines.

---

## Testing strategy

- **Marker parse / merge** (pure): no block → append; existing block → replace in place;
  malformed start-only → append fresh, no corruption; idempotent re-run is byte-stable.
- **Drift detection**: same version → current (exit 0); different version → stale (exit 1);
  absent → exit 1. Include the **negative/invariant** case (a re-`init` at the same version
  leaves the file byte-identical — asserts what must *not* change).
- **URL builder**: stable version → `v{version}` URL; `-`-bearing version → `main` URL.
- **Write targets**: `AGENTS.md`-only when no `CLAUDE.md`; both when `CLAUDE.md` exists;
  `--claude` forces `CLAUDE.md` creation.
- **Exit codes**: each path (`Success`/`ToolFailure`/`UsageError`/`InternalError`) pinned.
- **Seam-failure**: an injected failing file-writer surfaces a clean one-line error and 127,
  not a stack trace (per `feedback_ship_readiness_seam_failure_tests`).
- **Integration (temp dir)**: real `init` → `status` (exit 0) → `remove` → `status` (exit 1)
  round-trip on disk.
- **`--describe` contract**: regenerate the `winix` snapshot in `tests/Winix.Contract.Tests`.
- **Repo guard**: `AGENTS.md` exists at repo root (invariant B).

---

## Docs & bookkeeping

- Update `AGENTS.md` line 52–54 and `docs/plans/2026-06-06-agent-adoption-hardening-design.md`
  Rec 4: "winix init" → "winix agents init" (the roadmap note becomes "shipped").
- `src/winix/README.md` — add the `agents` subcommand (verbs, flags, exit codes, the block).
- `src/winix/man/man1/winix.1` (check for a `.1.md` pandoc source first per CLAUDE.md) — add
  the subcommand.
- `docs/ai/winix.md` — document `agents` and the managed-block contract.
- `llms.txt` — note that `winix` now self-installs its own discoverability pointer.
- `src/winix/CHANGELOG.md` — `Added` entry for `winix agents`.
- `post-publish.yml` — the live-URL HTTP-200 guard step.

---

## Out of scope / deferred

| Item | Why deferred |
|---|---|
| `--file <name>` arbitrary target | YAGNI; `--claude` + auto-detect cover realistic cases. Additive later. |
| `.cursorrules` / `GEMINI.md` / `.github/copilot-instructions.md` native targets | Rare; can be added as additive flags without redesign. |
| Embedding the full tool list inline in the block | Delegated to the runtime `winix list` pointer to avoid manifest coupling and staleness. |
| Per-subcommand `--describe` for `winix agents` | `winix` is single-parser by design; one envelope covers it. |
| Interactive prompt before overwriting a hand-edited block | The marker contract ("edits between markers are overwritten") + `--dry-run` are sufficient; a prompt would break non-interactive bootstrap use. |

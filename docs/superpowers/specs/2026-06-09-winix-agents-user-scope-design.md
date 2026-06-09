# `winix agents` — user-scope discoverability pointer

**Date:** 2026-06-09
**Status:** Approved (design) — pending implementation plan
**Branch:** `feature/agents-user-scope` (off `release/v0.4.0`)
**Affects:** `src/Winix.Winix/AgentsManager.cs`, `src/Winix.Winix/Cli.cs`, docs, contract snapshot, smokes

## 1. Problem

`winix agents init` writes a marker-delimited Winix discoverability block into a project's
committed `AGENTS.md` / `CLAUDE.md`, and the rendered block asserts a machine-local fact:

```
## Winix CLI tools (installed on this machine)
```

`AGENTS.md` / `CLAUDE.md` are normally committed and shared. The moment the repo is cloned by
anyone without winix installed, that assertion is false. Concretely this breaks three ways:

1. **False on other machines** — "installed on this machine" is only true on the machine that ran `init`.
2. **Hostile to OSS / teams** — committing "prefer Winix tools" imposes winix on every contributor.
3. **Per-project toil** — the pointer must be re-run in every repo individually.

**Root cause:** the block's *content* is already machine-scoped — it delegates "what's installed"
to the runtime `winix list` pointer and pins its URL to the running binary's version
(`AgentsManager.cs:38-40`). Only the *file it is written into* is project-scoped. That mismatch is
the defect.

## 2. Decision

Make the discoverability pointer **user/machine-scoped by default**, written into the user's global
agent-config homes — run once per machine, true for that machine, never committed to any repo.
Retain project scope as an explicit opt-in for teams that have deliberately standardized on winix,
with wording weakened to a *conditional* so a shared file makes no false claim.

This is a **pre-release change**: `agents` shipped on `release/v0.4.0`, which is **not yet tagged**,
so changing the default project→user requires no deprecation cycle.

## 3. Scope model

Two modes. **User is the default.**

| Mode | Trigger | Targets | Header / claim |
|------|---------|---------|----------------|
| **User** (default) | `winix agents init` | `~/.claude/CLAUDE.md`, `~/.codex/AGENTS.md` — each written **only if its parent dir exists** (or is force-created; §5) | **Asserts** availability: `## Winix CLI tools (available on this machine)` |
| **Project** (opt-in) | `winix agents init --project` | `<dir>/AGENTS.md` + `<dir>/CLAUDE.md` (existing `ResolveInitTargets` rules: AGENTS.md always; CLAUDE.md if it exists or `--claude`) | **Conditional**: `## Winix CLI tools (if available in your environment)` + conditional lead sentence |

Dir-exists gating means user scope writes only to agent homes you actually use: `~/.codex/`
absent ⇒ Codex target silently skipped. This is the same "write each target only when applicable"
pattern already in `ResolveInitTargets` (`AgentsManager.cs:227`), generalized from one optional
target to a table of known homes.

## 4. CLI surface

```
winix agents <init|status|remove>      # user scope by default
  --project                            # select project scope (committed files, conditional wording)
  --path DIR                           # project directory (default: cwd); meaningful only with --project
  --claude                             # force the Claude home: user scope → create ~/.claude/CLAUDE.md
                                       #   even if ~/.claude is absent; project scope → include CLAUDE.md
                                       #   even when it does not exist (today's meaning, preserved)
  --codex                              # force the Codex home: create ~/.codex/AGENTS.md even if ~/.codex absent
  --dry-run                            # show what would be written, write nothing (unchanged)
  --json                               # JSON envelope on stdout (unchanged)
```

**Edge — user scope, no known home exists, no force flag:** write nothing, emit
`winix: no agent home found (use --claude or --codex to create one)` on stderr, exit non-zero
(`UsageError`, 125). Never guess where to create files.

**Validation:** `--path` without `--project` is a usage error (`--path` only means "project dir").
`--project` + `--codex` is a usage error (Codex is a user-home concept, not a project file).

## 5. Home resolution (cross-platform)

Resolve `~` via `Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)` — `%USERPROFILE%`
on Windows, `$HOME` on *nix. Represent known homes as a small internal table so adding a third
agent later is a one-row change:

```csharp
internal readonly record struct AgentHome(string Id, string Dir, string File);
// e.g. ("claude", "~/.claude", "CLAUDE.md"), ("codex", "~/.codex", "AGENTS.md")
```

`ResolveUserTargets(opts, fs)` returns the homes to act on: every home whose `Dir` exists, plus any
home named by a force flag (`--claude`/`--codex`) regardless of dir existence. For a force-created
home, the parent dir is created on write (the atomic temp-then-move writer in
`DefaultAgentsFileSystem.WriteAllText` already needs the dir to exist — add a `Directory.CreateDirectory`
on the resolved dir before the temp write; gate it behind the seam so tests stay disk-free).

## 6. Wording divergence (single source of truth)

The substantive body — the `winix list` / `<tool> --describe` pointer, the version-pinned URL, and
the conventions bullet — is **identical** across both modes. Only two lines differ:

- **Header line:** `(available on this machine)` (user) vs `(if available in your environment)` (project).
- **Lead sentence:** user asserts the tools are present and gives the "prefer only when better" rule
  directly; project prefixes it with "If Winix tools are available in your environment, …".

`RenderBlock(version)` becomes `RenderBlock(version, RenderMode mode)` where
`enum RenderMode { UserScope, ProjectScope }`. Tests pin both rendered variants byte-for-byte.

## 7. Behaviour of `status` / `remove`

- `status` (user scope) reports state per resolved user home; exit 0 only when every applicable home
  carries a current block, else `ToolFailure` (1) — same all-current semantics as today. When **no**
  known home exists (nothing to report on), `status` exits `ToolFailure` (1) with
  `winix: no agent home found` — "nothing is set up" is a not-current state, not success, so the
  `status || init` bootstrap idiom still triggers an `init`.
- `remove` (user scope) strips the block from every known user home that exists and contains a block.
- Both honour `--project` to operate on a repo instead.
- The `status --path . || init --path .` bootstrap idiom in the README is replaced by the user-scope
  equivalent (`winix agents status || winix agents init`).

## 8. Back-compat / migration

No deprecation needed (untagged pre-release). Users who ran the pre-release **project** mode and
committed a block can remove it with `winix agents remove --project`. Document this one-liner in the
README migration note and the CHANGELOG entry for the first stable v0.4.0 ship.

## 9. Surfaces to update

- **Code:** `AgentsManager` (home table, `ResolveUserTargets`, `RenderMode`, force-create dir,
  empty-home error), `Cli.cs` (parse `--project`/`--codex`, scope dispatch, validation).
- **Docs:** `src/winix/README.md` (`agents` section + examples + exit codes), winix man page
  (run `git ls-files '*.1.md'` first; edit the `.md` source if one exists, else the rendered `.1`),
  `docs/ai/winix.md`, `llms.txt`.
- **Contract:** regenerate the winix `--describe` snapshot in `tests/Winix.Contract.Tests`.
- **Smokes:** update the `agents` cases in the native `run-smokes.sh` fixture +
  `.github/workflows/manual-smoke.yml`.
- **Tests:** user-home resolution (dir-exists gating), force-flag dir creation, empty-home error,
  both wording variants byte-pinned, `--project` parity with prior behaviour, `--path`-without-
  `--project` and `--project`+`--codex` usage errors.

## 10. Out of scope (explicitly deferred)

- **`prefer_when` in `--describe`** (memory: `project_winix_prefer_when_describe`). Adjacent but
  separate; not folded in here.
- **Additional agent homes beyond Claude/Codex** (Cursor, etc.) — the home table makes these a
  one-row addition when a real need appears; not built speculatively (YAGNI).

## 11. Doc↔behaviour reconciliation (ship gate)

Before "done": enumerate every user-facing claim across `winix --help`/`agents --describe`, the
README `agents` section, the man page, `docs/ai/winix.md`, and `llms.txt`, run the command that
demonstrates each, and hunt for the claim that is false. This class (a documented-but-unwired
surface) has bitten this repo repeatedly — the reconciliation is the verification oracle, not a
write-it-after artifact.

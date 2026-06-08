# ADR: `winix agents <verb>` — project-level agent discoverability

**Date:** 2026-06-08
**Status:** Accepted
**Context:** Implements Recommendation 4 of the agent-adoption hardening review. The Winix
suite is invisible to an AI agent working in a project where Winix is merely installed; the
suite's own `AGENTS.md` names the intended fix and flags it unshipped. This ADR records the
decisions taken in the 2026-06-08 brainstorm.
**Design doc:** [2026-06-08-winix-agents-design.md](2026-06-08-winix-agents-design.md)

---

## Decision 1 — Subcommand naming: `winix agents <verb>` (noun-group)

- **Context.** `AGENTS.md` named the fix `winix init`. The pointer block is removable and
  re-runnable, so the feature is more than a one-shot "init."
- **Decision.** Use a noun-group command `winix agents` with verbs `init` / `remove` /
  `status`, not a bare `winix init`.
- **Rationale.** `remove` and `status` want to be first-class subcommands, not flags on an
  `init`. The `agents` noun pairs naturally with `AGENTS.md` and signals the audience. It
  also leaves a future bare `winix init` free for any non-AI project setup.
- **Trade-offs accepted.** Slightly more typing than `winix init`; one extra dispatch level.
- **Options considered.** `winix init` (rejected: no room for remove/status as verbs);
  `winix init-ai` / `winix register-ai` (rejected: verb-y names don't extend to a verb set).

## Decision 2 — Block references runtime command + version-pinned URL, never a repo file path

- **Context.** The obvious content ("see `llms.txt` / `docs/ai/`") points at repo files an
  agent in another project cannot read — reproducing the exact gap the feature closes.
- **Decision.** The block carries behaviour-changing essentials *inline*, and points onward
  to detail via (a) the runtime `winix list` / `<tool> --describe` and (b) a fetchable URL
  to the repo `AGENTS.md`. No repo-relative file path appears in the block.
- **Rationale.** The only Winix surfaces a foreign-project agent can reach are the installed
  binaries and the network. Inline core works even offline; the two pointers serve
  tool-capable and web-capable agents respectively.
- **Trade-offs accepted.** The URL assumes web access; the inline core must stay small but
  complete enough to change behaviour alone.
- **Options considered.** Self-contained snapshot only (rejected: goes stale, no ground
  truth); runtime-pointer only (rejected: useless to a read-only agent); URL only (rejected:
  fails offline). Chose the hybrid.

## Decision 3 — Version-pinned URL (`/v{version}/`), not `/main/`

- **Context.** The URL can pin to the installed binary's version or float on `main`.
- **Decision.** Pin to `v{version}` for stable releases; fall back to `main` only when the
  version string contains `-` (dev / pre-release).
- **Rationale.** The binary knows its own version, so it can serve docs that match exactly
  what is installed — something a hand-written pointer cannot. Skew between installed tools
  and newer docs is eliminated for stable users.
- **Trade-offs accepted.** Three invariants must hold (tag exists, file at root, correct
  builder predicate) plus a pipeline guard — versus zero release-process coupling for
  `main`. Accepted because the invariants are cheap (A is free by construction; B is a
  one-line guard test; C reuses the existing winget stable/dev predicate).
- **Options considered.** `/main/` only (rejected: version skew; the maintainer explicitly
  chose pinning after seeing the cost).

## Decision 4 — Write targets: `AGENTS.md` always + `CLAUDE.md` if present (option B)

- **Context.** Claude Code reliably loads `CLAUDE.md`; `AGENTS.md` is the cross-vendor
  standard; whether Claude Code auto-loads `AGENTS.md` alongside `CLAUDE.md` is uncertain.
- **Decision.** Always write/create `AGENTS.md`; additionally write `CLAUDE.md` only when it
  already exists; `--claude` forces it.
- **Rationale.** Guarantees the pointer lands in a file the project's agents actually read,
  without manufacturing a `CLAUDE.md` where the user chose not to have one. Robust regardless
  of the Claude-Code-reads-AGENTS.md uncertainty.
- **Trade-offs accepted.** Two managed blocks to maintain in dual-file projects.
- **Options considered.** `AGENTS.md` only (rejected: may miss Claude-centric projects);
  both always (rejected: intrusive — creates `CLAUDE.md` against the user's choice).

## Decision 5 — `status` exit code is drift-sensitive (0 = present & current, 1 = absent/stale)

- **Context.** `status` can be purely informational (always 0) or a scriptable gate.
- **Decision.** Exit 0 only when the block is present and matches the current version;
  exit 1 when absent or stale.
- **Rationale.** Enables `winix agents status || winix agents init` as a one-line bootstrap
  and lets CI gate on pointer freshness.
- **Trade-offs accepted.** A non-zero exit from an introspection command can surprise; the
  `--json` output and a clear stderr line mitigate.
- **Options considered.** Always-0 informational (rejected: loses the scriptable gate).

## Decision 6 — `remove` never deletes the file; idempotent marker-delimited merge

- **Context.** Removing the block may leave an empty file; merging must be re-runnable.
- **Decision.** `remove` strips only the marker-delimited block and leaves the file (even if
  empty). `init` replaces between markers in place, or appends if absent — byte-stable on
  re-run.
- **Rationale.** Deleting a file we cannot prove we created violates least-surprise; a stray
  empty file is harmless. HTML-comment markers with an embedded `v=` give cheap in-place
  replacement and drift detection without regenerate-and-diff.
- **Trade-offs accepted.** Possible stray empty `AGENTS.md` after `remove`.
- **Options considered.** Delete-if-empty (rejected: risk of deleting user content if our
  empty-detection is wrong); regenerate-and-diff for drift (rejected: marker `v=` is cheaper).

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|---|---|
| `--file <name>` arbitrary target | YAGNI; additive later if demand appears. |
| Native `.cursorrules` / `GEMINI.md` / Copilot targets | Rare; additive flags, no redesign needed. |
| Embedding the full tool list inline | Delegated to runtime `winix list` to avoid manifest coupling/staleness. |
| Per-subcommand `--describe` for `winix agents` | `winix` is single-parser by design. |
| Interactive overwrite confirmation | Marker contract + `--dry-run` suffice; a prompt would break non-interactive bootstrap. |
| Code-fence-aware marker disambiguation (F6) | First-match-wins is documented + test-pinned; full fence parsing is scope creep for v1. A literal `<!-- winix:start -->` in user prose will be treated as the block. |
| Transactional multi-file `init`/`remove` (F3) | Per-file writes are atomic (temp + `File.Move`); cross-file rollback is not. A second-file failure leaves the first updated, with the failed file named. Acceptable for v1. |
| Unit test for invariant B (`AGENTS.md` at repo root) (F9) | The design's testing strategy listed a repo-root guard test; the plan cut it (repo-root discovery from the test bin dir is fragile). Enforcement moves to the release-time HTTP-200 check (`post-publish.yml`, stable releases only). **This is a recorded plan↔design divergence**, not a silent omission — a `main`-pinned pre-release block is unverified until the next stable tag. |

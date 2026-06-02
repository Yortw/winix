# ADR — Suite-wide resource-key leak remediation

**Date:** 2026-06-02
**Status:** Accepted
**Context:** Suite-wide audit (2026-06-02) found that ~24 tools' test csprojs guard the framework-exception resource-key leak class with the wrong flag, and ~43 source sites pipe framework `ex.Message` to user output, producing SR resource keys in shipped binaries. Cosmetic/diagnosability severity. See design doc `2026-06-02-resource-key-leak-design.md`.

---

## Decision 1 — Centralise the fix in a shared `Yort.ShellKit.SafeError` helper (hybrid)

- **Context:** 43 leak sites across ~18 tools, mostly the same handful of framework exception types.
- **Decision:** Add `SafeError.Describe(Exception)` to ShellKit; type-map common CoreLib exceptions to English, fall back to `ex.GetType().Name`, never return `ex.Message`. Route the common LEAK sites through it; leave genuinely-good tool-specific text (socket/HRESULT/errno) bespoke.
- **Rationale:** One tested place for the mapping logic; becomes the enforceable convention; uses the suite's own lowest shared layer rather than 43 hand-rolled variants. "Hybrid" avoids degrading already-good bespoke messages.
- **Trade-offs accepted:** New ShellKit public surface; a shared dependency that all tools now lean on for error text.
- **Options considered:** Per-site bespoke fixes (rejected: 43 variations to keep consistent, no enforceable convention). Pure-shared for all sites (rejected: would flatten good tool-specific text like socket/errno messages).

## Decision 2 — Mirror `UseSystemResourceKeys=true` on test csprojs (NOT InvariantGlobalization)

- **Context:** The leak reproduces only when the runtime feature switch `UseSystemResourceKeys` is on. Prior memory misattributed it to `InvariantGlobalization`; test csprojs that set only `InvariantGlobalization=true` have a decorative guard.
- **Decision:** Add `UseSystemResourceKeys=true` to every test csproj (mirroring its app csproj), keeping `when`'s `InvariantGlobalization=false`. This makes the leak reproduce in-process under `dotnet test`.
- **Rationale:** Reproducer-verified on demux (the JIT test host leaked the instant the flag was added). Without it the regression tests resolve to English and guard nothing.
- **Trade-offs accepted:** Any existing test asserting framework English text will break and need its assertion updated.
- **Options considered:** Keep InvariantGlobalization-only + rely on native smokes (rejected: no per-commit guard, reproduces the original blind spot). A custom runtime-config test harness (rejected: over-engineered; the csproj flag is the native mechanism).

## Decision 3 — Test density: helper-exhaustive + one regression test per distinct leak-class per tool

- **Context:** 43 sites; many are near-duplicate wirings, some are hard to trigger deterministically (network/walk loops).
- **Decision:** Exhaustively unit-test `SafeError`. Add one regression test per *distinct* leak-class per tool through the production seam (e.g. treex → regex test + walk-IO test). Untriggerable sites rely on helper tests + code inspection, noted per tool.
- **Rationale:** Centralised logic is tested where it lives; per-tool tests prove the wiring (closing the "claimed-but-unwired" gap) without 43 near-duplicates.
- **Trade-offs accepted:** Sibling sites in the same class within a tool are guarded by one test + code review, not individually.
- **Options considered:** Every triggerable site (rejected: degrades to "triggerable subset" anyway, heavy). Helper-only (rejected: reproduces the wiring gap that started this).

## Decision 4 — No v0.3.x re-ship

- **Context:** ~30 of the 43 leaks are in tools already shipped in v0.3.0.
- **Decision:** Fix the source for all tools, but do not cut an emergency v0.3.x release. Fixes land on `release/v0.4.0` and ride out as each tool next ships.
- **Rationale:** Severity is cosmetic; the cost/risk of re-releasing already-shipped binaries exceeds the benefit of fixing error-path text faster.
- **Trade-offs accepted:** Shipped v0.3.0 binaries keep the ugly keys on rare error paths until their next release.
- **Options considered:** v0.4-tools-only fix (rejected by user: wants source clean everywhere). Full suite + v0.3.x patch release (rejected: not worth it for cosmetics).

## Decision 5 — Native smoke fixtures for the 4 v0.4 tools + convention

- **Context:** The `run-smokes.sh` + `manual-smoke.yml` native-binary harness covers the 23 v0.3 tools; the 4 v0.4 tools were never added.
- **Decision:** Author `run-smokes.sh` for mksecret/trash/hcat/demux, add them to `manual-smoke.yml`, and codify both the smoke fixture and the `UseSystemResourceKeys` test-csproj mirror in the CLAUDE.md new-tool checklist.
- **Rationale:** Closes the immediate v0.4 gap and prevents recurrence for future tools.
- **Trade-offs accepted:** `manual-smoke.yml` stays `workflow_dispatch` (on-demand), not auto-on-PR.

---

## Decision 6 — Broad-catch sites get type-name-only diagnosability (adversarial-review F2)

- **Context:** Review F2 argued that routing broad `catch (Exception)` through `SafeError.Describe` (→ `GetType().Name` for unmapped types) hides real bugs and loses detail.
- **Decision:** Accept type-name-only output for broad-catch sites; do NOT build a verbose/detail channel this version.
- **Rationale:** It is not a regression. On the shipped AOT binary the status quo is already a keyed/thin `ex.Message`, and `Message` never carried a stack trace. `GetType().Name` is the same ACCEPTABLE shape the audit already blessed for the 24 ACCEPTABLE sites. A real exception still surfaces with a non-zero exit code and its type name. Full detail is recoverable by re-running under JIT / non-AOT (where `Message` resolves to English).
- **Trade-offs accepted:** A genuine programming bug caught by a broad catch prints e.g. `"NullReferenceException"` and looks intentional; the message text is gone.
- **Options considered:** Verbose `error_detail` field / debug-flag stack emission (deferred — YAGNI for cosmetic scope; revisit if a real diagnosis is hampered).

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|---|---|
| Auto-run native smokes on every PR | `manual-smoke.yml` stays on-demand; wiring native AOT publish into per-PR CI is a separate cost/CI-time decision. |
| `Cli.Run` seam retrofit for schedule/nc/retry/wargs/peep | Tracked separately (`cli-seam-retrofit-backlog`); orchestration-heavy, its own scoped effort. Some of their leak sites live in Program.cs and will be fixed in place regardless. |
| Reclassifying COMException / native-message sites as strict-ACCEPTABLE | Left SAFE; their `.Message` is native-OS text, not a CoreLib key. One cheap probe (Final task Step 4) validates the whole class. (F12) |
| Verbose detail channel for broad-catch sites | Type-name-only this version (Decision 6); recover via JIT re-run. Build a detail channel only if a real diagnosis is blocked. (F2) |
| Friendly mapping for long-tail exception types | `SafeError` falls back to `GetType().Name` (no message). Extend mappings only as real sites demand; no speculative coverage. (F9) |
| End-to-end wiring test for no-seam tools (nc/peep/retry) | Their leak-fix wiring is verified by per-site grep + commit-diff, not an end-to-end test, until the seam retrofit lands. Residual tracked in `cli-seam-retrofit-backlog`. (F10) |
| SAFE/ACCEPTABLE classifications unverified by per-site test | They are classifier predictions; the native smokes would catch a misclassification, and Step 4's probe de-risks the SAFE set. (F12) |
| **envvault secret-store-creation broad catches** (`Program.cs:40,58`, `Cli.cs:88` via `UnwrapTypeInit`) | DESIGN TENSION, not a mechanical fix. These surface *custom* backend messages ("keyring locked", "secret-tool missing", the libsecret load-failure cause) that SecretStore backends throw with literal English — 7 tests guard that the backend message is NOT swallowed. `SafeError` maps by type and would destroy them; DPAPI failures are `Win32Exception` (native text, already safe). A real fix needs a `SafeError` variant that prefers a non-framework inner `.Message` (out of scope). Found during the 2026-06-02 complete re-audit; left untouched. |
| **less `Cli.cs:184`** (TOCTOU FileNotFoundException) | Narrow race: a file deleted between the `File.Exists` check and the `FileStream` open yields a framework `FileNotFoundException` whose `.Message` leaks. Routing through `SafeError` would regress the dominant case (`"File not found: <path>"` → `"no such file"`, losing the path). Lowest-severity entry; deferred. |
| **Two-arg `ArgumentException(msg, paramName)` throw-form class** | A SEPARATE class: the auto-appended `(Parameter 'x')` renders as SR key `Arg_ParamName_Name` when `.Message` is printed. Reachable instance: `schedule/CrontabParser.cs:142,308` → printed at `Program.cs:193`; the clean fix conflicts with `ex.ParamName` security-pin tests (R3 crontab-injection guards). `HmacFactory.cs:52` (3-arg AOORange, unreachable). A large set of *latent* guard/DI-seam throws use this form but don't reach user output. Suite-wide remediation deferred as its own effort. |

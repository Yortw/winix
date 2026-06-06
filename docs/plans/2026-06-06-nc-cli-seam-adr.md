# ADR: nc Cli.RunAsync Seam Retrofit (seam phase 4, final)

**Date:** 2026-06-06
**Status:** Accepted
**Context:** Applies the phase-1 convention (D1's byte-stream shape) + phase-2/3 refinements to nc — the last of the five seamless tools. Records only nc-specific decisions.
**Design doc:** [2026-06-06-nc-cli-seam-design.md](2026-06-06-nc-cli-seam-design.md)

---

## N1. Single `Stream stdout` + internal writer wrap for check mode

**Context:** nc's stdout is bytes in connect/listen mode but text (the open-port list via `Console.Out`) in check mode. The convention signature has one `Stream stdout`.

**Decision:** Keep the convention signature. Check mode wraps `stdout` in a `leaveOpen` UTF-8(no-BOM) `StreamWriter`, flushed before return.

**Rationale:** One stdout, no signature special-casing, matches the phase-1 ADR's recorded shape for nc. The byte-identity claim vs `Console.Out` under `UseUtf8Streams` is verified by the byte-stability gate plus a dedicated exact-bytes seam test — not assumed.

**Trade-offs accepted:** A subtle encoding/newline equivalence carried by tests rather than by construction.

**Options considered:** two stdout parameters (6 params, mix-up-prone, diverges from the recorded convention); TextWriter-primary with a byte escape hatch (inverts the tool's nature) — both rejected.

## N2. Streams are caller-owned; the seam never disposes them

**Context:** The phase-1 deferred table flagged "console-stream lifetime / dispose ownership" as nc's open question.

**Decision:** `Cli.RunAsync` never disposes `stdin`/`stdout`. Caller owns lifetime (production: process-lifetime console streams; tests: their `MemoryStream`s). The internal check-mode writer is `leaveOpen`.

**Rationale:** Parity with today (the console streams are never disposed); disposing a caller's stream from a library is an ownership violation; `leaveOpen` keeps the wrap side-effect-free.

## N3. CTS/CancelKeyPress relocate from `DispatchAsync` to Main

**Context:** nc uniquely registers its Ctrl+C handler mid-stack (inside `DispatchAsync`) rather than in Main.

**Decision:** Registration moves to Main (convention: process-globals in Main); the seam takes the token; `DispatchAsync` dissolves.

**Rationale:** Convention alignment; handler body unchanged (named + finally-unregistered, ObjectDisposedException-swallowing). The OCE catch that consumes user-cancel moves into `Cli.RunAsync` with the rest of the envelope machinery (retry/wargs precedent), so exit-130 semantics are unchanged and now seam-testable.

**Trade-offs accepted:** Slightly larger relocation delta than the other tools (the registration site changes stack level, not just file). Behaviour identical: registration still precedes all network activity.

## N4. `UsageException` relocates to the library

**Context:** The validation matrix (BuildOptions) moves into the library; its exception type currently lives in the app.

**Decision:** Move `UsageException` to `src/Winix.NetCat/UsageException.cs` with the visibility the library requires.

**Rationale:** The type belongs with its throwers/catchers; leaving it in the app would force a library→app reference (impossible) or a duplicate.

## N5. Deliberate non-test: seam-level wall-clock timeout (exit 2)

**Decision:** No seam test drives the timeout exit against a firewalled/blackholed address; existing library-level timeout coverage stands.

**Rationale:** Wall-clock network-timeout tests against unroutable addresses are flake bait on CI (route behaviour varies by network). Recording the absence as a decision per the loop-closer discipline.

## N6. Two-stage test delivery (inherited W4) + probed cancellation envelope

**Decision:** Stage 1 wiring validates neutrality; stage 2 adds byte-path and cancellation tests. nc's cancellation envelope (`exit_reason: "interrupted"`, not `"cancelled"`) is probed on the real binary before any assertion or fixture pins it.

**Rationale:** The W4 auditability property (phase 3) + the probe-before-pin rule that falsified wargs's documented `-1` — nc's envelope differs from its siblings, exactly the trap probing exists to catch.

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|---|---|
| `UnwrapTypeInit` lift to ShellKit (3+ copies now) | Quality follow-up AFTER the seam class closes; suite-wide change deserves its own small pass |
| TLS-listen support | Pre-existing product limitation, unrelated to the seam |
| Timeout-exit seam test hardening (deterministic fake-clock probe) | Needs a PortChecker time seam — out of behaviour-neutral scope |

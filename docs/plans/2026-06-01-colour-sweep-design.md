# Suite-wide colour sweep — design

**Date:** 2026-06-01
**Status:** Approved (brainstorm complete; pending per-sub-plan implementation plans)
**Scope:** all colour-relevant Winix tools (functional fixes on 3, regression tests on ~18, doc fixes on 9)
**Depends on:** ShellKit `--color=when` (built this session, commits `b6ff0d2..449a1bb`) — the flag surface is in place; this sweep makes colour actually *emit* and locks it against regression.

## 1. Motivation

The 2026-06-01 colour audit found two defect classes plus a regression-proofing gap:
- **Unwired colour (functional bug):** `trash`, `hcat`, `wargs` thread `useColor` and their READMEs claim coloured output, but their formatters emit **no** ANSI — `--color` is a silent no-op. `wargs` shipped that way in v0.3.0 (passed 17 review rounds — proof the blind spot is structural: a flag whose effect nothing asserts).
- **Doc drift:** 9 tools / 15 surfaces document `--color WHEN` implying a space-separated value; the implementation is equals-only (`--color=never`).
- **No regression guard:** the 15 tools that *do* emit colour have no test asserting `--color` actually colours — which is exactly how the unwired tools slipped through. (The hcat-QR class: a green test on a side formatter while the real caller is unwired — see `feedback_test_passes_but_caller_unwired`.)

This sweep closes all three: emit-fixes, regression tests, doc fixes.

## 2. Test pattern (the regression guard)

**Primary — end-to-end through the tool's real `Cli`/output seam.** Force colour on (`--color` or `--color=always`), capture the tool's output writer, assert it **contains** ESC; force off (`--no-color` or `--color=never`), assert it contains **no** ESC. This drives the production path — the only form that catches an unwired formatter. `--color`/always forces colour even when the captured writer is not a TTY (ShellKit's `ResolveColor` honours the explicit flag over auto-detection), so a `StringWriter` capture works.

**ESC literal:** express the escape byte as `((char)27).ToString()` in assertions — NOT a `""`/`"\x1b"` string literal (those round-trip ambiguously through JSON tool-args and source encoding; `(char)27` is unambiguous). `Assert.Contains(esc, captured, StringComparison.Ordinal)` / `Assert.DoesNotContain(esc, captured, StringComparison.Ordinal)`.

**Fallback — renderer/formatter level.** For interactive/pager tools (`less`, `man`, `peep`) where driving full `Cli.Run` to capture coloured output is impractical (blocking screen loop / renderer not a summary writer), test the real rendering function directly (the demux `RoutingSummary.FormatHuman` pattern), still asserting ESC-iff-`useColor`. Which tools need the fallback is decided per-tool when its sub-plan task is written.

**No shared helper.** Seam signatures vary (`Run(args, stdout, stderr)`; injectable deps for notify/ids/mksecret; async `RunAsync` for winix/squeeze). Each test is ~10 lines using that tool's own seam (a fake/null for deps, `await` for async). The *pattern* is uniform; the wiring is per-tool.

## 3. Colour convention (for the emit-fixes)

Apply the established suite idiom — capture locals `string dim = AnsiColor.Dim(useColor); … red = AnsiColor.Red(useColor); reset = AnsiColor.Reset(useColor);` (each returns `""` when `useColor` is false, so plain output is byte-identical), then interpolate, exactly like demux's `RoutingSummary.FormatHuman` and `Winix.Retry/Formatting`. Roles: **dim** headers/labels, **green** success/counts, **red** errors/dead, **yellow** warnings. Each tool colours **what its README already claims** — no new claims:
- **trash** — its `--list` table and summary lines.
- **hcat** — its banner and request-log lines.
- **wargs** — its terminal/verbose output (exact elements confirmed against the code when Sub-plan A is written).

Plain (`--no-color` / non-TTY) output must remain byte-for-byte identical to today.

## 4. Decomposition — three sub-plans, each separately planned + adversarially reviewed

### Sub-plan A — emit-fixes (the functional bug)
Tools: **trash, hcat, wargs.** Per tool: (1) wire colour emission in the formatter per §3; (2) add the end-to-end colour regression test per §2; (3) fix that tool's `--color WHEN`→`--color=WHEN` doc surface (trash + hcat have the drift; align wargs's Colour section too). Establishes the test pattern on 3 tools. Code + test + doc. Adversarial review before build.

### Sub-plan B — regression-proof the already-emitting tools
Tools (14 — demux already done this session): **timeit, squeeze, peep, files, treex, man, less, whoholds, schedule, nc, winix, retry, when, envvault.** Add the §2 colour regression test to each. **Test-only — no production code change** (these already emit correctly; the test locks them). Group by seam type: simple-writer (most), async (winix, squeeze), interactive-fallback (less, man, peep). Lower-risk; still reviewed. Scales the pattern A proved.

### Sub-plan C — doc-only fixes
Tools (7 data-tools, correctly colourless, only the wording is wrong): **digest, ids, notify, url, mksecret, protect, unprotect.** Mechanical README/man `--color WHEN`→`--color=WHEN` (auto/always/never) edits + drop any wrong "`--no-color` ≡ `--color never`" lines (now actually true, so keep/correct). No code. Repo doc commits, not re-ships.

**Sequence: A → C → B.** A fixes the real bug and proves the test pattern; C is a quick mechanical pass that closes the doc-drift class; B is the largest (14-tool test rollout). Each sub-plan = its own `writing-plans` plan → `adversarial-plan-review` → subagent build.

## 5. Out of scope / deferred

| Topic | Why |
|---|---|
| ShellKit `--color=when` parser | Already built this session (the dependency). |
| Changing WHAT each tool colours beyond its README claim | This sweep makes claims *true*, it doesn't expand them. |
| New colour for the data tools (digest/url/clip/…) | They correctly emit no colour (data output); only their docs are fixed (C). |
| A shared cross-tool colour-test helper | Seam signatures vary too much; per-tool ~10-line tests are clearer. |
| Re-shipping shipped v0.3.0 tools for the doc fixes | Repo doc edits only; shipped copies self-heal on each tool's next release. `wargs`'s *functional* fix (A) rides v0.4.0. |

## 6. Success criteria

- `trash`, `hcat`, `wargs` emit ANSI when colour is on and none when off (verified end-to-end).
- Every colour-emitting tool (~18) has a regression test asserting ESC-iff-colour through its real output path.
- No tool documents `--color WHEN` (space form); all show `--color[=auto|always|never]` or bare `--color`/`--no-color`.
- Full solution build 0 warnings; full suite green (modulo the known parallel-run `IsOnPath_DoesNotSpawnProcess` flake); plain (`--no-color`) output byte-identical to today for every tool.

# Design — Suite-wide resource-key leak remediation

**Date:** 2026-06-02
**Status:** Approved (brainstorm)
**Branch:** `release/v0.4.0`
**Companion ADR:** `2026-06-02-resource-key-leak-adr.md`

## Problem

Every Winix tool's app csproj sets `<UseSystemResourceKeys>true</UseSystemResourceKeys>` (a NativeAOT/trim size optimisation). Under that runtime feature switch, .NET's `System.Private.CoreLib` exception messages return the bare **resource key** plus its format arguments instead of English. So any code that pipes a *framework* exception's `.Message` into user-facing output emits an unreadable key in the shipped binary.

Observed on the real demux native binary:

| Trigger | Intended | Actual (shipped) |
|---|---|---|
| bad regex `(` | "missing closing parenthesis at position 1" | `MakeException, (, 1, InsufficientClosingParentheses` |
| missing directory | "Could not find a part of the path 'C:\…'" | `IO_PathNotFound_Path, C:\…\out.log` |

**Severity: cosmetic / diagnosability, not functional.** It is deterministic on the affected error path, but only fires on those specific framework-error paths (bad regex, missing/again unwritable file, malformed input). It does **not** affect: the happy path; our own (literal-string) error messages; exit codes; `--json` envelopes; `--describe`; `--help`. It affects both the AOT native binaries and the framework-dependent NuGet global tools, because the switch is a runtime feature flag set in each tool's csproj (proven: the JIT test host reproduced the leak the instant the flag was mirrored onto the test csproj).

### Why it stayed invisible

Two compounding blind spots:

1. **Ineffective test guard.** 25 app csprojs set `UseSystemResourceKeys=true`; only `Winix.Demux.Tests` (fixed 2026-06-02) mirrors it. The other ~24 test projects "guard" the class with `InvariantGlobalization=true` alone — which does **not** reproduce the leak (the test host resolves messages to English). Our memory misattributed the mechanism to `InvariantGlobalization`; the real trigger is `UseSystemResourceKeys`.
2. **No native capability smoke for the new tools.** A `run-smokes.sh` + `manual-smoke.yml` native-binary harness exists and covers the 23 v0.3 tools, but the 4 v0.4 tools (mksecret, trash, hcat, demux) were never added. Their verification was in-process `Cli.Run` tests (which don't reproduce the leak) plus ad-hoc manual checks.

### Audit results (classifier subagent, 2026-06-02)

~88 `ex.Message`→user-output sites classified:
- **LEAK (43):** framework exception piped raw, no type context. The fix list.
- **ACCEPTABLE (24):** already include `ex.GetType().Name` — context even if the message is a key. Left alone.
- **SAFE (21):** only project exceptions reach them, or `Win32Exception`/`SocketException`/`COMException` (native-OS message text, unaffected by the flag). Left alone.

LEAK sites cluster as: regex-parse (`peep`, `files`, `treex`), file-IO (`digest` ×5, `treex` walk ×7, `protect`, `envvault`, `less`, `schedule`, `notify` Aumid), broad `catch (Exception)` catch-alls (`mksecret`, `trash`, `hcat` ×2, `ids`, `digest`, `demux` summary path), typed (`qr`, `when` timezone, `retry`), and framework-message-embedded-in-our-wrapper (`envvault` decoder, `winix` json, `schedule` crontab). A wrong comment at `files/Cli.cs:330` claims `RegexParseException` is safe — it is not.

## Design

### 1. Shared helper — `Yort.ShellKit.SafeError`

A small static class in ShellKit (lowest shared layer; AOT-safe):

```csharp
public static class SafeError
{
    /// <summary>Stable, readable description of an exception for user output.
    /// NEVER returns ex.Message (which is an SR resource key under UseSystemResourceKeys).
    /// Type-maps common CoreLib exceptions to English; falls back to ex.GetType().Name.</summary>
    public static string Describe(Exception ex);
}
```

Mappings (verify exact set at implementation):
- `DirectoryNotFoundException` → `"no such directory"`
- `FileNotFoundException` → `"no such file"`
- `UnauthorizedAccessException` → `"access denied"`
- `PathTooLongException` → `"path too long"`
- `RegexParseException` → `$"{ex.Error} at offset {ex.Offset}"` (Error/Offset are invariant-stable enum/int)
- (extend with IOException-general, FormatException, etc. as the fix surface demands)
- default → `ex.GetType().Name` (turns a LEAK into the ACCEPTABLE shape: context, no key)

The caller supplies its own prefix and any path/context (e.g. `$"digest: failed to read '{path}': {SafeError.Describe(ex)}"`).

### 2. Guard fix (P2)

Add `<UseSystemResourceKeys>true</UseSystemResourceKeys>` to every test csproj lacking it (~24), mirroring its app csproj. Keep `when`'s `InvariantGlobalization=false`. Per tool: add flag → run existing tests → fix any breakage (only tests asserting framework English; update the assertion text, preserve the contract — never shape-shift the assertion's intent).

### 3. Leak remediation (P4) — hybrid

- Route LEAK sites through `SafeError.Describe` for the common cases.
- Leave genuinely-good tool-specific text bespoke (already SAFE/ACCEPTABLE — `nc` socket text, `trash` statx errno, `notify` COM HRESULT).
- Broad `catch (Exception)` → `SafeError.Describe(ex)` (gets the `GetType().Name` fallback).
- **Leave ACCEPTABLE and SAFE sites untouched** (YAGNI — not broken).
- One commit per tool.
- Fix the wrong `files/Cli.cs:330` comment as part of the files commit.

### 4. Native smokes (P3)

- Author `run-smokes.sh` capability fixtures for the 4 v0.4 tools (promote demux's from `tmp/`; derive each from the tool's README options/exit-code surface, not from its tests).
- Pick a canonical home consistent with the existing `artifacts/.../run-smokes.sh` convention (resolve exact path at planning time — current fixtures live under dated `artifacts/` folders).
- Add all 4 to `manual-smoke.yml`'s tool list + runner-OS map.

### 5. Convention (P1)

Update CLAUDE.md "adding a new tool" checklist:
- test csproj mirrors `UseSystemResourceKeys` (not just `InvariantGlobalization`);
- every tool ships a `run-smokes.sh` + `manual-smoke.yml` entry;
- new error paths use `SafeError.Describe`, never raw framework `ex.Message`.

### 6. Testing

- `SafeError` unit tests: every type mapping + regex Error/Offset + unknown→GetType().Name fallback. Test csproj sets `UseSystemResourceKeys=true` so it exercises the real condition.
- One regression test **per distinct leak-class per tool**, exercised through the real `Cli.Run`/library seam: red under the mirrored flag (pre-fix) → green post-fix. E.g. `treex` gets a regex test AND a walk-IO test; a single-catch-all tool gets one. Sites buried in network/walk loops that can't be triggered deterministically rely on the helper's own tests + code inspection (noted explicitly per tool).

### 7. Sequencing & non-goals

Order: P1 + helper (so leak fixes have something to call) → P2+P4 per tool → P3 → final full-solution green + native re-smoke of the 4 new tools.

**Non-goal: no v0.3.x re-ship.** Already-shipped v0.3.0 binaries stay as-is; fixes ride out as each tool next ships (v0.4.0 and beyond). The work lands entirely on `release/v0.4.0`.

## Testing strategy summary

- Helper: exhaustive unit tests.
- Per tool: one regression test per distinct leak-class, through the production seam, proving the wiring (closes the "claimed-but-unwired" gap).
- Full `dotnet test Winix.sln` green after each tool's commit.
- Native re-smoke of mksecret/trash/hcat/demux binaries at the end.

## Risks

- **Mirrored flag breaks an existing test** that asserts framework English → expected; update the assertion (don't change the contract). Demux had none; varies per tool.
- **Classifier hypotheses, not facts.** The 43 are predictions; the mechanism is reproducer-confirmed (demux). The fix can only improve a message (never changes behaviour/exit code), and each per-tool test proves the site was leaking by going red first. We will not claim a site "leaked in production" unless its guard test fires.
- **`SafeError` mapping completeness** — start from the audited exception types; extend only as real sites demand (avoid speculative mappings).

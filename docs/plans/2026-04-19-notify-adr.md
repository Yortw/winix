# notify — Architecture Decision Record

**Date:** 2026-04-19
**Status:** Accepted
**Companion document:** [2026-04-19-notify-design.md](2026-04-19-notify-design.md)
**Context:** Designing `notify`, the second tool in Winix v0.4.0. A cross-platform CLI for desktop notifications + ntfy.sh push notifications.

---

## D1. Ship desktop and ntfy.sh together in v1 (not desktop-first / ntfy-second)

**Context.** The original v0.4.0 plan (2026-04-18) had ntfy.sh as v2. Brainstorming surfaced that the two backends are genuinely complementary — desktop covers "I'm at my desk", ntfy covers "stepped out" — and ntfy is ~50 lines of HttpClient.

**Decision.** Both backends ship in v1.

**Rationale.** The user's use cases overlap. Splitting across versions would force users to wait for v2 for the "remote alerts" pattern, which is one of the main reasons to reach for a notification CLI in the first place.

**Trade-offs accepted.** Slightly more v1 surface area (one extra backend, four flags, one env-var convention). Worth it for the unified mental model.

**Options considered.**
- Desktop only (v2 ntfy) — rejected; defers a high-value, low-cost feature.
- ntfy only (defer desktop) — rejected; the platform-toast layer is the harder, more valuable gap-fill.

---

## D2. Desktop and ntfy fire in parallel when both configured

**Context.** When both backends are configured (e.g. `--ntfy TOPIC` + default desktop), should we fire both, or pick one?

**Decision.** Both fire in parallel. The env var (`NOTIFY_NTFY_TOPIC`) is the global "I want remote alerts on" switch; per-invocation `--no-desktop` / `--no-ntfy` are the suppression escape hatches.

**Rationale.** Zero per-call decision needed. Matches the actual user scenario — at desk you see desktop, away you see phone. Cost of running both is dispatch time only (~milliseconds for desktop, ~tens of milliseconds for ntfy round-trip) and they run on separate `Task`s so they don't serialize.

**Trade-offs accepted.** A globally-set env var means every `notify` call phones home. Mitigated by the `--no-ntfy` per-call override. Documented in README.

**Options considered.**
- Explicit `--via desktop|ntfy|both` — rejected as too verbose for the common case.
- ntfy wins if configured, else desktop — rejected; loses the "both" use case which is the actual user value.

---

## D3. Title and body as positionals, no stdin auto-read

**Context.** How does the user pass title + body? Positionals, flags, stdin, or some combination?

**Decision.** `notify TITLE [BODY]` — two positionals, body optional. Stdin is not consulted. Use shell substitution (`notify "error" "$(tail -1 build.log)"`) for the compose case.

**Rationale.** Matches Linux `notify-send` (most reference tool); zero ambiguity; simplest possible CLI. Stdin auto-read creates surprising behaviour when a pipe is accidentally still open.

**Trade-offs accepted.** Some compose patterns (`cmd 2>&1 | notify "build failed" --body -`) need shell substitution instead. Acceptable; idiom is well-known to Unix users.

**Options considered.**
- `notify TITLE [BODY]` + `--body -` opt-in for stdin — rejected as YAGNI for v1; can be added later non-breakingly.
- `notify` reads both from stdin — rejected; awkward interactive UX, surprising when stdin is unintentionally redirected.

---

## D4. Windows toast — design said direct WinRT COM, ships as PowerShell shellout (amended)

> **Amended 2026-04-19 during implementation.** The original decision below was the right *intent*, but modern .NET (5+) cannot marshal `IInspectable` (the WinRT base interface) at runtime — `[ComImport, InterfaceIsIInspectable]` compiles but throws `PlatformNotSupportedException` at first use. The official path requires `Microsoft.Windows.SDK.NET.Ref` plus a TFM split (`net10.0-windows10.0.19041.0`) through the entire dependency chain. We fell back to the design's documented option B: **inline PowerShell + WinRT toast XML**. PowerShell 5.1+ has its own internal WinRT projection that handles `IInspectable` marshalling. Cost: ~400ms cold-start instead of ~30ms direct. Accepted for v1; a future v2 can migrate to the WinRT projection if sub-100ms latency becomes important. `AumidShortcut` survives unchanged because `IShellLinkW` is classic COM (`IUnknown`), which modern .NET still supports via `CoCreateInstance` + `[ComImport]`. Original decision context preserved below for reference.



**Context.** Windows toast notification implementations range from a 5-line MessageBox call to a 200-line WinRT-from-AOT integration with AUMID registration.

**Decision.** Direct WinRT toast via COM, with an idempotent per-user Start Menu shortcut for the AUMID.

**Rationale.** Matches Winix's "fast native" ethos. ~30ms latency (vs ~400ms for PowerShell shellout). One-time complexity cost in the codebase, paid back every invocation. The AUMID-via-shortcut trick (`%APPDATA%\Microsoft\Windows\Start Menu\Programs\Winix Notify.lnk`) keeps installation requirements zero — no installer dependency, no registry-write privileges needed; works for direct-download installs the same as scoop/winget installs.

**Trade-offs accepted.** ~200 lines of AOT-friendly COM/WinRT code. AOT trim warnings need careful `[DynamicDependency]` annotations. AUMID shortcut creation adds ~5-10ms idempotent cost per invocation (skipped after first run). One on-disk artifact (the .lnk) created in the user's Start Menu — visible but innocuous.

**Options considered.**
- PowerShell shellout with inline toast XML — rejected; ~400ms cold-start cost, contradicts "fast native" ethos.
- Plain `MessageBox` via user32.dll — rejected; modal dialog is not a "notification" in 2026 UX terms.
- Bundle snoretoast.exe — rejected; adds a third-party binary to every release; supply-chain input we'd rather not have.
- BurntToast PowerShell module — rejected; users would need to install a separate module.

---

## D5. macOS uses osascript (no proper notification API)

**Context.** The proper macOS notification APIs (`UNUserNotificationCenter`, deprecated `NSUserNotification`) require the calling binary to be packaged as a signed app bundle with Info.plist. A loose CLI binary doesn't qualify and can't be made to qualify without significant restructuring.

**Decision.** Shell out to `osascript -e 'display notification ...'`.

**Rationale.** Forced choice. osascript is universally present on macOS, has zero dependencies, and produces a real notification (the same kind that `notify-send`-style apps produce). The "ugly" framing in earlier notes was about its quote-escaping syntax, not about user-visible output.

**Trade-offs accepted.** Shellout latency (~tens of milliseconds — negligible for fire-and-forget). No icon support (osascript doesn't expose it without a bundle). Documented in README and per-flag help.

**Options considered.**
- Bundle the CLI as a `.app` — rejected; massively over-engineers a notification tool, breaks the "single binary across all platforms" model.
- Shell out to `terminal-notifier` (Homebrew tool) — rejected; adds an install requirement.

---

## D6. Linux uses `notify-send` shellout (not direct D-Bus)

**Context.** `notify-send` (libnotify CLI) is the universal Linux desktop notification path. Direct D-Bus to `org.freedesktop.Notifications` is feasible but adds ~200 lines of D-Bus protocol code.

**Decision.** Shell out to `notify-send`. Detect at backend construction; if absent, return a `BackendResult` with a helpful install hint per distro.

**Rationale.** `notify-send` is as universal as libnotify itself (they ship together — `libnotify-bin` on Debian/Ubuntu, included in `libnotify` on Fedora). Shell-out cost on Linux is ~5ms. The complexity of speaking D-Bus directly buys us nothing the user perceives.

**Trade-offs accepted.** Hard dependency on `notify-send` being installed — but the install command is one line and our error message tells the user exactly what to do. Slightly slower than direct D-Bus by a few milliseconds (irrelevant for fire-and-forget).

**Options considered.**
- Direct D-Bus — rejected for v1; complexity isn't justified by user-visible benefit. Can be added later if a "no shell-out" purity requirement emerges.
- P/Invoke libnotify — rejected; AOT-friendly but fragile across distros (library presence/version skew).

---

## D7. Strategy interface (`IBackend`) for testability

**Context.** Backend implementations involve OS-specific calls (WinRT, osascript, notify-send, HttpClient). They're inherently hard to unit-test.

**Decision.** Every backend implements `IBackend.Send(NotifyMessage) → BackendResult`. Tests use fake `IBackend` implementations to verify dispatch, parallel execution, result aggregation, exit-code logic.

**Rationale.** All orchestration logic — which backends to run, how to aggregate results, exit-code mapping — is testable without touching the actual desktop or network. Backend internals (the OS-specific call) are smoke-tested manually per the digest pattern.

**Trade-offs accepted.** No automated integration test for "did a toast actually appear?" Requires manual smoke-testing on each platform. Acceptable: visible-output testing is a CI dead-end regardless of implementation.

**Options considered.**
- Direct method calls in `Dispatcher` switching on `RuntimeInformation.IsOSPlatform` — rejected; not testable without mocking the platform check.
- Interface segregation per backend type (separate `IDesktopBackend` / `INtfyBackend`) — rejected as YAGNI; one interface fits both shapes.

---

## D8. Best-effort default + `--strict` opt-in

**Context.** When some configured backends fail, what's the exit code?

**Decision.** Default: exit 0 if at least one backend succeeded. `--strict` flag inverts this — exit 1 if any backend failed.

**Rationale.** The common case is "I want my notification to land somewhere" — if desktop fires but ntfy times out, that's still a successful notification. Best-effort by default avoids `notify` becoming the script's failure point in flaky-network situations. `--strict` is available for users who want all-or-nothing semantics (e.g. CI alerting where missing-the-message is a bug).

**Trade-offs accepted.** Failed backends are easy to miss in best-effort mode. Mitigated by always logging warnings to stderr (`notify: warning: <backend>: <error>`) so the user sees what happened.

**Options considered.**
- All-or-nothing default — rejected; punishes the common case for the rare strict case.
- No exit-code distinction (always 0 if any backend ran) — rejected; loses signal for users who want it.

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|---|---|
| `--timeout MS` | Backend semantics differ wildly (libnotify supports, Windows toast doesn't really, ntfy doesn't apply). Cross-platform answer is muddy. Add if asked. |
| `--sound` | Windows toast has it, Linux libnotify limited, macOS osascript awkward. Same muddy story; add if asked. |
| `--actions` (clickable buttons) | Adds complexity around capturing the click + reporting it back. Out of scope for v1. |
| ntfy `Tags`, `Click`, `Markdown`, attachments | ntfy has a rich feature surface; ship the basics first, grow naturally. |
| Stdin auto-read for title/body | YAGNI for v1; shell substitution covers compose patterns. Easy to add later non-breakingly. |
| Direct D-Bus on Linux (no `notify-send` dep) | Pure-purity argument; no user-perceived benefit over the shellout. Revisit if "no external deps" becomes a hard requirement. |
| Bundling as a macOS `.app` for icon support | Massive complexity for one feature. Revisit if icon-on-macOS becomes important enough. |
| AOT WinRT projection mechanism (direct COM vs `Microsoft.Windows.SDK.NET.Ref` vs `CsWinRT`) | Implementation detail to decide during the plan, not a design decision. |

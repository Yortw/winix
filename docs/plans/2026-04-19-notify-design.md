# notify — Design

**Date:** 2026-04-19
**Tool:** `notify`
**Release:** v0.4.0
**Status:** Approved

## Goal

A one-shot CLI that sends a desktop notification and/or a push to ntfy.sh in a single invocation, with consistent UX across Windows, macOS, and Linux. Pairs naturally with long-running commands (`make test; notify "tests done"`) and with other Winix tools (`peep --once cmd; notify ...`).

## Positioning

The notification space is fragmented: Linux `notify-send` (libnotify), macOS `osascript -e 'display notification ...'` (awkward), Windows has no first-class CLI (people reach for the BurntToast PowerShell module or third-party binaries). `notify` provides one consistent CLI with the same flags, exit codes, and JSON output across all three platforms, plus optional remote push via ntfy.sh in the same invocation.

Cross-suite synergy: complements `timeit`, `peep`, `wargs`, `retry` — pure shell composition, no integration code.

**Landscape acknowledgement:** Scott Hanselman's [Toasty](https://github.com/shanselman/toasty) is the closest existing tool — Windows-only, focused on AI agent hooks. README will cite it as the Windows-AI-agent alternative.

## CLI Interface

**Synopsis:**

```
notify TITLE [BODY] [options]
```

**Flags:**

| Flag | Default | Description |
|---|---|---|
| `--urgency low\|normal\|critical` | `normal` | Maps to backend-specific urgency + ntfy priority. |
| `--icon PATH` | none | Best-effort per backend: libnotify accepts paths/named icons; Windows toast accepts file paths; macOS osascript ignores (limitation, documented). |
| `--ntfy TOPIC` | env `NOTIFY_NTFY_TOPIC` | Send a push to ntfy.sh on this topic. Without `--ntfy` and without env, ntfy is skipped. |
| `--ntfy-server URL` | env `NOTIFY_NTFY_SERVER` or `https://ntfy.sh` | Override for self-hosted ntfy. |
| `--ntfy-token TOKEN` | env `NOTIFY_NTFY_TOKEN` | Bearer auth for self-hosted ntfy with access control. |
| `--no-desktop` | off | Suppress desktop backend (e.g. when you only want the push). |
| `--no-ntfy` | off | Suppress ntfy even if env is set. |
| `--strict` | off | Exit non-zero if any configured backend fails. Default is best-effort. |
| `--json` | off | Emit a JSON record describing what was sent and per-backend status. |
| `--describe` / `--help` / `--version` / `--color` / `--no-color` | | Standard Winix flags. |

**Both backends fire when both are configured** — the env var is the global "I want remote alerts on" switch, per-invocation `--no-*` flags are the escape hatches. Rationale: at desk → see desktop; stepped out → phone fires too. Zero per-call decision needed.

**Exit codes:**

| Code | Meaning |
|---|---|
| `0` | Success — at least one backend succeeded (or all succeeded under `--strict`). |
| `1` | `--strict` mode; at least one configured backend failed. |
| `125` | Usage error — bad flags, missing title, no backends configured. |
| `126` | All backends failed. |

**Examples:**

```bash
notify "build done"
notify "tests failed" "5 of 200 failed — see build.log"
notify "deployment complete" --urgency critical
notify "scrape done" --ntfy myalerts                 # desktop + push
NOTIFY_NTFY_TOPIC=alerts notify "ssh: idle warning"  # env-set, applies globally
notify "see you" --no-desktop --ntfy phone           # push only
make test; notify "tests done"
notify "error" "$(tail -1 build.log)"
```

## Architecture

**Project structure** (mirrors digest):

```
src/Winix.Notify/
  ArgParser.cs                 — argv → NotifyOptions, Q-matrix validation
  NotifyOptions.cs             — record carrying parsed config
  Urgency.cs                   — enum: Low, Normal, Critical
  IBackend.cs                  — interface: Send(NotifyMessage) → BackendResult
  Backends/
    WindowsToastBackend.cs     — WinRT toast via COM
    MacOsAppleScriptBackend.cs — osascript shellout
    LinuxNotifySendBackend.cs  — notify-send shellout
    NtfyBackend.cs             — HttpClient POST to ntfy server
  AumidShortcut.cs             — Windows-only Start Menu shortcut helper (idempotent)
  Dispatcher.cs                — selects backends; runs in parallel; aggregates
  Formatting.cs                — JSON output composition
src/notify/
  Program.cs                   — thin orchestrator, exit-code mapping
  notify.csproj                — AOT, PackAsTool
  README.md
  man/man1/notify.1
tests/Winix.Notify.Tests/
  ArgParserTests.cs
  NtfyBackendTests.cs          — uses fake HttpMessageHandler
  DispatcherTests.cs           — uses fake IBackend implementations
  FormattingTests.cs
```

**Strategy interface (`IBackend`):** every backend implements one method (`Send(message)`). The dispatcher selects which to invoke based on platform (`RuntimeInformation.IsOSPlatform`) plus the `--no-desktop` / `--ntfy` flags. Fake backends in tests verify dispatch and aggregation without touching the actual desktop or network.

**Parallel dispatch:** when both desktop and ntfy fire, run them on separate `Task`s so a slow ntfy POST doesn't delay the desktop toast (and vice versa). Aggregate results, log per-backend failures to stderr, return the appropriate exit code.

**Best-effort default + `--strict` opt-in:** at-least-one-backend-succeeded passes by default; `--strict` requires all configured backends to succeed.

**No new shared library** — `notify` doesn't need anything codec-shaped; pure tool-local code. No additions to `Winix.Codec` / `Winix.FileWalk`.

## Backend Implementations

### Windows (`WindowsToastBackend`)

- WinRT `ToastNotificationManager.CreateToastNotifier(aumid).Show(toastNotification)` from a built XML payload.
- AUMID: `Yortw.Winix.Notify` (reverse-domain). Set on the per-user Start Menu shortcut by `AumidShortcut.EnsureExists()`.
- XML payload: `Windows.UI.Notifications.ToastTemplateType.ToastText02` (title + body line) when no `--icon`, or a custom `<binding template="ToastGeneric">` with `<image src="…"/>` when `--icon` provided.
- Urgency mapping: `low` → silent toast, `normal` → default toast, `critical` → `Scenario="urgent"` (priority + repeating sound on Win11; harmless attribute on Win10).
- Failure: WinRT call throws → `BackendResult(false, "Windows toast: <type>: <message>")`. AUMID shortcut creation failure → bare `CreateToastNotifier()` fallback (may fail silently; documented).
- AOT: WinRT direct COM is the trickiest piece. Use `[DynamicDependency]` annotations carefully; verify with `dotnet publish -r win-x64 --verbosity normal` for trim warnings before merging.

### macOS (`MacOsAppleScriptBackend`)

- `osascript -e 'display notification "BODY" with title "TITLE" sound name "Submarine"'`
- argv passed via `ProcessStartInfo.ArgumentList` (per project convention — never string-concat into `Arguments`). Quotes inside title/body escaped within the AppleScript string literal (`"` → `\"`, `\` → `\\`).
- Urgency: macOS notifications don't have urgency natively. Map `critical` → add the `Submarine` alert sound; `low`/`normal` → silent. Documented in README.
- `--icon` ignored on macOS (osascript can't set icon without proper bundle). Documented.

### Linux (`LinuxNotifySendBackend`)

- `notify-send [-u low|normal|critical] [-i ICON] TITLE [BODY]`
- argv via `ArgumentList`.
- Detect `notify-send` on PATH at backend construction; if absent, return `BackendResult(false, "notify-send not found — install libnotify-bin (Debian/Ubuntu) or libnotify (Fedora)")` with helpful hint.
- Urgency maps 1:1 to notify-send's `-u` flag.

### ntfy (`NtfyBackend`)

- `POST {server}/{topic}` with body = message text, headers:
  - `Title: <title>`
  - `Priority: 2|3|5` (low/normal/critical)
  - `Authorization: Bearer <token>` if `--ntfy-token` set
- Static `HttpClient` singleton (recommended pattern; AOT-friendly).
- Timeout: 10s default (hardcoded for v1; `--ntfy-timeout` if asked later).
- Failure: connection refused / 4xx / 5xx → `BackendResult(false, "ntfy POST failed: <status> <message>")`.

### Urgency → backend mapping (single source of truth)

| `--urgency` | Win toast | macOS osascript | Linux notify-send | ntfy priority |
|---|---|---|---|---|
| `low` | silent toast | silent | `-u low` | `2` |
| `normal` (default) | default toast | silent | `-u normal` | `3` |
| `critical` | `urgent` scenario | sound `Submarine` | `-u critical` | `5` |

## Error Handling

- **Best-effort default** — at least one configured backend succeeds → exit 0. Failed backends are logged to stderr (`notify: warning: <backend>: <error>`).
- **`--strict`** — any backend failure → exit 1.
- **No backends configured** (e.g. `--no-desktop` + no `--ntfy`) — exit 125 at parse time, not at dispatch.
- **Headless / no display** — Linux `notify-send` will report a D-Bus error; Windows toast will throw. Surfaced via the warning-to-stderr path. Common in SSH/CI; recommend `--no-desktop --ntfy TOPIC` for those contexts (mention in README).

## JSON Output (`--json`)

```json
{
  "title": "tests done",
  "body": "5 of 200 failed",
  "urgency": "critical",
  "backends": [
    {"name": "windows-toast", "ok": true},
    {"name": "ntfy", "ok": true, "server": "https://ntfy.sh", "topic": "alerts"}
  ]
}
```

- `backends` array contains an entry per backend that was attempted.
- Failed backends include `"error": "..."`.
- JSON to stdout; warnings still go to stderr.

## Testing

- **`ArgParserTests`** — Q-matrix coverage: `--no-desktop --no-ntfy` (error), `--ntfy` without topic value (parser error), `--strict` propagation, env-var fallback for ntfy fields, urgency parse, etc. Pure parsing — no I/O.
- **`NtfyBackendTests`** — fake `HttpMessageHandler` injected via `HttpClient` constructor. Verifies POST URL, headers (`Title`, `Priority`, `Authorization`), body, error mapping. No real network.
- **`DispatcherTests`** — fake `IBackend` implementations. Verify parallel execution, result aggregation, best-effort vs strict exit-code logic, no-op when backend disabled. Pure logic — no platform calls.
- **`FormattingTests`** — JSON shape locked via `JsonDocument` parsing.
- **Manual smoke tests** in the final task (per digest pattern) — run on Windows, exercise toast appearance + Action Center; document steps in the plan.

**No backend integration tests in CI** — desktop notifications are inherently visible output that CI can't verify. The strategy interface lets us cover all orchestration logic with fakes.

## Distribution

Pipeline integration follows the digest pattern:

- `bucket/notify.json` — scoop manifest
- `.github/workflows/release.yml` — pack/publish/zip/combined-zip/tools-map entries
- `.github/workflows/post-publish.yml` — manifest update + winget generator entries
- `CLAUDE.md` — project layout, NuGet package IDs, scoop manifests list
- NuGet package ID: `Winix.Notify`
- Scoop binary: `notify.exe`

## Out of Scope (v1)

- `--timeout MS` — semantics differ wildly per platform; libnotify supports, Windows toast doesn't really, ntfy doesn't apply. Add if requested.
- `--sound` — Windows toast has it, Linux libnotify limited, macOS osascript awkward. Cross-platform answer is muddy.
- `--actions` (clickable buttons in the toast) — adds complexity around capturing the click; defer.
- ntfy `Tags`, `Click`, `Markdown`, attachments — defer; can grow naturally.
- Stdin input for title or body — shell substitution (`$(...)`) covers the compose case. Add if it turns out to be needed; hard to remove once shipped.

## Open Implementation Questions (decide during plan)

- AOT-safe WinRT projection mechanism — direct COM via `[DllImport("combase.dll")]` and `[DynamicDependency]`, vs `Microsoft.Windows.SDK.NET.Ref`, vs `CsWinRT`. Verify which produces the smallest AOT binary with no trim warnings.
- HttpClient singleton creation timing — static `Lazy<HttpClient>` vs constructor-injected. Static `Lazy` likely best for AOT + one-shot CLI lifetime.

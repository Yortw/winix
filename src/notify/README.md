# notify

Cross-platform desktop notifications + ntfy.sh push, in one consistent CLI. Single native binary, no runtime, same flag surface across Windows, macOS, and Linux. Pairs naturally with long-running commands (`make test; notify "tests done"`) and other Winix tools.

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/notify
```

### Winget (Windows, stable releases)

```bash
winget install Winix.Notify
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.Notify
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).

## Usage

```
notify TITLE [BODY] [options]
```

### Examples

```bash
# Basic desktop notification
notify "build done"

# Title and body
notify "tests done" "5 of 200 failed — see build.log"

# Critical urgency — sound + attention on every backend
notify "deploy failed" --urgency critical

# Silent low-priority
notify "scrape complete" --urgency low

# Send to ntfy.sh — desktop AND push to phone
notify "alert" --ntfy myalerts

# Set ntfy globally for the shell session
export NOTIFY_NTFY_TOPIC=alerts
notify "see you"
notify "back online"

# Push only — no desktop attempt (useful in CI / SSH)
notify "server warn" --no-desktop --ntfy phone

# Self-hosted ntfy with auth
notify "deploy ok" --ntfy deploys --ntfy-server https://ntfy.example.com --ntfy-token tk_xyz

# Strict mode — exit non-zero if any backend fails
notify "important" --ntfy alerts --strict

# JSON output for scripts
notify "build done" --json
# {"title":"build done","urgency":"normal","backends":[{"name":"windows-toast","ok":true}]}

# Compose with other tools
make test && notify "tests done"
timeit slow-script.sh && notify "done"
notify "release published" --ntfy phone | tee log.txt
```

## Options

| Flag | Default | Description |
|---|---|---|
| `--urgency LEVEL` | `normal` | `low`, `normal`, or `critical`. |
| `--icon PATH` | none | Icon path. Best-effort per backend (see below). |
| `--ntfy TOPIC` | env `NOTIFY_NTFY_TOPIC` | Send to ntfy.sh on TOPIC. |
| `--ntfy-server URL` | env `NOTIFY_NTFY_SERVER` or `https://ntfy.sh` | Override server (self-hosted). |
| `--ntfy-token TOKEN` | env `NOTIFY_NTFY_TOKEN` | Bearer auth for self-hosted ntfy. |
| `--no-desktop` | off | Suppress the desktop backend. |
| `--no-ntfy` | off | Suppress ntfy even if env var is set. |
| `--strict` | off | Exit non-zero if any configured backend fails (default: best-effort). |
| `--json` | off | Emit JSON output to stdout. |
| `--describe` | | Emit structured JSON metadata for AI agents. |
| `--help` `-h` | | Show help and exit. |
| `--version` `-v` | | Show version and exit. |
| `--color[=auto\|always\|never]` | `auto` | Coloured output: auto (default when omitted), always, or never. |
| `--no-color` | | Disable coloured output. Respects `NO_COLOR`. |

## Backend Behaviour

| Behaviour | Windows | macOS | Linux | ntfy.sh |
|---|---|---|---|---|
| Implementation | Inline PowerShell + WinRT toast XML | `osascript -e 'display notification ...'` | `notify-send` shellout | HTTP POST |
| Title + body | yes | yes | yes | yes (Title header) |
| `--urgency low` | silent toast | silent | `-u low` | priority 2 |
| `--urgency normal` | default toast | silent | `-u normal` | priority 3 |
| `--urgency critical` | `urgent` scenario | sound `Submarine` | `-u critical` | priority 5 |
| `--icon PATH` | yes (file path) | ignored — bundle required | yes (path or named) | not applicable |
| Cold-start latency | ~400ms (PowerShell startup) | ~50ms | ~5ms | ~100-300ms (network) |

### Windows specifics

The Windows backend creates a per-user Start Menu shortcut at `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Winix Notify.lnk` on first invocation. This is an Action Center requirement — toasts won't display unless the calling process has an `AppUserModelID` (AUMID) that resolves to a Start Menu shortcut. The shortcut is created idempotently (skipped if it already exists) and is harmless to delete; it'll be recreated on the next run.

If the shortcut can't be created (locked-down profile, AV blocking lnk write, COM init failure), `notify` returns a typed backend failure with the captured diagnostic — the toast does not silently no-op. Recovery hint: try running `notify` once interactively, or check Programs folder permissions.

Toasts appear in the standard Action Center / notification flyout. Click behaviour: launches `notify.exe` (the AUMID target) which is a no-op since notify is fire-and-forget.

The PowerShell-shellout latency (~400ms) is the trade-off for not requiring a Windows-only TFM split — modern .NET cannot directly marshal `IInspectable` (the WinRT base interface). A future v2 may migrate to the official WinRT projection if sub-100ms latency becomes important.

### macOS specifics

`osascript` is the only viable path on macOS — `UNUserNotificationCenter` requires a signed app bundle which a loose CLI binary can't provide. As a result, `--icon` is ignored on macOS.

### Linux specifics

Requires `notify-send` (libnotify CLI) on PATH. Install:
- Debian/Ubuntu: `sudo apt install libnotify-bin`
- Fedora: `sudo dnf install libnotify`
- Arch: `sudo pacman -S libnotify`

If `notify-send` is missing, `notify` exits with a clear install hint.

Headless / SSH usage: pair with `--no-desktop --ntfy TOPIC` since libnotify needs a D-Bus session.

## ntfy.sh Integration

[ntfy.sh](https://ntfy.sh) is a free, self-hostable pub-sub push notification service. Topics are URL paths — anyone who knows the topic name can publish or subscribe. **Treat the topic name as a password.**

```bash
# Subscribe in browser, app (Android/iOS), or curl:
curl -s https://ntfy.sh/myalerts/json

# Publish:
notify "build done" --ntfy myalerts
```

### Self-hosted ntfy

Self-hosted ntfy supports Bearer-token auth for access control:

```bash
notify "deploy ok" \
    --ntfy deploys \
    --ntfy-server https://ntfy.example.com \
    --ntfy-token tk_xyz
```

Or set globally via env:
```bash
export NOTIFY_NTFY_SERVER=https://ntfy.example.com
export NOTIFY_NTFY_TOKEN=tk_xyz
export NOTIFY_NTFY_TOPIC=deploys
notify "deploy ok"  # uses all three from env
```

### ntfy error diagnostics

When ntfy returns a non-2xx status (401 unauthorized, 403 forbidden, 429 rate-limited, etc.), `notify` reads up to 2 KB of the response body and includes a 512-character snippet in the stderr error message. So a 401 from a self-hosted server with auth misconfigured will surface the server's "topic requires auth" / "rate limited" detail rather than just the bare status code. The body read is bounded to defend against hostile or misconfigured servers returning multi-GB responses.

### Why both desktop and ntfy fire

When you set `NOTIFY_NTFY_TOPIC` and run `notify`, **both** the desktop notification AND the ntfy push fire in parallel. Rationale: at desk → see desktop; stepped out → phone catches it. The env var is the global "I want remote alerts on" switch; per-call `--no-desktop` / `--no-ntfy` are the suppression escape hatches.

## Headless / SSH / CI

Inside an SSH session or CI runner there's no display, so the Linux desktop backend will fail with a D-Bus error. Recommended:

```bash
notify --no-desktop --ntfy alerts "build $BUILD_NUMBER ok"
```

The stderr warning from the failed desktop backend is honest about what happened, and best-effort mode keeps exit 0 as long as ntfy succeeded. Add `--strict` if you want CI to fail when ntfy fails too.

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success — at least one configured backend succeeded. |
| 1 | `--strict` mode and at least one backend failed. |
| 125 | Usage error — bad flags, missing TITLE, no backends configured. |
| 126 | All backends failed, or runtime error (generic catch-all). |

Exit `1` is reserved for `--strict`-mode failures and never used for runtime errors. Scripts of the shape `notify ... --strict || alert-loud` can rely on `1` meaning "at least one delivery channel failed" — runtime crashes return `126` instead.

### Concurrent backend isolation

When multiple backends run in parallel (desktop + ntfy), one backend throwing an exception can no longer corrupt the batch — the dispatcher converts any backend's exception to a per-backend `BackendResult` with a clear "violated never-throw contract" diagnostic. So a typo'd `--ntfy-server` URL won't mask a successful desktop notification with a process-crash error. Cooperative cancellation (the internal 15-second timeout) is also converted to per-backend "cancelled before completion" results so partial successes are preserved.

## Differences from `notify-send`

- Cross-platform — same flag surface on Windows, macOS, Linux.
- ntfy.sh integration — push to phone in the same call.
- `--json` for machine-parseable output.
- `--describe` for AI-agent discovery.

## Related Tools

- [`timeit`](../timeit/README.md) — `timeit slow-cmd && notify "done"`
- [`peep`](../peep/README.md) — watch + notify on completion
- [`retry`](../retry/README.md) — retry with notification on final failure
- [`clip`](../clip/README.md) — `notify --json | jq -r .title | clip`
- Windows-only AI-agent alternative: [Toasty](https://github.com/shanselman/toasty)

## See Also

- `man notify` (after `winix install man`)
- `notify --describe` for JSON metadata
- [ntfy.sh](https://ntfy.sh) — the push notification service

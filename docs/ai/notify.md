# notify — AI Agent Guide

## What This Tool Does

`notify` sends a desktop notification and/or a push to ntfy.sh in a single CLI call, with consistent UX across Windows, macOS, and Linux. Single AOT native binary. When both backends are configured, both fire in parallel.

## When to Use This

- Notify yourself when a long-running command completes: `make test; notify "tests done"`
- Page yourself remotely (phone/web) when away from the desk: `notify --ntfy mytopic "build failed"`
- Cross-platform desktop notifications without per-OS backends in your scripts
- Headless/CI alerts via ntfy.sh: `notify --no-desktop --ntfy ci "build $BUILD_ID ok"`
- Compose with other Winix tools: `timeit slow-cmd && notify "done"`, `retry --until 0 cmd && notify "ok"`

## When NOT to Use This

- For interactive prompts or dialogs — `notify` is fire-and-forget; no input or click handlers.
- For persistent notifications with action buttons — `--actions` is out of scope for v1.
- Where you need real-time push to thousands of users — that's what ntfy.sh itself is for; `notify` is a publisher, not a subscriber/server.

## Basic Invocation

```bash
notify "build done"
notify "tests done" "5 of 200 failed"
notify "deploy failed" --urgency critical
notify "low priority info" --urgency low
notify "see details" --icon /path/to/icon.png
```

## ntfy.sh Push Notifications

Three ways to enable ntfy:

```bash
# 1. Per-call flag
notify "alert" --ntfy myalerts

# 2. Env variable (applies to all calls in the shell)
export NOTIFY_NTFY_TOPIC=myalerts
notify "build done"

# 3. Self-hosted ntfy with Bearer auth
notify "deploy ok" \
    --ntfy deploys \
    --ntfy-server https://ntfy.example.com \
    --ntfy-token tk_xyz
```

When ntfy is configured, **both** desktop and ntfy fire in parallel — at desk you see desktop, away you see phone. Suppress per-call with `--no-desktop` or `--no-ntfy`.

**Topic naming:** ntfy.sh topics are public — anyone who knows the topic name can subscribe or publish. Treat it as a password. For sensitive use, self-host ntfy and use Bearer tokens.

## Subscribing to ntfy

Users (humans) subscribe via:
- Browser: `https://ntfy.sh/<topic>`
- Mobile app: ntfy app on Android (F-Droid + Play Store) and iOS
- CLI: `curl -s https://ntfy.sh/<topic>/json` (streaming JSON)

## JSON Output

```bash
notify "json check" --json
```

Shape:

```json
{
  "title": "json check",
  "body": "optional body",
  "urgency": "normal",
  "backends": [
    {"name": "windows-toast", "ok": true},
    {"name": "ntfy", "ok": true, "server": "https://ntfy.sh", "topic": "myalerts"}
  ]
}
```

Backend statuses are reported in input order. Failed backends include `"error": "..."`.

## Platform Notes

| Platform | Implementation | Latency | Caveats |
|---|---|---|---|
| Windows | Inline PowerShell + WinRT toast XML | ~400ms | Creates a Start Menu shortcut on first run (AUMID requirement). |
| macOS | `osascript` shellout | ~50ms | `--icon` ignored (no app bundle). |
| Linux | `notify-send` shellout | ~5ms | Requires `libnotify-bin` / `libnotify` installed. |
| ntfy | HTTP POST | network | Free hosted at ntfy.sh; self-hosting supported. |

## Best-effort vs Strict

**Default (best-effort):** exit 0 if at least one backend succeeded. Useful when notifications are advisory — you don't want a flaky network or missing libnotify to break your script.

**`--strict`:** exit 1 if any backend failed. Useful for CI alerting where missing the message is a real problem.

```bash
# Best-effort: scripted reminders
notify "scrape done" --ntfy myalerts

# Strict: CI alert that must arrive
notify "deploy failed" --no-desktop --ntfy on-call --strict
```

## Composability

```bash
# Long-running task → notify
make test && notify "tests done"

# Pair with timeit (timing + notify)
timeit slow-script.sh && notify "done in $?"

# Pair with retry (alert on final failure)
retry --times 5 -- flaky-cmd || notify "fail after 5 retries" --urgency critical

# Pipe a hash to notify (compose with digest)
digest file.iso | notify "iso integrity check" --body "$(cat -)"

# CI: post-build alert
notify "build $CI_BUILD_ID ok" --no-desktop --ntfy ci-alerts --strict
```

## Headless / SSH / CI

In SSH or CI runners there's no display, so the Linux desktop backend will fail with a D-Bus error. Recommended pattern:

```bash
notify --no-desktop --ntfy alerts "build done"
```

The stderr warning from the failed desktop attempt is honest about what happened, but best-effort mode keeps exit 0 as long as ntfy succeeded.

## Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success — at least one configured backend succeeded. |
| 1 | `--strict` mode and at least one backend failed. |
| 125 | Usage error — bad flags, missing TITLE, no backends configured. |
| 126 | All backends failed. |

## Metadata

Run `notify --describe` for full structured metadata (flags, exit codes, examples, JSON output fields).
Run `notify --help` for human-readable help.

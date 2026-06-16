# runfor

Run a command with a time limit — cross-platform timeout(1) with a graceful Unix kill window.

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/runfor
```

### Winget (Windows, stable releases)

```bash
winget install Winix.RunFor
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.RunFor
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).

## Usage

```
runfor [options] DURATION -- command [args...]
```

Use `--` before commands that take their own dashed flags to prevent runfor from consuming them.

### Examples

```bash
# Abort a request after 30 seconds
runfor 30s -- curl https://example.com

# Cap a test run at 5 minutes
runfor 5m -- dotnet test

# SIGTERM at 10s, SIGKILL 3s later if it ignores it (Unix)
runfor --kill-after 3s 10s -- ./server

# Send SIGINT instead of SIGTERM at the deadline (Unix)
runfor --signal INT 1m -- ./job
```

### Duration Format

DURATION accepts a number followed by a unit suffix: `ms` (milliseconds), `s` (seconds), `m` (minutes), `h` (hours). Examples: `500ms`, `30s`, `5m`, `1h`.

### Output

**Default** (stderr, only when the deadline fires or the child cannot be launched):
```
runfor: timed out after 30.0s: curl
```

**JSON** (`--json`, stderr):
```json
{"tool":"runfor","version":"0.5.0","exit_code":124,"outcome":"timed_out","timed_out":true,"child_exit_code":null,"signal":"TERM","kill_failed":false,"duration_ms":30012}
```

## Options

| Option | Short | Default | Description |
|--------|-------|---------|-------------|
| `--signal NAME` | `-s NAME` | `TERM` | Signal sent at the deadline on Unix: TERM (default), HUP, INT, QUIT, KILL. Ignored on Windows. |
| `--kill-after GRACE` | `-k GRACE` | (none) | Unix: after the deadline signal, wait GRACE then SIGKILL the tree. No-op on Windows (kills immediately). |
| `--json` | | off | JSON output (to stderr) |
| `--color[=auto\|always\|never]` | | auto | Coloured output: auto (default when omitted), always, or never. |
| `--no-color` | | auto | Disable coloured output |
| `--version` | | | Show version |
| `--help` | `-h` | | Show help |
| `--describe` | | | AI/agent metadata (JSON) |

## Platform Behaviour

### Unix (default — coreutils-faithful)

At the deadline, runfor sends `--signal` (default `TERM`) to the direct child and exits 124. A child that **ignores** the signal survives — there is no automatic SIGKILL backstop. This matches `timeout` without `-k`, so existing scripts behave identically.

`--kill-after GRACE` opts into escalation: after the deadline signal, runfor waits GRACE then sends `SIGKILL` to the entire process tree. Use this when the child may ignore `TERM`.

### Windows

runfor kills the entire process tree immediately at the deadline using `TerminateProcess`. There is no signal model on Windows; `--signal` and `--kill-after` are accepted but have no effect.

## Exit Codes

| Code | Meaning |
|------|---------|
| 0–123 | Child exited 0 before the deadline (or forwarded child code 1–123) |
| 124 | Deadline exceeded — the child was terminated |
| 125 | Usage error: missing/invalid DURATION, no command, bad `--signal`/`--kill-after` |
| 126 | Command not executable |
| 127 | Command not found on PATH |
| 130 | Interrupted by Ctrl+C |

## Limitations

runfor signals only the **direct child** (ADR D10). A child that handles the signal and exits within the `--kill-after` grace may leave its own **grandchildren** running — the SIGKILL tree-backstop only reaps the whole tree when the child **ignores** the signal past grace. For a wrapper that spawns long-lived workers, have it forward the signal to its children.

## Colour

- Automatic: colour when outputting to a terminal, plain when piped
- `--color` forces colour on (overrides `NO_COLOR`)
- `--no-color` forces colour off
- Respects the `NO_COLOR` environment variable ([no-color.org](https://no-color.org))

## Part of Winix

`runfor` is part of the [Winix](../../README.md) CLI toolkit.

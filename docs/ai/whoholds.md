# whoholds — AI Agent Guide

## What This Tool Does

`whoholds` finds which processes are holding a file lock or binding a network port. Given a file path or port number it returns a list of process names, PIDs, and owners. Use it to diagnose "file is in use" or "address already in use" errors, or to automate terminating lock holders via pipes.

## Platform Story

Cross-platform, but the Windows story is the primary value. On **Windows**, no built-in command exposes file lock holders; `handle.exe` (Sysinternals) requires a manual download and admin rights. `whoholds` uses the Restart Manager API (no admin required for current-user processes) and the IP Helper API for port queries, and ships as a single native binary. On **Linux/macOS**, `whoholds` delegates to `lsof`, which ships with the OS on most distributions and macOS.

## When to Use This

- A deployment fails because a DLL or config file is locked: `whoholds myapp.dll`
- A server won't start because a port is already bound: `whoholds :8080`
- An automated script needs to kill all holders before replacing a file: `whoholds myfile --pid-only | wargs kill -f {}`
- A pipeline needs structured output for further processing: `whoholds :443 --json`

## Common Patterns

**Find what holds a file:**
```bash
whoholds myapp.dll
```

**Find what is bound to a port:**
```bash
whoholds :8080
```

**Unambiguous port query (leading colon):**
```bash
whoholds :443
```

**Bare number (port if no file named "8080" exists):**
```bash
whoholds 8080
```

**Emit PIDs only (for piping):**
```bash
whoholds myapp.dll --pid-only
```

**Kill all lock holders:**
```bash
whoholds myapp.dll --pid-only | wargs kill -f {}
```

**Machine-readable JSON:**
```bash
whoholds :8080 --json
```

**Structured metadata:**
```bash
whoholds --describe
```

## Composing with Other Tools

**whoholds + wargs** — kill all holders then proceed:
```bash
whoholds myapp.dll --pid-only | wargs taskkill /PID {} /F
```

**whoholds + jq** — filter JSON output:
```bash
whoholds :8080 --json | jq '.[].name'
```

**whoholds + timeit** — measure how long a file stays locked:
```bash
timeit -- sh -c 'until [ -z "$(whoholds myfile --pid-only)" ]; do sleep 0.5; done'
```

## Auto-Detection Logic

`whoholds` determines whether the argument is a file or port target in this order:

1. **Leading `:` prefix** — always a port query. `:8080`, `:443`.
2. **Contains a path separator** (`/` or `\`) — always a file query.
3. **Exists as a file on disk** — treated as a file query.
4. **Bare number with no matching file** — treated as a port query.

The `:` prefix is the most reliable way to query a port when you don't want to risk ambiguity.

## Elevation Warning

`whoholds` always prints a warning to stderr when not running as administrator:

```
Warning: not running as administrator — results may be incomplete.
```

Without elevation, the Restart Manager API only sees current-user processes. A file locked by a system service or another user won't appear. If you see zero holders but the error persists, re-run elevated.

## Output Format

**Default (human-readable):**
```
myapp.dll is held by:
  Visual Studio (PID 1234, user DESKTOP\troy)
  MsBuild.exe  (PID 5678, user DESKTOP\troy)
```

**`--pid-only`:**
```
1234
5678
```

**`--json`:**
```json
[
  { "pid": 1234, "name": "Visual Studio", "owner": "DESKTOP\\troy", "target": "C:\\path\\myapp.dll" },
  { "pid": 5678, "name": "MsBuild.exe",   "owner": "DESKTOP\\troy", "target": "C:\\path\\myapp.dll" }
]
```

## Gotchas

**Results may be incomplete without elevation.** The elevation warning is always shown on stderr when not running as admin. This is intentional — the most common failure mode is "nothing shows up" when the holder is a system service. Re-run elevated if you see no results but the problem persists.

**`lsof` must be installed on Linux/macOS.** `whoholds` shells out to `lsof` on Unix platforms. On most distributions and macOS it is present by default. If not, install it via the system package manager (`apt install lsof`, `brew install lsof`).

**Port query is a snapshot.** The IP Helper API and `lsof` both take a point-in-time snapshot of the TCP/UDP table. A port that is briefly bound between query and action may not appear. For polling use cases, loop the query rather than relying on a single result.

**Restart Manager is Windows-only.** The file-lock detection on Windows uses the Restart Manager API, which tracks application file usage for Windows Update and installer purposes. It covers most user-mode processes but does not cover kernel-mode drivers or raw file system handles opened without going through the RM registration path. If a file is locked by a kernel driver, `whoholds` will correctly report no user-mode holders.

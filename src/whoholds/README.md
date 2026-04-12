# whoholds

Find which processes are holding a file lock or binding a network port.

**`lsof` / `handle` replacement** for diagnosing "file is locked" or "port is already in use" errors. Shows process name, PID, and owner for every holder. Works without admin rights for files; elevation improves results for ports.

## Install

### Scoop (Windows)

```bash
scoop bucket add winix https://github.com/Yortw/winix
scoop install winix/whoholds
```

### Winget (Windows, stable releases)

```bash
winget install Winix.WhoHolds
```

### .NET Tool (cross-platform)

```bash
dotnet tool install -g Winix.WhoHolds
```

### Direct Download

Download native binaries from [GitHub Releases](https://github.com/Yortw/winix/releases).

## Usage

```
whoholds [options] <target>
```

`<target>` is either a file path or a port specifier. `whoholds` auto-detects which you mean:

- **File path** — any argument containing a path separator, or an argument that names an existing file on disk.
- **`:port`** — leading `:` is unambiguous: `whoholds :8080` always queries port 8080.
- **Bare number** — `whoholds 8080` queries port 8080 only if `8080` is not a file that exists on disk.

### Examples

```bash
# Find what holds a DLL that can't be replaced
whoholds myapp.dll

# Find what is bound to port 8080 (TCP + UDP, IPv4 + IPv6)
whoholds :8080

# Bare number — treated as port if no file named "8080" exists
whoholds 8080

# Emit only PIDs (one per line) — suitable for piping
whoholds myapp.dll --pid-only

# Kill everything holding the file (pipe PIDs to wargs)
whoholds myapp.dll --pid-only | wargs kill -f {}

# Machine-readable output for scripting
whoholds :8080 --json

# Show structured metadata about the tool itself
whoholds --describe
```

## Options

| Option | Description |
|--------|-------------|
| `--pid-only` | Output only PIDs (one per line). Suitable for piping to `wargs` or `kill`. |
| `--json` | Output results as a JSON array. Each entry includes `pid`, `name`, `owner`, and `target`. |
| `--describe` | Output structured tool metadata as JSON (flags, examples, composability). |
| `--color` | Force coloured output (overrides `NO_COLOR`). |
| `--no-color` | Disable coloured output. |
| `--help` | Show help and exit. |
| `--version` | Show version and exit. |

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success — query completed (even if no holders found). |
| 1 | Target not found or query error. |
| 125 | Usage error (bad arguments). |

## Elevation Warning

`whoholds` always prints a warning to stderr when it is not running as administrator. This is deliberate: without elevation, the Restart Manager API (Windows) and `lsof` (Linux/macOS) may only see processes belonging to the current user. A file held by a system service or another user's process will not appear in the results.

The warning reads:

```
Warning: not running as administrator — results may be incomplete.
```

This prevents the frustrating "it says nothing is holding the file, but I still can't delete it" scenario. If you see no holders but the problem persists, re-run elevated.

## Platform Notes

| Platform | Implementation |
|----------|---------------|
| Windows | File locks: Win32 Restart Manager API (no admin needed for current-user processes). Ports: IP Helper `GetExtendedTcpTable` / `GetExtendedUdpTable` (covers TCP + UDP, IPv4 + IPv6). |
| Linux / macOS | Delegates to `lsof`, which must be installed. On most distributions and macOS, `lsof` is present by default. |

## Colour

- Process names are highlighted for quick scanning.
- Elevation warning is shown in yellow (stderr).
- `--no-color` suppresses all ANSI colour output.
- Respects the `NO_COLOR` environment variable ([no-color.org](https://no-color.org)).

## Part of Winix

`whoholds` is part of the [Winix](../../README.md) CLI toolkit.

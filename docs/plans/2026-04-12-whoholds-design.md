# whoholds — Find What's Locking a File or Port

**Date:** 2026-04-12
**Status:** Proposed
**Project:** Winix (`D:\projects\winix`)

---

## Overview

`whoholds` shows which processes are holding a file lock or binding a network port. It answers the two most common "who's using this?" questions on Windows:

- "Why can't I delete/rename this file?" → `whoholds myfile.dll`
- "What's listening on port 8080?" → `whoholds :8080`

**Why it's needed:** Windows has no built-in CLI for this. The workarounds are bad:
- **Files:** Task Manager → Resource Monitor → search. Or Sysinternals Handle.exe (requires admin, ugly output, not redistributable).
- **Ports:** `netstat -ano | findstr :8080` then cross-reference PID with `tasklist`. Painful every time.

**Platform:** Cross-platform with platform-specific backends. On Windows, uses native APIs (Restart Manager, IP Helper) via P/Invoke. On Linux/macOS, delegates to `lsof` and parses its output — the same approach `winix` uses with native package managers. Unified UX across all platforms.

---

## Project Structure

```
src/Winix.WhoHolds/        — class library (finders, formatting, argument parsing)
src/whoholds/              — thin console app (arg parsing via ShellKit, call library, exit code)
tests/Winix.WhoHolds.Tests/ — xUnit tests
```

Standard Winix conventions: library does all work, console app is thin.

---

## Argument Detection

The tool accepts a single positional argument that is either a file path or a port number.

**Resolution order:**
1. If argument starts with `:` → port lookup (explicit, unambiguous). Strip the `:` and parse as integer.
2. If argument exists as a file or directory on disk → file lookup.
3. If argument is a bare integer and no such file exists → port lookup.
4. Otherwise → "file not found" error, exit 1.

The `:` prefix is the unambiguous escape hatch for the rare case where a file is named as a bare number.

```
whoholds myfile.dll      → file lookup
whoholds :8080           → port lookup (explicit)
whoholds 8080            → port lookup (if no file named "8080" exists)
whoholds 8080            → file lookup (if ./8080 exists as a file)
whoholds nosuchfile.txt  → error: file not found
```

---

## Components

### LockInfo

Result type returned by both finders.

```csharp
public sealed class LockInfo
{
    /// <summary>Process ID.</summary>
    public int ProcessId { get; }

    /// <summary>Process name (e.g. "devenv.exe").</summary>
    public string ProcessName { get; }

    /// <summary>
    /// The resource being held. For files: the queried file path.
    /// For ports: "TCP :8080" or "UDP :53".
    /// </summary>
    public string Resource { get; }
}
```

### FileLockFinder

Uses the Windows Restart Manager API to find processes holding a file lock. Does not require admin privileges — only sees processes in the current user's session.

**API sequence:**
1. `RmStartSession` — create a Restart Manager session
2. `RmRegisterResources` — register the file path to query
3. `RmGetList` — retrieve the list of processes holding the resource
4. `RmEndSession` — clean up

```csharp
public static class FileLockFinder
{
    /// <summary>
    /// Returns processes holding a lock on the specified file.
    /// Uses the Restart Manager API — no admin required, but only sees
    /// the current user's processes.
    /// </summary>
    public static List<LockInfo> Find(string filePath);
}
```

**P/Invoke:** `rstrtmgr.dll` — `RmStartSession`, `RmRegisterResources`, `RmGetList`, `RmEndSession`. AOT-compatible via `DllImport` with explicit struct layouts.

**Error handling:** If the API fails (e.g. file doesn't exist at the OS level), returns an empty list. The caller is responsible for checking file existence beforehand and providing a good error message.

### PortLockFinder

Uses the IP Helper API to find processes bound to a TCP or UDP port. Does not require admin privileges.

**API calls:**
- `GetExtendedTcpTable` with `TCP_TABLE_OWNER_PID_ALL` — returns all TCP connections/listeners with PIDs
- `GetExtendedUdpTable` with `UDP_TABLE_OWNER_PID` — returns all UDP bindings with PIDs

```csharp
public static class PortLockFinder
{
    /// <summary>
    /// Returns processes bound to the specified port (TCP and UDP).
    /// Checks both IPv4 and IPv6. No admin required.
    /// </summary>
    public static List<LockInfo> Find(int port);
}
```

**P/Invoke:** `iphlpapi.dll` — `GetExtendedTcpTable`, `GetExtendedUdpTable`. Structures: `MIB_TCPROW_OWNER_PID`, `MIB_UDPROW_OWNER_PID`.

The method scans all rows and filters for the requested port. Returns both TCP and UDP matches. Each result's `Resource` field shows the protocol: `"TCP :8080"` or `"UDP :53"`.

### LsofFinder (Linux/macOS)

Delegates to `lsof` for both file and port lookups on Unix platforms. Parses the output into `LockInfo` results.

```csharp
public static class LsofFinder
{
    /// <summary>
    /// Returns processes holding a lock on the specified file using lsof.
    /// </summary>
    public static List<LockInfo> FindFile(string filePath);

    /// <summary>
    /// Returns processes bound to the specified port using lsof -i.
    /// </summary>
    public static List<LockInfo> FindPort(int port);

    /// <summary>
    /// Returns true if lsof is available on PATH.
    /// </summary>
    public static bool IsAvailable();
}
```

**File lookup:** Runs `lsof <filePath>`, parses tabular output. Each row has COMMAND, PID, USER, FD, TYPE, etc. Extract PID and COMMAND columns.

**Port lookup:** Runs `lsof -i :<port>`, same output parsing.

**Error handling:** If `lsof` is not on PATH, prints a helpful error ("lsof not found — install it via your package manager") and exits 1. Most Linux distros and macOS include it by default.

### ElevationDetector

Checks whether the current process is running elevated (admin).

```csharp
public static class ElevationDetector
{
    /// <summary>Returns true if the process is running with admin privileges.</summary>
    public static bool IsElevated();
}
```

On Windows: uses `WindowsIdentity.GetCurrent()` + `WindowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator)`. AOT-safe.

On Linux/macOS: checks `geteuid() == 0` (running as root). Non-root `lsof` has similar visibility limitations — it only sees the current user's processes.

### Formatting

Output formatting for the console app.

```csharp
public static class Formatting
{
    /// <summary>
    /// Formats results as a human-readable table with PID, process name, and resource columns.
    /// </summary>
    public static string FormatTable(IReadOnlyList<LockInfo> results, bool useColor);

    /// <summary>
    /// Formats results as one PID per line (for piping).
    /// </summary>
    public static string FormatPidOnly(IReadOnlyList<LockInfo> results);

    /// <summary>
    /// Formats the elevation warning shown on stderr when not running as admin.
    /// </summary>
    public static string FormatElevationWarning(bool useColor);

    /// <summary>
    /// Formats the "no results" message.
    /// </summary>
    public static string FormatNoResults(string resource);
}
```

---

## Output Behaviour

**Terminal (stdout is a tty):**
```
⚠ Not elevated — only showing current user's processes.
  PID   Process          Resource
  1234  devenv.exe       D:\projects\winix\bin\tool.dll
  5678  dotnet.exe       D:\projects\winix\bin\tool.dll
```

The `⚠` is yellow when colour is on (`\x1b[33m`). Table goes to stdout. Warning goes to stderr.

**Piped (stdout is not a tty):**
```
1234
5678
```

One PID per line to stdout. Warning still goes to stderr (doesn't pollute pipe).

**`--pid-only` flag:** forces PID-per-line output even on a terminal.

**No results:**
```
No processes found holding D:\projects\winix\bin\tool.dll
```
Written to stderr. Exit code 0 (not an error — the resource just isn't locked).

**Port output (terminal):**
```
  PID   Process          Resource
  4321  node.exe         TCP :8080
  8765  dotnet.exe       TCP :5000
```

---

## Elevation Warning

Always shown on stderr when the process is not elevated. One line:

```
⚠ Not elevated — only showing current user's processes.
```

- Yellow `⚠` when colour is on, plain when off.
- Written to stderr — never pollutes piped stdout.
- Suppressed when `--no-color` and `--quiet` are both set? No — always show it. The user needs to know their view is incomplete. It's one line to stderr.
- When elevated: no warning shown.

---

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success (results found, or no results but query succeeded) |
| 1 | Error (file not found, API failure, unsupported platform) |
| 125 | Usage error (bad arguments) |

---

## CLI Flags

| Flag | Description |
|------|-------------|
| `--pid-only` | Force one-PID-per-line output (auto when piped) |
| `--color` | Force coloured output |
| `--no-color` | Disable coloured output |
| `--json` | Structured JSON output to stderr |
| `--describe` | AI agent metadata |
| `-h`, `--help` | Show help |
| `--version` | Show version |

No `--system` flag in v1 — the Restart Manager and IP Helper APIs don't need elevation for the common case. `--system` (NtQuerySystemInformation for files) is a v2 feature.

---

## Testing Strategy

**Unit-testable (class library):**
- `ArgumentParser` — file vs port detection, `:` prefix, bare number, file existence check
- `Formatting` — table output, PID-only output, elevation warning, no-results message
- `LockInfo` — construction and properties

**Integration-testable (Windows-only):**
- `FileLockFinder` — create temp file, open `FileStream` (lock), query, verify own PID appears
- `PortLockFinder` — bind `TcpListener` on random port, query, verify own PID appears

Integration tests use `[Fact]` with `Skip` on non-Windows (or `[PlatformSpecific(TestPlatforms.Windows)]` if available).

**Manual testing:**
- Elevation warning appearance
- Output mode auto-detection (terminal vs pipe)
- Cross-process lock detection

---

## Scope Boundaries

**In scope (v1):**
- File lock detection via Restart Manager API
- Port binding detection via IP Helper API (TCP + UDP, IPv4 + IPv6)
- Auto-detect file vs port from argument
- Table output (terminal) / PID-only (piped) with `--pid-only` override
- Elevation warning on stderr
- No admin required

**Out of scope (v1):**
- `--system` flag for NtQuerySystemInformation (admin-only, sees all processes)
- Named pipes, mutexes, registry key locks
- Multiple file arguments / glob patterns
- Killing processes (compose with `wargs taskkill /PID`)
- Direct kernel API on Linux/macOS (delegates to `lsof` instead)

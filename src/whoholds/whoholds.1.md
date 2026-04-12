% WHOHOLDS(1) Winix | User Commands
% Troy Willmot
% 2026-04-12

# NAME

whoholds - find which processes are holding a file lock or binding a network port

# SYNOPSIS

**whoholds** [*options*] *target*

# DESCRIPTION

Shows the process name, PID, and owner for every process holding a file lock or binding a network port. Useful for diagnosing "file is locked" or "port is already in use" errors.

_target_ is either a file path or a port specifier. **whoholds** auto-detects which you mean:

- **File path** — any argument containing a path separator, or an argument that names an existing file on disk.
- **:port** — a leading **:** is unambiguous: **whoholds :8080** always queries port 8080.
- **Bare number** — treated as a port only if a file with that name does not exist on disk.

Without elevation, only processes belonging to the current user are visible. A warning is printed to stderr when not running as administrator.

# OPTIONS

**--pid-only**
:   Output only PIDs, one per line. Suitable for piping to **wargs** or **kill**.

**--full-path**, **-l**
:   Show the full executable path instead of just the process name. Requires elevation for system processes.

**--json**
:   Output results as a JSON object. Each process entry includes **pid**, **name**, **path**, **state**, and **resource**.

**--describe**
:   Output structured tool metadata as JSON (flags, examples, composability) and exit.

**--color**
:   Force coloured output, overriding **NO_COLOR**.

**--no-color**
:   Disable coloured output.

**--help**
:   Show help and exit.

**--version**
:   Show version and exit.

# EXIT CODES

**0**
:   Success — query completed (even if no holders were found).

**1**
:   Target not found or query error.

**125**
:   Usage error (bad arguments).

# ENVIRONMENT

**NO_COLOR**
:   If set, disables coloured output (no-color.org).

# ELEVATION

**whoholds** always prints a warning to stderr when not running as administrator. Without elevation, the Restart Manager API (Windows) and **lsof** (Linux/macOS) may only see processes belonging to the current user. A file held by a system service or another user's process will not appear in results.

When elevated, file lock detection and port binding detection cover all processes, and **--full-path** returns paths for system processes.

# PLATFORM NOTES

On Windows, file locks are detected via the Win32 Restart Manager API. Port bindings are detected via the IP Helper **GetExtendedTcpTable** / **GetExtendedUdpTable** APIs (TCP + UDP, IPv4 + IPv6).

On Linux and macOS, **whoholds** delegates to **lsof**, which must be installed. **lsof** is present by default on most distributions and macOS.

# EXAMPLES

    whoholds myapp.dll

    whoholds :8080

    whoholds 8080

    whoholds myapp.dll --pid-only

    whoholds myapp.dll --pid-only | wargs kill -f {}

    whoholds :8080 --json

    whoholds --describe

# SEE ALSO

**wargs**(1), **files**(1), **man**(1)

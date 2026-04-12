# ADR: Winix WhoHolds

**Date:** 2026-04-12
**Status:** Proposed
**Related design:** `2026-04-12-whoholds-design.md`

---

## 1. Restart Manager API for file lock detection (Windows)

**Context:** The primary use case — "which process is preventing me from deleting or replacing this file?" — requires querying file lock holders without necessarily having administrator rights. Windows exposes several mechanisms for this: `NtQuerySystemInformation` (kernel, requires admin), the undocumented handle enumeration path, and the Restart Manager API introduced in Vista to support Windows Update and installer restarts.

**Decision:** Use the Win32 Restart Manager API (`RmRegisterResources`, `RmGetList`) for file lock detection on Windows.

**Rationale:** Restart Manager was designed specifically for this purpose — discovering which applications hold a file so they can be requested to close before an in-place replacement. It requires no administrator rights for processes belonging to the current user, is stable and documented, and is already used by installers and Windows Update for exactly this scenario.

**Trade-offs Accepted:** Only sees processes belonging to the current user without elevation. A file held by a system service or another user's process will not appear. This is mitigated by the always-on elevation warning (see decision 5).

**Options Considered:**
- *`NtQuerySystemInformation` with handle enumeration:* rejected — requires admin rights for full results and relies on undocumented kernel structures that can change between Windows versions.
- *Sysinternals Handle / OpenFiles:* rejected — external dependencies, not suitable for a redistributable tool.

---

## 2. IP Helper API for port binding detection (Windows)

**Context:** Diagnosing "address already in use" errors requires knowing which process is bound to a given TCP or UDP port. Windows provides this information via the IP Helper API, which is accessible without administrator rights.

**Decision:** Use `GetExtendedTcpTable` and `GetExtendedUdpTable` from the IP Helper API (`iphlpapi.dll`) for port binding queries on Windows.

**Rationale:** The IP Helper API covers TCP and UDP on both IPv4 and IPv6 in a single consistent interface, requires no elevation, and is fully documented. It returns owner PIDs directly, so no secondary handle enumeration is needed.

**Trade-offs Accepted:** The TCP/UDP table is a point-in-time snapshot. A process that binds a port briefly between the query and any subsequent action will not appear in results. For the diagnostic use case this is acceptable — transient bindings are not the intended target.

**Options Considered:**
- *`netstat -ano` output parsing:* rejected — fragile text parsing, locale-sensitive, slower, and adds a subprocess dependency.
- *`GetTcpTable` / `GetUdpTable` (without Extended):* rejected — the non-extended variants do not return owner PID information, which is the core requirement.

---

## 3. lsof delegation on Linux/macOS

**Context:** Linux and macOS both have `lsof` — a mature, ubiquitous tool that can list open files and bound ports by process. Building a native implementation would require platform-specific P/Invoke for `/proc` (Linux) and `libproc` / `sysctl` (macOS), significantly increasing implementation complexity and test surface.

**Decision:** On Linux and macOS, shell out to `lsof` rather than building a native implementation.

**Rationale:** `lsof` is present by default on macOS and available on virtually all Linux distributions. It handles both file locks and port bindings, supports JSON-like structured output via flags, and is actively maintained. Delegating to it keeps the codebase small and lets `lsof` handle all the platform-specific edge cases it has accumulated over decades.

**Trade-offs Accepted:** Creates a runtime dependency on `lsof`. If `lsof` is not installed, `whoholds` will fail with a clear error message directing the user to install it. This is an acceptable trade-off given `lsof`'s near-universal availability on Unix platforms.

**Options Considered:**
- *Native `/proc` parsing on Linux:* rejected — Linux-only, fragile, requires extensive testing across kernel versions and container environments.
- *`libproc` / `sysctl` on macOS:* rejected — macOS-only, complex, and duplicates functionality that `lsof` already covers correctly.
- *Separate implementations per Unix platform:* rejected — doubles the Unix complexity for no meaningful user benefit over `lsof` delegation.

---

## 4. File-first auto-detection for ambiguous arguments

**Context:** A bare argument like `8080` is ambiguous — it could be a file named `8080` in the current directory or a port number. The tool needs a deterministic, user-intuitive resolution strategy. Getting this wrong silently (querying a port when the user meant a file, or vice versa) is a frustrating failure mode.

**Decision:** Resolve ambiguous arguments in this order: (1) a leading `:` prefix is always a port; (2) an argument containing a path separator is always a file; (3) an argument that names an existing file on disk is treated as a file; (4) a bare number with no matching file is treated as a port.

**Rationale:** A file-not-found failure is a worse experience than an unexpected port query — the user likely knows whether a file exists in the current directory, and will quickly notice if they get port results when expecting file results. The `:` prefix provides an unambiguous escape hatch that makes the intent explicit in scripts and pipelines.

**Trade-offs Accepted:** A bare number that happens to be both an existing file name and a port will always resolve as a file. Users who want to query a port in that situation must use the `:` prefix. This is the correct trade-off: files are more commonly the intent for short arguments without a separator.

**Options Considered:**
- *Port-first resolution:* rejected — querying a port for a file path that doesn't yet exist (pre-creation) is a confusing failure mode; file paths are the more common argument type.
- *Require explicit `--file` / `--port` flags for disambiguation:* rejected — adds friction for the common case where the intent is unambiguous; reserved as a future option if ambiguity proves problematic in practice.

---

## 5. Always-on elevation warning

**Context:** Without administrator rights, Restart Manager only returns processes belonging to the current user, and `lsof` on some systems requires sudo for full results. A user who runs `whoholds` without elevation, sees zero results, and concludes "nothing holds the file" will waste time retrying other approaches — not realising the tool simply couldn't see the holder.

**Decision:** Always print a warning to stderr when `whoholds` is not running as administrator (Windows: `IsUserAnAdmin`; Linux/macOS: `getuid() != 0`).

**Rationale:** The "I see nothing but the file is still locked" confusion is the single most damaging failure mode for this tool. The warning is cheap (one line to stderr, suppressed by `--no-color` if desired) and prevents a class of frustrating debugging sessions. Printing it only when results are empty is insufficient — users need to know about the limitation upfront so they can decide whether to elevate before acting on zero results.

**Trade-offs Accepted:** The warning appears even when results are complete (i.e., when the holder is a current-user process). This is intentional — the tool cannot know whether there are additional elevated holders it didn't see.

**Options Considered:**
- *Show warning only when result set is empty:* rejected — by the time the user acts on empty results it is too late; the warning needs to set expectations before they interpret the output.
- *Suppress warning if all visible processes are current-user:* rejected — cannot distinguish "no other holders exist" from "other holders exist but are invisible without elevation."
- *Require `--force` to run without elevation:* rejected — too high a barrier for the common case; the tool should be frictionless for most uses.

---

## Decisions Explicitly Deferred

| Topic | Why Deferred |
|-------|-------------|
| `NtQuerySystemInformation` full handle enumeration | Requires admin, relies on undocumented kernel structures; Restart Manager covers the primary use case without these risks |
| Multiple file/port arguments in a single invocation | Adds output grouping complexity; single-target queries cover all current use cases |
| Killing lock holders directly (`--kill` flag) | Destructive action; composing with `wargs` via `--pid-only` is safer and more composable |
| Named pipes and mutex enumeration | Niche use case; requires separate Win32 enumeration paths not covered by Restart Manager |
| `--watch` / polling mode | Useful for "wait until file is released" scripts; can be built as a follow-up using the existing finder infrastructure |

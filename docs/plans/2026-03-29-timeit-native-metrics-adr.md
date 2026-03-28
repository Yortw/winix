# ADR: Native Process Metrics via P/Invoke

**Date:** 2026-03-29
**Status:** Accepted
**Related design:** `2026-03-29-timeit-native-metrics-design.md`

---

## 1. Replace System.Diagnostics.Process metrics with platform-native APIs

**Context:** `Process.TotalProcessorTime` and `Process.PeakWorkingSet64` are unreliable on .NET 10 — they throw `InvalidOperationException` for short-lived processes due to a race between process exit and metric reads. Additionally, `TotalProcessorTime` only provides combined CPU time, not the user/system split that `time` users expect.

**Decision:** Use platform-native APIs: `GetProcessTimes` + `GetProcessMemoryInfo` on Windows, `getrusage(RUSAGE_CHILDREN)` on Linux and macOS.

**Rationale:** Native APIs provide reliable post-exit access. On Windows, the process handle keeps the kernel object alive. On Unix, `getrusage(RUSAGE_CHILDREN)` is designed for post-reap measurement. Both approaches also provide the user/system CPU split.

**Trade-offs Accepted:** Three platform implementations to maintain. Code compiles on all platforms but each path is only testable on its target OS.

**Options Considered:**
- *Stay with System.Diagnostics.Process:* rejected — race condition is a known .NET 10 regression with no managed workaround.
- *Use `wait4` to replace `Process.WaitForExit()`:* rejected — too invasive; conflicts with .NET's SIGCHLD handling. `getrusage` delta achieves the same result without replacing the wait mechanism.
- *`/proc/[pid]/stat` on Linux:* rejected — `/proc` entries disappear after reap, same race condition as the managed API.

## 2. Use `getrusage(RUSAGE_CHILDREN)` delta on both Linux and macOS

**Context:** Linux and macOS both need post-reap metrics. `getrusage(RUSAGE_CHILDREN)` is available on both via libc/libSystem. An alternative on macOS is `proc_pid_rusage`, which requires a valid PID (unreliable after reap).

**Decision:** Use `getrusage(RUSAGE_CHILDREN)` on both Unix platforms, taking a delta (pre-spawn baseline minus post-wait snapshot).

**Rationale:** Same API on both platforms minimises divergence. The delta approach isolates the child's metrics from any other children (though timeit only spawns one).

**Trade-offs Accepted:** `ru_maxrss` units differ between platforms (KB on Linux, bytes on macOS) — each platform file handles its own conversion. Separate `RUsage` struct definitions per platform due to ABI differences.

**Options Considered:**
- *`proc_pid_rusage` on macOS:* rejected — requires valid PID, unreliable after reap.
- *Single shared `RUsage` struct:* rejected — field layout differs between Linux and macOS.

## 3. Make metric fields nullable

**Context:** When a native API fails, the result needs to distinguish "measured zero" from "couldn't measure."

**Decision:** `TimeItResult` uses `TimeSpan?` for CPU times and `long?` for peak memory. Human output shows `N/A`, JSON shows `null`.

**Rationale:** Nullable types make the distinction type-safe. `TimeSpan.Zero` is a legitimate measurement (very short process). Sentinel values (using 0 for "unavailable") are an antipattern that misleads consumers.

**Trade-offs Accepted:** Callers must handle null. JSON consumers see nullable fields. In practice, null should be extremely rare.

**Options Considered:**
- *Use zero as sentinel:* rejected — conflates "measured zero" with "failed to measure."
- *Omit field from JSON when null:* rejected — inconsistent schema is harder for typed consumers.

## 4. Match POSIX `time` output convention

**Context:** The v1 output showed a single `cpu` line. With user/system split available, the output format needs updating.

**Decision:** Replace the `cpu` line with `user` and `sys` lines, matching the POSIX `time` format. JSON includes `cpu_seconds` as a computed total alongside `user_cpu_seconds` and `sys_cpu_seconds`.

**Rationale:** `time` is the established convention. Users of timing tools expect `real` / `user` / `sys`. The `real` line already serves the "how long did it take overall" role, making a combined CPU total redundant in human output.

**Trade-offs Accepted:** Breaking change to output format. Acceptable — no external consumers yet.

**Options Considered:**
- *Keep combined `cpu` line, add user/sys as optional detail:* rejected — adds a CLI flag for something `time` handles cleanly by default.
- *Show all three (cpu, user, sys):* rejected — redundant; `cpu` is always `user + sys`.

## 5. Organise platform code as partial class files with runtime dispatch

**Context:** Platform-specific P/Invoke code needs a home. Options ranged from `#if` conditional compilation to interface-based abstraction.

**Decision:** Partial class `NativeMetrics` with one file per platform plus a common dispatch file. Runtime dispatch via `RuntimeInformation.IsOSPlatform()`. All files compile on all platforms.

**Rationale:** Clean file separation without abstraction overhead. Runtime dispatch avoids cross-compilation issues (conditional compilation requires the build to know the target platform, which breaks `dotnet build` without a RID). The AOT trimmer eliminates unreachable platform code during RID-specific publish anyway.

**Trade-offs Accepted:** All platform code is compiled into non-AOT builds. Negligible cost — the code is small.

**Options Considered:**
- *Conditional compilation (`#if`):* rejected — breaks `dotnet build` without a RID, complicates cross-compilation.
- *Interface + DI:* rejected — unnecessary abstraction for a static utility with no testability benefit (integration tests are the right level).
- *Single file with runtime checks:* rejected — file would grow unwieldy with three platforms' interop structs.

---

## Decisions Explicitly Deferred

| Topic | Why deferred |
|-------|-------------|
| FreeBSD / other Unix support | No demand; same `getrusage` pattern would work if needed later |
| `wait4` replacement for `Process.WaitForExit()` | `getrusage` delta solves the problem without the complexity |
| Per-thread CPU time | Process-level user/sys is sufficient for a `time` replacement |
| Private bytes / commit size metrics | Peak working set matches `time`'s `maxrss`; other metrics can be added later |

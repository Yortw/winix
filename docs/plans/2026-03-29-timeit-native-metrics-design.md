# timeit — Native Process Metrics via P/Invoke

**Date:** 2026-03-29
**Status:** Design approved
**Project:** Winix (`D:\projects\winix`)
**Parent doc:** `2026-03-28-timeit-design.md`

## Purpose

Replace `System.Diagnostics.Process` metric properties with platform-native APIs to:

1. **Split CPU time into user and system** — matching the POSIX `time` convention (`user` / `sys`) instead of the current combined `cpu` total.
2. **Fix the peak memory race condition** — `Process.PeakWorkingSet64` throws `InvalidOperationException` on short-lived processes in .NET 10 because the handle is gone before the read. Native APIs provide reliable post-exit access.

## Target Platforms

Windows, Linux, and macOS — the three platforms .NET AOT targets in practice.

## Result Model Changes

### Before

```csharp
public sealed record TimeItResult(
    TimeSpan WallTime,
    TimeSpan CpuTime,
    long PeakMemoryBytes,
    int ExitCode
);
```

### After

```csharp
public sealed record TimeItResult(
    TimeSpan WallTime,
    TimeSpan? UserCpuTime,
    TimeSpan? SystemCpuTime,
    long? PeakMemoryBytes,
    int ExitCode
)
{
    /// <summary>
    /// Total CPU time (user + system). Null if either component is unavailable.
    /// </summary>
    public TimeSpan? TotalCpuTime => (UserCpuTime != null && SystemCpuTime != null)
        ? UserCpuTime.Value + SystemCpuTime.Value
        : null;
}
```

Breaking change to all consumers of `TimeItResult`. Acceptable — no external consumers exist yet.

### Why nullable?

A null metric means "the OS declined to provide it." This is distinct from `TimeSpan.Zero` / `0` which mean "measured and found to be zero." Using nullable types makes this distinction type-safe rather than relying on sentinel values.

In practice, null metrics should be extremely rare. The native APIs we use are reliable when called correctly. The nullability is a safety net for truly exceptional situations, not a routine case.

## NativeMetrics — File Layout

```
src/Winix.TimeIt/
    NativeMetrics.cs              — ProcessMetrics struct, MetricsBaseline struct, dispatch
    NativeMetrics.Windows.cs      — GetProcessTimes + GetProcessMemoryInfo
    NativeMetrics.Linux.cs        — getrusage(RUSAGE_CHILDREN) via LibraryImport
    NativeMetrics.MacOS.cs        — getrusage(RUSAGE_CHILDREN) via LibraryImport
```

Partial class split by file for organization. All files compile on all platforms — runtime dispatch via `RuntimeInformation.IsOSPlatform()`, not conditional compilation. The AOT trimmer eliminates dead platform branches when publishing for a specific RID.

### Public API

```csharp
/// <summary>
/// Raw process metrics collected via platform-native APIs.
/// Null fields indicate the OS did not provide the metric.
/// </summary>
public readonly struct ProcessMetrics
{
    public TimeSpan? UserCpuTime { get; init; }
    public TimeSpan? SystemCpuTime { get; init; }
    public long? PeakMemoryBytes { get; init; }
}

/// <summary>
/// Opaque baseline snapshot for delta-based metric collection (Unix).
/// On Windows this is an empty struct — metrics come from the process handle.
/// </summary>
public readonly struct MetricsBaseline { /* internal fields */ }

public static partial class NativeMetrics
{
    /// <summary>
    /// Captures a pre-spawn baseline. On Unix, snapshots getrusage(RUSAGE_CHILDREN).
    /// On Windows, returns a no-op token.
    /// Call immediately before Process.Start().
    /// </summary>
    public static MetricsBaseline CaptureBaseline();

    /// <summary>
    /// Reads final metrics for the exited child process.
    /// Call after WaitForExit(), before Dispose().
    /// </summary>
    public static ProcessMetrics GetMetrics(Process process, MetricsBaseline baseline);
}
```

### CommandRunner Integration

```csharp
var baseline = NativeMetrics.CaptureBaseline();
process = Process.Start(startInfo);
process.WaitForExit();
stopwatch.Stop();
var metrics = NativeMetrics.GetMetrics(process, baseline);

return new TimeItResult(
    WallTime: stopwatch.Elapsed,
    UserCpuTime: metrics.UserCpuTime,
    SystemCpuTime: metrics.SystemCpuTime,
    PeakMemoryBytes: metrics.PeakMemoryBytes,
    ExitCode: process.ExitCode
);
```

The existing try/catch blocks for `InvalidOperationException` around `Process.TotalProcessorTime` and `Process.PeakWorkingSet64` are removed — `NativeMetrics` handles all error cases internally, returning null for any metric it cannot read.

## Platform Implementations

### Windows (`NativeMetrics.Windows.cs`)

**APIs:**
- `GetProcessTimes(SafeProcessHandle, ...)` — returns `FILETIME` structs for user and kernel time. `FILETIME` ticks are 100-nanosecond intervals, the same unit as `TimeSpan.Ticks`, so `TimeSpan.FromTicks(value)` converts directly.
- `GetProcessMemoryInfo(SafeProcessHandle, ...)` — returns `PROCESS_MEMORY_COUNTERS` with `PeakWorkingSetSize` in bytes.

**Why this works after WaitForExit():** On Windows, `Process.SafeHandle` holds an open handle to the kernel process object. The kernel keeps the object alive (with all its metrics) as long as any handle is open. The handle is released at `Dispose()`, not at `WaitForExit()`. So there is no race.

**P/Invoke:**
```csharp
[LibraryImport("kernel32")]
private static partial bool GetProcessTimes(
    SafeProcessHandle hProcess,
    out long creationTime, out long exitTime,
    out long kernelTime, out long userTime);

[LibraryImport("psapi")]
private static partial bool GetProcessMemoryInfo(
    SafeProcessHandle hProcess,
    out PROCESS_MEMORY_COUNTERS counters, uint size);
```

Uses `LibraryImport` (source-generated marshalling) for AOT compatibility. `SafeProcessHandle` is used directly — no raw `IntPtr`.

**Baseline:** `CaptureBaseline()` returns an empty `MetricsBaseline` — Windows doesn't need a pre-spawn snapshot.

**Error handling:** If either API returns `false`, the corresponding metric(s) are null.

### Linux (`NativeMetrics.Linux.cs`)

**API:** `getrusage(RUSAGE_CHILDREN, out RUsage usage)` via `LibraryImport("libc")`.

`RUSAGE_CHILDREN` (`-1`) returns cumulative resource usage for all terminated-and-reaped children. Since timeit spawns exactly one child, the delta between pre-spawn and post-wait snapshots gives this child's metrics.

**Why this works after WaitForExit():** This is fundamentally different from the Windows approach. On Linux, .NET's `WaitForExit()` calls `waitpid()` which reaps the child — after that, `/proc/[pid]/` is gone. This is the root cause of the current race condition (`Process.TotalProcessorTime` reads `/proc/[pid]/stat` under the hood).

`getrusage(RUSAGE_CHILDREN)` works precisely *because* the child has been reaped — it's designed for post-reap measurement. No race condition possible.

**Struct layout:**
```csharp
[StructLayout(LayoutKind.Sequential)]
private struct TimeVal
{
    public long tv_sec;
    public long tv_usec;
}

[StructLayout(LayoutKind.Sequential)]
private struct RUsage
{
    public TimeVal ru_utime;    // user CPU time
    public TimeVal ru_stime;    // system CPU time
    // ... padding fields to ru_maxrss ...
    public long ru_maxrss;      // peak RSS in kilobytes (Linux)
}
```

**Unit conversion:** `ru_maxrss` is in **kilobytes** on Linux. Multiply by 1024 to get bytes for `PeakMemoryBytes`.

**CPU time delta:**
- `UserCpuTime = post.ru_utime - baseline.ru_utime`
- `SystemCpuTime = post.ru_stime - baseline.ru_stime`

**Peak memory:** `post.ru_maxrss` directly (not a delta — it's already the peak of the largest child).

**Error handling:** If `getrusage` returns `-1`, all metrics from this platform are null.

### macOS (`NativeMetrics.MacOS.cs`)

Same approach as Linux — `getrusage(RUSAGE_CHILDREN)` via `LibraryImport("libSystem")`.

**Key difference:** `ru_maxrss` is in **bytes** on macOS (not kilobytes as on Linux). No multiplication needed.

**RUsage struct layout:** The struct field order and sizes differ slightly between Linux and macOS. Each platform file defines its own `RUsage` struct matching the platform's ABI. Since only one platform file's struct is ever used at runtime (and the trimmer eliminates the others during AOT publish), there's no conflict.

## Output Format Changes

### Default (multi-line, stderr)

```
  real  12.4s
  user   9.1s
  sys    0.3s
  peak  482 MB
  exit  0
```

Replaces the single `cpu` line with `user` + `sys`, matching the POSIX `time` convention. Labels remain dim, values normal brightness.

When a metric is unavailable:
```
  real  12.4s
  user  N/A
  sys   N/A
  peak  N/A
  exit  0
```

### One-line (`-1` / `--oneline`, stderr)

```
[timeit] 12.4s wall | 9.1s user | 0.3s sys | 482 MB peak | exit 0
```

Unavailable metrics show `N/A` in place of the value.

### JSON (`--json`, stderr)

```json
{"wall_seconds":12.400,"user_cpu_seconds":9.100,"sys_cpu_seconds":0.300,"cpu_seconds":9.400,"peak_memory_bytes":505413632,"exit_code":0}
```

- `user_cpu_seconds` and `sys_cpu_seconds` replace the old `cpu_seconds`
- `cpu_seconds` remains as user + system total (convenience for machine consumers)
- Unavailable metrics are `null` (field present, value null — consistent schema)
- `cpu_seconds` is `null` if either user or system is `null`

```json
{"wall_seconds":12.400,"user_cpu_seconds":null,"sys_cpu_seconds":null,"cpu_seconds":null,"peak_memory_bytes":null,"exit_code":0}
```

### Documentation

`--help` output and README should note that metric fields may be `N/A` / `null` when the operating system could not provide them. This is rare in normal operation.

## Error Handling

`NativeMetrics` never throws. If a platform API fails, the affected metric(s) are `null`. This keeps `CommandRunner` simple — it always gets a `ProcessMetrics` back and passes nullable values through to `TimeItResult`.

Possible failure modes:
- Windows: `GetProcessTimes` or `GetProcessMemoryInfo` returns `false` — metric is null
- Linux/macOS: `getrusage` returns `-1` — all metrics from this call are null
- Unsupported platform: all metrics null (graceful degradation)

## AOT Compatibility

- `LibraryImport` is source-generated at compile time — no runtime IL emit, fully AOT-compatible
- `RuntimeInformation.IsOSPlatform()` is an AOT-friendly intrinsic — the trimmer eliminates unreachable platform branches during RID-specific publish
- No reflection, no dynamic loading
- `StructLayout` attributes on interop structs are compile-time metadata

## Testing Strategy

### Formatting tests (unit)

- Update all existing formatter tests: `CpuTime` → `UserCpuTime` + `SystemCpuTime`
- Test `TotalCpuTime` computed property: equals `User + System` when both non-null, null when either is null
- Test null metric display: human formats show `N/A`, JSON shows `null`
- Test zero metric display: human formats show `0.000s` / `0 KB`, JSON shows `0.000` / `0` — distinct from null/N/A

### Integration tests (CommandRunner)

- Update existing tests: assert on `UserCpuTime` and `SystemCpuTime`
- Both should be `>= TimeSpan.Zero` (non-null) for a normal command like `dotnet --version`
- At least one of user/sys should be `> TimeSpan.Zero` for non-trivial commands
- `PeakMemoryBytes` should be `> 0` (non-null) — the race condition should be fixed
- Retain command-not-found and permission-denied tests (unaffected by this change)

### No mock tests for NativeMetrics

It's a static class wrapping P/Invoke. The integration tests via `CommandRunner` validate the full chain. Mocking the OS would test the mock, not the code.

## Explicitly Not In This Change

- **No `wait4` replacement for Process.WaitForExit()** — `getrusage(RUSAGE_CHILDREN)` delta achieves the same reliability without replacing .NET's process management
- **No per-thread CPU time** — process-level user/sys is sufficient
- **No private bytes / commit size** — peak working set matches `time`'s `maxrss` concept
- **No FreeBSD/other Unix** — Windows, Linux, macOS only. Could add later using the same `getrusage` pattern

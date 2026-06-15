using System.Runtime.InteropServices;

namespace Winix.RunFor.Tests;

/// <summary>
/// Cross-platform child-command builders for the runner integration tests. The runner spawns
/// command+args directly (no shell), so each helper returns the executable plus an argument list.
/// </summary>
/// <remarks>
/// Duplicated from <c>Winix.ProcessSupervision.Tests</c> — <c>internal</c> helpers cannot cross
/// test-assembly boundaries, and cross-test-assembly coupling is not worth the complexity.
/// </remarks>
internal static class ChildHelpers
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>A child that exits immediately with the given code.</summary>
    public static (string Command, string[] Args) ExitWith(int code)
    {
        return IsWindows
            ? ("cmd", new[] { "/c", "exit", code.ToString() })
            : ("/bin/sh", new[] { "-c", $"exit {code}" });
    }

    /// <summary>A child that sleeps for the given seconds (used for kill-on-cancel tests).</summary>
    public static (string Command, string[] Args) SleepSeconds(int seconds)
    {
        return IsWindows
            // ping is a portable sleep on Windows: -n N pings ≈ N-1 seconds. Add 1 to compensate.
            ? ("cmd", new[] { "/c", $"ping -n {seconds + 1} 127.0.0.1 > NUL" })
            : ("/bin/sh", new[] { "-c", $"sleep {seconds}" });
    }

    /// <summary>A Unix child that traps the given signal, exits with <paramref name="exitCode"/> on it,
    /// and otherwise sleeps. Used to prove graceful termination (the child exits ITSELF on SIGTERM,
    /// before the SIGKILL backstop). Unix-only — the caller must platform-gate.</summary>
    public static (string Command, string[] Args) TrapSignalThenSleepUnix(string signalName, int exitCode)
    {
        // CRITICAL idiom: `sleep & wait`, NOT a foreground `sleep`. bash/dash defer a trap until the
        // current FOREGROUND command finishes, so `trap …; sleep 120` would not run the trap until the
        // (120s) sleep returned — the signal would appear ignored and the SIGKILL backstop would fire
        // instead (the exact CI failure this idiom fixes). `wait` IS interruptible by traps, so the
        // signal runs the trap promptly and the shell exits with `exitCode`. This also mirrors the
        // real-world caveat that a graceful signal to a shell wrapper does not reach its foreground child.
        return ("/bin/sh", new[] { "-c", $"trap 'exit {exitCode}' {signalName}; sleep 120 & wait" });
    }

    /// <summary>A Unix child that IGNORES the given signal and keeps sleeping. Used to prove the
    /// SIGKILL backstop fires after the grace window. Unix-only — the caller must platform-gate.</summary>
    public static (string Command, string[] Args) IgnoreSignalThenSleepUnix(string signalName)
    {
        return ("/bin/sh", new[] { "-c", $"trap '' {signalName}; sleep 120" });
    }
}

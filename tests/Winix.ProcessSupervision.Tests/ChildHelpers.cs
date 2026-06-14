using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Winix.ProcessSupervision.Tests;

/// <summary>
/// Cross-platform child-command builders for the runner integration tests. The runner spawns
/// command+args directly (no shell), so each helper returns the executable plus an argument list.
/// </summary>
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
}

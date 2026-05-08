#nullable enable

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Winix.Less;

/// <summary>
/// Ensures interactive keyboard input is available even when stdin was piped.
/// When content is read from a pipe (<c>git diff | less</c>), the standard input
/// handle points to the pipe, not the console. <see cref="Console.ReadKey"/> throws
/// <see cref="InvalidOperationException"/> in this state. This helper reopens the
/// console input device so interactive key reading works.
/// </summary>
public static class ConsoleInput
{
    /// <summary>
    /// Reattaches stdin to the console device if it was redirected.
    /// Call this <em>after</em> all piped content has been read and
    /// <em>before</em> entering the interactive pager.
    /// </summary>
    /// <remarks>
    /// Best-effort. On Linux/macOS .NET versions where the runtime caches the original
    /// stdin handle, <see cref="Console.ReadKey"/> may still throw
    /// <see cref="InvalidOperationException"/> after this call. The pager loop catches
    /// that exception and falls back to a direct dump (Pager.cs round-1 fresh-eyes
    /// 2026-05-09 — CR C2 + SFH C03). Reattach failures on Unix are surfaced via stderr
    /// once per process so the user knows interactive paging won't work in this context.
    /// </remarks>
    public static void ReattachIfRedirected()
    {
        if (!Console.IsInputRedirected)
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            ReattachWindows();
        }
        else
        {
            ReattachUnix();
        }
    }

    private static void ReattachWindows()
    {
        // Open CONIN$ — the Windows console input device — and replace the
        // stdin handle so that Console.ReadKey() works again.
        IntPtr handle = CreateFileW(
            "CONIN$",
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (handle != InvalidHandle)
        {
            SetStdHandle(StdInputHandle, handle);
        }
    }

    private static bool _reattachUnixFailureReported;

    private static void ReattachUnix()
    {
        // Open /dev/tty — the controlling terminal — and redirect Console.In.
        // This doesn't fix Console.ReadKey() on all .NET versions, but it's the
        // best we can do without dup2. On Unix, users typically have GNU less
        // available, so this is a fallback path.
        //
        // Round-1 fresh-eyes 2026-05-09 SFH H01: pre-fix this had a bare `catch { }`
        // that swallowed every exception type, including OutOfMemoryException and
        // ThreadAbortException-class. Now narrowed to (IOException,
        // UnauthorizedAccessException, SecurityException) — the documented throws
        // from FileStream + StreamReader. Other exceptions propagate so the pager's
        // top-level handler can route them. On first failure, emit a one-line stderr
        // diagnostic so the user knows interactive paging will be unavailable
        // (deduped via _reattachUnixFailureReported to avoid spam on repeat calls).
        try
        {
            if (File.Exists("/dev/tty"))
            {
                var ttyStream = new FileStream("/dev/tty", FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                Console.SetIn(new StreamReader(ttyStream));
            }
            else
            {
                ReportReattachFailureOnce("/dev/tty does not exist (likely no controlling terminal)");
            }
        }
        catch (IOException)
        {
            ReportReattachFailureOnce("could not open /dev/tty (I/O error)");
        }
        catch (UnauthorizedAccessException)
        {
            ReportReattachFailureOnce("could not open /dev/tty (permission denied)");
        }
        catch (System.Security.SecurityException)
        {
            ReportReattachFailureOnce("could not open /dev/tty (security policy)");
        }
    }

    private static void ReportReattachFailureOnce(string reason)
    {
        if (_reattachUnixFailureReported) { return; }
        _reattachUnixFailureReported = true;
        try
        {
            Console.Error.WriteLine($"less: warning: console input reattach failed ({reason}); interactive paging may be unavailable");
        }
        catch
        {
            // stderr unavailable — diagnostic is best-effort, never blocks.
        }
    }

    // --- Win32 P/Invoke (AOT-compatible via LibraryImport) ---

    private const int StdInputHandle = -10;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 1;
    private const uint FileShareWrite = 2;
    private const uint OpenExisting = 3;
    private static readonly IntPtr InvalidHandle = new IntPtr(-1);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetStdHandle(int nStdHandle, IntPtr hHandle);
}

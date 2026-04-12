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
    /// <summary>
    /// Reattaches stdin to the console device if it was redirected.
    /// Call this <em>after</em> all piped content has been read and
    /// <em>before</em> entering the interactive pager.
    /// </summary>
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

    private static void ReattachUnix()
    {
        // Open /dev/tty — the controlling terminal — and redirect Console.In.
        // This doesn't fix Console.ReadKey() on all .NET versions, but it's the
        // best we can do without dup2. On Unix, users typically have GNU less
        // available, so this is a fallback path.
        try
        {
            if (File.Exists("/dev/tty"))
            {
                var ttyStream = new FileStream("/dev/tty", FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                Console.SetIn(new StreamReader(ttyStream));
            }
        }
        catch
        {
            // If /dev/tty can't be opened, the pager will fall back to
            // quit-if-one-screen or direct stdout output.
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

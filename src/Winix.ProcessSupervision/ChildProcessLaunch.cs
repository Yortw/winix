using System;
using System.ComponentModel;
using Yort.ShellKit;

namespace Winix.ProcessSupervision;

/// <summary>
/// Classifies <see cref="System.Diagnostics.Process.Start()"/> launch failures into the suite's typed exceptions.
/// Extracted from retry so the whole process-supervision family maps launch failures identically.
/// </summary>
public static class ChildProcessLaunch
{
    /// <summary>
    /// Maps a <see cref="Win32Exception"/> raised by <see cref="System.Diagnostics.Process.Start()"/>
    /// to the appropriate typed exception. <see cref="Win32Exception"/> is thrown on all platforms —
    /// .NET maps POSIX <c>errno</c> values onto Win32 error codes on Linux/macOS.
    /// </summary>
    /// <param name="ex">The exception raised by <c>Process.Start</c>.</param>
    /// <param name="command">The command that failed to launch (for the message).</param>
    /// <returns>
    /// A <see cref="CommandNotFoundException"/> (codes 2/3 — ENOENT/path-not-found),
    /// or a <see cref="CommandNotExecutableException"/> (codes 5/13 — access denied, or any
    /// other code such as ERROR_BAD_EXE_FORMAT 193). The caller throws the returned exception.
    /// </returns>
    public static Exception ClassifyWin32(Win32Exception ex, string command)
    {
        // ERROR_ACCESS_DENIED (5) on Windows, EACCES (13) on Linux/macOS → not executable.
        if (ex.NativeErrorCode == 5 || ex.NativeErrorCode == 13)
        {
            return new CommandNotExecutableException(command);
        }

        // ERROR_FILE_NOT_FOUND (2), ERROR_PATH_NOT_FOUND (3), ENOENT (2) → not found.
        if (ex.NativeErrorCode == 2 || ex.NativeErrorCode == 3)
        {
            return new CommandNotFoundException(command);
        }

        // Other errors (ERROR_BAD_EXE_FORMAT 193, etc.). Use the (message, inner) ctor: the
        // single-arg ctor prepends "permission denied: " unconditionally, which is misleading
        // for non-permission errors. The 2-arg ctor preserves the message verbatim and keeps
        // the underlying Win32Exception for diagnostics.
        return new CommandNotExecutableException($"{command}: {ex.Message}", ex);
    }
}

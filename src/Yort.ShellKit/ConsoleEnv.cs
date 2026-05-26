using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Yort.ShellKit;

/// <summary>
/// Terminal environment detection — colour support, pipe detection, NO_COLOR handling.
/// </summary>
public static partial class ConsoleEnv
{
    /// <summary>
    /// Enables ANSI/VT100 escape sequence processing on Windows. No-op on other platforms.
    /// Call once at startup before writing any ANSI codes. Safe to call multiple times.
    /// </summary>
    /// <remarks>
    /// Windows CMD and PowerShell do not process ANSI escape codes by default.
    /// This sets <c>ENABLE_VIRTUAL_TERMINAL_PROCESSING</c> on both the stdout and stderr
    /// console handles, which tells Windows to interpret escape sequences instead of printing
    /// them literally. Both handles are needed because Winix tools write coloured output to
    /// stderr (summary stats, diagnostics). Windows Terminal enables this by default, but CMD
    /// and older PowerShell do not.
    /// </remarks>
    public static void EnableAnsiIfNeeded()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        try
        {
            EnableVtForHandle(StdOutputHandle);
            EnableVtForHandle(StdErrorHandle);
        }
        catch
        {
            // Best effort — if it fails, colours just won't render
        }
    }

    private static void EnableVtForHandle(int handleId)
    {
        nint handle = GetStdHandle(handleId);
        if (handle == InvalidHandle)
        {
            return;
        }

        if (!GetConsoleMode(handle, out uint mode))
        {
            return;
        }

        SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing);
    }

    /// <summary>
    /// Sets <see cref="Console.OutputEncoding"/> and <see cref="Console.InputEncoding"/>
    /// to UTF-8 so non-ASCII output (em-dashes in help text, emoji, CJK) round-trips
    /// correctly through Windows <c>cmd.exe</c> pipes and non-ASCII stdin (e.g. text
    /// copied via <c>clip</c>) decodes correctly. On Windows this calls
    /// <c>SetConsoleCP/SetConsoleOutputCP(65001)</c>. On *nix the setters are effectively
    /// no-ops (the console is already UTF-8). Call alongside
    /// <see cref="EnableAnsiIfNeeded"/> at process startup.
    /// </summary>
    /// <remarks>
    /// Both setters can throw <see cref="IOException"/> when the underlying handle
    /// rejects a code-page change (unusual redirection chains, locked-down containers).
    /// Failing silently is intentional — worst case is the terminal decoding our UTF-8
    /// output in its native code page, which is the same as pre-fix behaviour.
    /// <para/>
    /// Does not rescue the <c>cmd.exe echo X | tool</c> path — cmd replaces non-OEM
    /// chars with <c>?</c> BEFORE piping, so the bytes never reach us. Users still
    /// need <c>chcp 65001</c> in cmd.exe for that case. Git Bash and Windows Terminal
    /// are unaffected.
    /// </remarks>
    public static void UseUtf8Streams()
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch (IOException) { }
        try { Console.InputEncoding = Encoding.UTF8; } catch (IOException) { }
    }

    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;
    private static readonly nint InvalidHandle = new(-1);
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GetStdHandle(int nStdHandle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleMode(nint hConsoleHandle, uint dwMode);

    /// <summary>
    /// Returns true if the <c>NO_COLOR</c> environment variable is set (any value, including empty).
    /// See https://no-color.org.
    /// </summary>
    public static bool IsNoColorEnvSet()
    {
        return Environment.GetEnvironmentVariable("NO_COLOR") is not null;
    }

    /// <summary>
    /// Returns true if the given stream (stdout or stderr) is connected to a terminal, not a pipe.
    /// </summary>
    public static bool IsTerminal(bool checkStdErr)
    {
        return checkStdErr ? !Console.IsErrorRedirected : !Console.IsOutputRedirected;
    }

    /// <summary>
    /// Resolves whether colour output should be used, applying the precedence rules:
    /// explicit flag &gt; NO_COLOR env var &gt; auto-detection (is terminal?).
    /// </summary>
    /// <param name="colorFlag">True if --color was passed.</param>
    /// <param name="noColorFlag">True if --no-color was passed.</param>
    /// <param name="noColorEnv">True if NO_COLOR environment variable is set.</param>
    /// <param name="isTerminal">True if the output stream is a terminal.</param>
    public static bool ResolveUseColor(bool colorFlag, bool noColorFlag, bool noColorEnv, bool isTerminal)
    {
        if (colorFlag)
        {
            return true;
        }

        if (noColorFlag)
        {
            return false;
        }

        if (noColorEnv)
        {
            return false;
        }

        return isTerminal;
    }

    /// <summary>
    /// Returns the terminal height in rows, or 24 if not attached to a terminal.
    /// </summary>
    public static int GetTerminalHeight()
    {
        try
        {
            int height = Console.WindowHeight;
            // Some Linux CI/redirect environments return 0 instead of throwing
            return height > 0 ? height : 24;
        }
        catch
        {
            return 24;
        }
    }

    /// <summary>
    /// Returns the terminal width in columns, or 80 if not attached to a terminal.
    /// </summary>
    public static int GetTerminalWidth()
    {
        try
        {
            int width = Console.WindowWidth;
            // Some Linux CI/redirect environments return 0 instead of throwing
            return width > 0 ? width : 80;
        }
        catch
        {
            return 80;
        }
    }
}

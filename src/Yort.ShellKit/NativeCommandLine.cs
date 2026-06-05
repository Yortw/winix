using System.Runtime.InteropServices;

namespace Yort.ShellKit;

/// <summary>
/// Reads the process's true native command line via <c>GetCommandLineW</c>.
/// </summary>
/// <remarks>
/// On .NET (Core), <c>Environment.CommandLine</c> is NOT the raw command line — it is
/// <c>Environment.GetCommandLineArgs()</c> re-joined with spaces (managed assembly path
/// first, quotes destroyed). Glob-expansion quote suppression needs the bytes the shell
/// actually passed, so this P/Invokes the real thing. Verified by probe 2026-06-05:
/// a child launched as <c>app.exe "*.txt"</c> sees Environment.CommandLine
/// <c>app.dll *.txt</c> (no quotes) but GetCommandLineW <c>"...app.exe" "*.txt"</c>.
/// </remarks>
internal static partial class NativeCommandLine
{
    [LibraryImport("kernel32.dll", EntryPoint = "GetCommandLineW")]
    private static partial IntPtr GetCommandLineWNative();

    /// <summary>
    /// Returns the raw native command line on Windows, or <see langword="null"/> on other
    /// platforms (Unix has no equivalent single-string command line; quoting was resolved
    /// by the shell before exec) or if the OS returns nothing.
    /// </summary>
    internal static string? Get()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        IntPtr ptr = GetCommandLineWNative();
        return ptr == IntPtr.Zero ? null : Marshal.PtrToStringUni(ptr);
    }
}

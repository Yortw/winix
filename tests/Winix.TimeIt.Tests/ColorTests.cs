#nullable enable
using System;
using System.IO;
using System.Linq;
using Winix.TimeIt;
using Xunit;

namespace Winix.TimeIt.Tests;

/// <summary>
/// Regression tests locking timeit's --color emission path.
/// Guards against a future regression where colour is silently unwired from the
/// Cli.Run production path (as occurred in trash/hcat/wargs).
/// </summary>
/// <remarks>
/// Colour path: Cli.Run → Formatting.FormatDefault / FormatOneLine (useColor arg).
/// useColor is resolved via result.ResolveColor(checkStdErr: !useStdout).
/// --color=always forces colour even to a non-TTY StringWriter.
/// Summary goes to stderr by default; --stdout redirects it so stdout can be captured.
/// </remarks>
public class ColorTests
{
    private static (int exit, string stdout, string stderr) RunCli(params string[] args)
    {
        var stdoutWriter = new StringWriter();
        var stderrWriter = new StringWriter();
        int exit = Cli.Run(args, stdoutWriter, stderrWriter);
        return (exit, stdoutWriter.ToString(), stderrWriter.ToString());
    }

    private static string[] QuickCommand()
    {
        // Minimal command that exits 0 quickly on both Windows and POSIX.
        return OperatingSystem.IsWindows()
            ? new[] { "cmd", "/c", "exit 0" }
            : new[] { "sh", "-c", "exit 0" };
    }

    [Fact]
    public void Run_ColorAlways_OutputContainsEscape()
    {
        // --stdout routes the timing summary to stdout so we can capture it through
        // the StringWriter seam; without it the summary goes to stderr.
        var args = new[] { "--color=always", "--stdout" }.Concat(QuickCommand()).ToArray();
        var r = RunCli(args);
        Assert.Equal(0, r.exit);
        Assert.Contains(((char)27).ToString(), r.stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_NoColor_OutputContainsNoEscape()
    {
        var args = new[] { "--no-color", "--stdout" }.Concat(QuickCommand()).ToArray();
        var r = RunCli(args);
        Assert.Equal(0, r.exit);
        Assert.DoesNotContain(((char)27).ToString(), r.stdout, StringComparison.Ordinal);
    }
}

#nullable enable
using System;
using System.IO;
using Winix.When;
using Xunit;

namespace Winix.When.Tests;

/// <summary>
/// Regression tests locking when's --color emission path.
/// Guards against a future regression where colour is silently unwired from the
/// Cli.Run production path (as occurred in trash/hcat/wargs).
/// </summary>
/// <remarks>
/// Colour path: Cli.Run → RunConversionMode → Formatting.FormatDefault(useColor).
/// useColor is resolved via result.ResolveColor(checkStdErr: false).
/// --color=always forces colour even to a non-TTY StringWriter.
/// Output goes to stdout; stderr carries only error messages.
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

    [Fact]
    public void Run_ColorAlways_OutputContainsEscape()
    {
        // "now" → RunConversionMode → Formatting.FormatDefault(..., useColor: true).
        // FormatDefault emits AnsiColor.Dim wrapping "UTC:", "Local:", etc.
        var r = RunCli("now", "--color=always");
        Assert.Equal(0, r.exit);
        Assert.Contains(((char)27).ToString(), r.stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_NoColor_OutputContainsNoEscape()
    {
        var r = RunCli("now", "--no-color");
        Assert.Equal(0, r.exit);
        Assert.DoesNotContain(((char)27).ToString(), r.stdout, StringComparison.Ordinal);
    }
}

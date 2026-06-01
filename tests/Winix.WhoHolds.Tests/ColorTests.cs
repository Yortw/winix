#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Winix.WhoHolds;
using Xunit;

namespace Winix.WhoHolds.Tests;

/// <summary>
/// Regression tests locking whoholds's --color emission path.
/// Guards against a future regression where colour is silently unwired from the
/// Cli.Run production path (as occurred in trash/hcat/wargs).
/// </summary>
/// <remarks>
/// Colour path: Cli.Run → FormatTable(locks, useColor) → AnsiColor.Dim on header row.
/// useColor is resolved via result.ResolveColor(checkStdErr: true) — the elevation
/// warning lands on stderr; the table lands on stdout.
/// --color=always forces useColor=true even to a non-TTY StringWriter.
/// The fake portFinder returns one LockInfo so FormatTable is actually called;
/// --pid-only is NOT passed so the table path (not the pid-only path) is exercised.
/// isStdoutRedirected=false suppresses the auto-pid-only behaviour.
/// isElevated=() => true suppresses the elevation warning (tests stdout only).
/// </remarks>
public sealed class ColorTests
{
    private static readonly string Esc = ((char)27).ToString();

    // One realistic-looking lock result returned by the fake finder.
    private static FindResult OneLockResult { get; } = FindResult.Success(
        new[] { new LockInfo(1234, "testprocess", ":9999", "", "LISTEN") });

    private static (int exit, string stdout, string stderr) RunCli(params string[] args)
    {
        var so = new StringWriter();
        var se = new StringWriter();
        int exit = Cli.Run(
            args,
            so,
            se,
            isStdoutRedirected: false,
            portFinder: _ => OneLockResult,
            fileFinder: _ => FindResult.Empty,
            isElevated: () => true);
        return (exit, so.ToString(), se.ToString());
    }

    [Fact]
    public void Run_ColorAlways_TableHeaderContainsEscape()
    {
        // ":9999" drives the port path → portFinder returns OneLockResult → FormatTable is called.
        // The header row is wrapped in AnsiColor.Dim + AnsiColor.Reset → ESC must appear on stdout.
        var r = RunCli(":9999", "--color=always");
        Assert.Equal(0, r.exit);
        Assert.Contains(Esc, r.stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_NoColor_TableHeaderContainsNoEscape()
    {
        var r = RunCli(":9999", "--no-color");
        Assert.Equal(0, r.exit);
        Assert.DoesNotContain(Esc, r.stdout, StringComparison.Ordinal);
        // The table header text must still be present — confirming plain output, not empty.
        Assert.Contains("PID", r.stdout, StringComparison.Ordinal);
    }
}

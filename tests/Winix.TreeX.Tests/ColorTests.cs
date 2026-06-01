#nullable enable
using System;
using System.IO;
using Winix.TreeX;
using Xunit;

namespace Winix.TreeX.Tests;

/// <summary>
/// Regression tests locking treex's --color emission path.
/// Guards against a future regression where colour is silently unwired from the
/// Cli.Run production path (as occurred in trash/hcat/wargs).
/// </summary>
/// <remarks>
/// Colour path: Cli.Run → TreeRenderer.Render → WriteConnector (AnsiColor.Dim on
/// connector characters), FormatName (AnsiColor.Blue for directories).
/// useColor is resolved via result.ResolveColor(checkStdErr: true).
/// --color=always forces colour even to a non-TTY StringWriter.
/// Tree output goes to stdout; the summary line goes to stderr.
/// A subdirectory in the temp root guarantees at least one connector line is rendered,
/// which always emits AnsiColor.Dim regardless of node type.
/// </remarks>
public sealed class ColorTests : IDisposable
{
    private readonly string _tempDir;

    public ColorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "treex-color-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        // Create a subdirectory so the renderer emits at least one connector line.
        // WriteConnector always wraps connector chars in AnsiColor.Dim — guarantees ESC appears.
        Directory.CreateDirectory(Path.Combine(_tempDir, "child"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static (int exit, string stdout, string stderr) RunCli(params string[] args)
    {
        var stdoutWriter = new StringWriter();
        var stderrWriter = new StringWriter();
        int exit = Cli.Run(args, stdoutWriter, stderrWriter, isStdoutRedirected: false);
        return (exit, stdoutWriter.ToString(), stderrWriter.ToString());
    }

    [Fact]
    public void Run_ColorAlways_OutputContainsEscape()
    {
        // Walk _tempDir — child subdirectory triggers WriteConnector (dim connector)
        // and GetNodeColor (blue directory name), both emitting ESC via AnsiColor.
        var r = RunCli(_tempDir, "--color=always");
        Assert.Equal(0, r.exit);
        Assert.Contains(((char)27).ToString(), r.stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_NoColor_OutputContainsNoEscape()
    {
        var r = RunCli(_tempDir, "--no-color");
        Assert.Equal(0, r.exit);
        Assert.DoesNotContain(((char)27).ToString(), r.stdout, StringComparison.Ordinal);
    }
}

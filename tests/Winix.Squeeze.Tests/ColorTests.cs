#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;
using Winix.Squeeze;
using Xunit;

namespace Winix.Squeeze.Tests;

/// <summary>
/// Regression tests locking squeeze's --color emission path.
/// Guards against a future regression where colour is silently unwired from the
/// Cli.RunAsync production path (as occurred in trash/hcat/wargs).
/// </summary>
/// <remarks>
/// Colour path: Cli.RunAsync → file-mode loop → Formatting.FormatHuman(result, useColor)
/// → AnsiColor.Dim(useColor) + filename + AnsiColor.Reset(useColor) → stderr.WriteLine.
/// The Dim escape applies to the filename unconditionally (not just for high-ratio input),
/// so any successful single-file compression emits ESC on stderr when showStats is true.
/// showStats requires --verbose (or a terminal stdout); tests use --verbose because
/// stdoutIsTerminal is false in test context.
/// useColor is resolved via result.ResolveColor(checkStdErr: true); --color=always
/// forces useColor=true even to a non-TTY StringWriter, overriding NO_COLOR.
/// Colour goes to stderr (the human-summary stream); stdout carries binary payload.
/// </remarks>
public sealed class ColorTests : IDisposable
{
    private readonly string _tempDir;

    public ColorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "squeeze-color-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
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

    private async Task<(int exit, string stderr)> RunCliAsync(params string[] args)
    {
        using MemoryStream stdinStream = new(Array.Empty<byte>());
        using MemoryStream stdoutStream = new();
        var stderrWriter = new StringWriter();
        int exit = await Cli.RunAsync(
            args,
            stdinStream,
            stdoutStream,
            stderrWriter,
            stdinIsRedirected: false,
            stdoutIsTerminal: false);
        return (exit, stderrWriter.ToString());
    }

    [Fact]
    public async Task RunAsync_ColorAlways_SummaryLineContainsEscape()
    {
        // Compress a small file with --verbose to force showStats=true (stdoutIsTerminal=false
        // in test context, so stats are suppressed without --verbose).
        // Formatting.FormatHuman wraps the filename in AnsiColor.Dim + AnsiColor.Reset
        // unconditionally, so any successful compression yields ESC on stderr.
        string input = Path.Combine(_tempDir, "data.txt");
        File.WriteAllText(input, "hello squeeze colour test");

        var r = await RunCliAsync(input, "--verbose", "--color=always");

        Assert.Equal(0, r.exit);
        Assert.Contains(((char)27).ToString(), r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_NoColor_SummaryLineContainsNoEscape()
    {
        string input = Path.Combine(_tempDir, "data2.txt");
        File.WriteAllText(input, "hello squeeze no-colour test");

        var r = await RunCliAsync(input, "--verbose", "--no-color");

        Assert.Equal(0, r.exit);
        // Confirm stats were emitted (summary line present) then assert no ESC.
        Assert.Contains("→", r.stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(((char)27).ToString(), r.stderr, StringComparison.Ordinal);
    }
}

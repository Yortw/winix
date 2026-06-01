#nullable enable
using System;
using System.IO;
using Winix.Files;
using Xunit;

namespace Winix.Files.Tests;

/// <summary>
/// Regression tests locking files's --color emission path.
/// Guards against a future regression where colour is silently unwired from the
/// Cli.Run production path (as occurred in trash/hcat/wargs).
/// </summary>
/// <remarks>
/// Colour path: Cli.Run → walker loop → Formatting.FormatPath(entry, useColor) /
/// Formatting.FormatLong(entry, useColor). FormatPath colours directories blue and
/// symlinks cyan; plain files have no colour. The temp dir always contains at least
/// one directory entry (the subdirectory created in the fixture) to guarantee a
/// coloured output line even under --type f suppression; using the root temp dir
/// itself means the default walk emits the sub-directory as a coloured entry.
/// useColor is resolved via result.ResolveColor() (no checkStdErr override for files).
/// --color=always forces colour even to a non-TTY StringWriter.
/// Output (paths / long-format lines) goes to stdout.
/// </remarks>
public sealed class ColorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _subDir;

    public ColorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "files-color-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        // Create a subdirectory so the walk emits at least one directory entry.
        // Directories are coloured blue by FormatPath — guarantees ESC appears.
        _subDir = Path.Combine(_tempDir, "subdir");
        Directory.CreateDirectory(_subDir);
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
    public void Run_ColorAlways_DirectoryEntryContainsEscape()
    {
        // Walk _tempDir without filters — the subdirectory is emitted first.
        // Formatting.FormatPath(directoryEntry, useColor: true) → AnsiColor.Blue(true) + path + Reset.
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

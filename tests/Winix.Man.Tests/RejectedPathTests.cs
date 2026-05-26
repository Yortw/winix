#nullable enable

using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Winix.Man;
using Xunit;

namespace Winix.Man.Tests;

/// <summary>
/// Tests for round-2 fresh-eyes 2026-05-09 SFH-H4 closure: a candidate man page file
/// rejected by <see cref="PageDiscovery"/>'s structural check (no groff macro line in
/// the first 64 lines) was previously silent — <see cref="PageDiscovery.FindPage"/>
/// returned null, the orchestration layer emitted "no manual entry", and the user
/// gave up. The file was actually present-but-malformed.
/// </summary>
/// <remarks>
/// Fix: <see cref="PageDiscovery.LastRejectedPaths"/> records every candidate that
/// failed the structural peek; <see cref="Cli.Run"/> consults the property and emits
/// a "found candidate(s) but none are valid" diagnostic with exit 125 when discovery
/// returns null but rejections occurred. Genuine "no candidate at all" still emits
/// "no manual entry" with exit 1.
/// </remarks>
public sealed class RejectedPathTests : IDisposable
{
    private readonly string _tempDir;

    public RejectedPathTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "manrejected-" + Guid.NewGuid().ToString("N"));
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
            // Best-effort.
        }
    }

    private static (StringWriter stdout, StringWriter stderr) Sinks()
    {
        return (new StringWriter(), new StringWriter());
    }

    [Fact]
    public void CorruptGzipDecompressableNonGroff_RoutesToInternalErrorWithRejectedPath()
    {
        // SFH-H4 reproducer: a real gzip stream that decompresses cleanly into non-groff
        // plain text. Pre-fix LooksLikeManPage rejected it, FindPage returned null, and
        // Cli.Run emitted "no manual entry" — silent failure. Now the rejection is
        // tracked via PageDiscovery.LastRejectedPaths and surfaced as exit 125 with a
        // specific diagnostic naming the rejected file.
        Directory.CreateDirectory(Path.Combine(_tempDir, "man1"));
        byte[] payload = Encoding.UTF8.GetBytes("lorem ipsum dolor sit amet\nnot a man page at all\n");
        string gzPath = Path.Combine(_tempDir, "man1", "corrupt.1.gz");
        using (var fs = File.Create(gzPath))
        using (var gz = new GZipStream(fs, CompressionMode.Compress))
        {
            gz.Write(payload, 0, payload.Length);
        }

        var (stdout, stderr) = Sinks();
        int exit = Cli.Run(
            new[] { "--no-pager", "corrupt" },
            stdout, stderr,
            isTerminal: false, terminalWidth: 80,
            exeDirectory: _tempDir,
            manpathEnv: _tempDir);

        Assert.Equal(ManExitCode.InternalError, exit);
        string err = stderr.ToString();
        Assert.Contains("found", err, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("corrupt.1.gz", err, StringComparison.Ordinal);
        // Crucially NOT the misleading "no manual entry" message.
        Assert.DoesNotContain("no manual entry", err, StringComparison.Ordinal);
    }

    [Fact]
    public void PlainNonGroffUncompressed_AlsoRoutesToInternalErrorWithRejectedPath()
    {
        // Companion case: plain `.1` file with no groff macros. Same defect class as
        // the gzip case (SFH-I2 round-1 reproducer was also a plain non-groff file
        // shadowing a valid copy). The rejection-tracking fix surfaces both shapes.
        Directory.CreateDirectory(Path.Combine(_tempDir, "man1"));
        File.WriteAllText(Path.Combine(_tempDir, "man1", "plain.1"),
            "this is just text\nno groff at all\n");

        var (stdout, stderr) = Sinks();
        int exit = Cli.Run(
            new[] { "--no-pager", "plain" },
            stdout, stderr,
            isTerminal: false, terminalWidth: 80,
            exeDirectory: _tempDir,
            manpathEnv: _tempDir);

        Assert.Equal(ManExitCode.InternalError, exit);
        string err = stderr.ToString();
        Assert.Contains("found", err, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("plain.1", err, StringComparison.Ordinal);
        Assert.DoesNotContain("no manual entry", err, StringComparison.Ordinal);
    }

    [Fact]
    public void CorruptHighPri_DoesNotShadowValidLowPri_FallsThroughCleanly()
    {
        // SFH-I2 round-1 contract must still hold: when a high-priority root has a
        // corrupt candidate AND a lower-priority root has a valid one, FindPage falls
        // through to the valid copy and returns it. The rejection-tracking change kicks
        // in only when EVERY candidate was rejected.
        string highPri = Path.Combine(_tempDir, "high");
        string lowPri = Path.Combine(_tempDir, "low");
        Directory.CreateDirectory(Path.Combine(highPri, "man1"));
        Directory.CreateDirectory(Path.Combine(lowPri, "man1"));

        byte[] payload = Encoding.UTF8.GetBytes("just text, no groff\n");
        using (var fs = File.Create(Path.Combine(highPri, "man1", "tool.1.gz")))
        using (var gz = new GZipStream(fs, CompressionMode.Compress))
        {
            gz.Write(payload, 0, payload.Length);
        }
        File.WriteAllText(Path.Combine(lowPri, "man1", "tool.1"),
            ".TH TOOL 1\n.SH NAME\ntool - the real one\n");

        var (stdout, stderr) = Sinks();
        int exit = Cli.Run(
            new[] { "--path", "tool" },
            stdout, stderr,
            isTerminal: false, terminalWidth: 80,
            exeDirectory: _tempDir,
            manpathEnv: highPri + Path.PathSeparator + lowPri);

        Assert.Equal(ManExitCode.Success, exit);
        string discovered = stdout.ToString().Trim();
        Assert.Contains(lowPri, discovered, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("", stderr.ToString());
    }

    [Fact]
    public void NoCandidateAtAll_StillEmitsNoManualEntryWithExit1()
    {
        // Negative case: when discovery genuinely finds NO candidate file (no file in
        // any search root, not even rejected ones), the message remains "no manual
        // entry" and exit code 1 — the rejection-tracking diagnostic must not fire
        // when there's nothing to report.
        var (stdout, stderr) = Sinks();
        int exit = Cli.Run(
            new[] { "--no-pager", "definitely_does_not_exist_xyz_qwe" },
            stdout, stderr,
            isTerminal: false, terminalWidth: 80,
            exeDirectory: _tempDir,
            manpathEnv: _tempDir);

        Assert.Equal(ManExitCode.NotFound, exit);
        Assert.Contains("no manual entry", stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("found", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LastRejectedPaths_ResetOnEveryFindPageCall()
    {
        // The property must reflect the most recent FindPage call only. A successful
        // second call must clear rejections from a prior failed call.
        Directory.CreateDirectory(Path.Combine(_tempDir, "man1"));
        File.WriteAllText(Path.Combine(_tempDir, "man1", "junk.1"), "no groff\n");

        var discovery = new PageDiscovery(new[] { _tempDir });
        string? first = discovery.FindPage("junk");
        Assert.Null(first);
        Assert.Single(discovery.LastRejectedPaths);

        // Now create a valid page and search for it. The previous rejection list must
        // be cleared at the start of FindPage.
        File.WriteAllText(Path.Combine(_tempDir, "man1", "valid.1"), ".TH VALID 1\n.SH NAME\nvalid - ok\n");
        string? second = discovery.FindPage("valid");
        Assert.NotNull(second);
        Assert.Empty(discovery.LastRejectedPaths);
    }
}

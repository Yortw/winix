#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text;
using Winix.Files;
using Xunit;

namespace Winix.Files.Tests;

/// <summary>
/// Tests for round-1 fresh-eyes 2026-05-09 test-analyzer findings I3 (multi-root with
/// mixed valid/invalid), I6 (--text/--binary content classification), and an NDJSON
/// shape regression guard distinct from the substring-only assertions in
/// <see cref="FormattingTests"/>.
/// </summary>
public sealed class Round1CoverageTests : IDisposable
{
    private readonly string _tempDir;

    public Round1CoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "files-r1cov-" + Guid.NewGuid().ToString("N"));
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

    // ── I3: multi-root with mixed valid/invalid ────────────────────────────────────

    [Fact]
    public void Run_FirstRootMissing_StopsImmediatelyAndReturns1()
    {
        // Pre-fix this contract was unobserved. The dispatch loop checks each root for
        // existence before walking; if the first is missing it short-circuits before
        // touching the second. Verify that's the contract: stop on first invalid root.
        string root1Missing = Path.Combine(_tempDir, "missing");
        string root2Existing = _tempDir;
        File.WriteAllText(Path.Combine(root2Existing, "would-have-been-yielded.txt"), "x");

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = Cli.Run(new[] { root1Missing, root2Existing }, stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(1, exit);
        Assert.Contains("path not found", stderr.ToString(), StringComparison.Ordinal);
        // The would-have-been-yielded.txt under the second root must NOT appear:
        // the missing-root validation runs BEFORE any walking.
        Assert.DoesNotContain("would-have-been-yielded.txt", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_TwoValidRoots_WalksBothInOrder()
    {
        string root1 = Path.Combine(_tempDir, "r1");
        string root2 = Path.Combine(_tempDir, "r2");
        Directory.CreateDirectory(root1);
        Directory.CreateDirectory(root2);
        File.WriteAllText(Path.Combine(root1, "alpha.txt"), "a");
        File.WriteAllText(Path.Combine(root2, "bravo.txt"), "b");

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = Cli.Run(
            new[] { "--json", root1, root2 },
            stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(0, exit);
        string outText = stdout.ToString();
        // Both roots referenced in searched_roots envelope field.
        Assert.Contains("r1", outText, StringComparison.Ordinal);
        Assert.Contains("r2", outText, StringComparison.Ordinal);

        // Round-2 fresh-eyes 2026-05-09 test-analyzer Item 6: the test name promised
        // ordering ("WalksBothInOrder") but the body only asserted both files appear,
        // not the order. Pin the order: alpha.txt (under r1) must precede bravo.txt
        // (under r2) in the output stream because Cli.Run iterates roots in argv order.
        int alphaIdx = outText.IndexOf("alpha.txt", StringComparison.Ordinal);
        int bravoIdx = outText.IndexOf("bravo.txt", StringComparison.Ordinal);
        Assert.True(alphaIdx >= 0, "alpha.txt must appear in output");
        Assert.True(bravoIdx >= 0, "bravo.txt must appear in output");
        Assert.True(alphaIdx < bravoIdx,
            $"alpha.txt (under r1, walked first) should precede bravo.txt (under r2); got alpha@{alphaIdx}, bravo@{bravoIdx}");
    }

    // ── I6: --text/--binary content classification ────────────────────────────────

    [Fact]
    public void Run_TextFilter_ExcludesBinaryFile()
    {
        // A file containing a null byte counts as binary per the null-byte heuristic
        // (matches git). --text should exclude it.
        string textFile = Path.Combine(_tempDir, "ascii.txt");
        string binaryFile = Path.Combine(_tempDir, "with-null.bin");
        File.WriteAllText(textFile, "plain ascii content");
        File.WriteAllBytes(binaryFile, new byte[] { 0x68, 0x69, 0x00, 0x21 }); // hi\0!

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = Cli.Run(
            new[] { "--text", _tempDir },
            stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(0, exit);
        string outText = stdout.ToString();
        Assert.Contains("ascii.txt", outText, StringComparison.Ordinal);
        Assert.DoesNotContain("with-null.bin", outText, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_BinaryFilter_ExcludesTextFile()
    {
        string textFile = Path.Combine(_tempDir, "ascii.txt");
        string binaryFile = Path.Combine(_tempDir, "with-null.bin");
        File.WriteAllText(textFile, "plain ascii content");
        File.WriteAllBytes(binaryFile, new byte[] { 0x68, 0x69, 0x00, 0x21 });

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = Cli.Run(
            new[] { "--binary", _tempDir },
            stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(0, exit);
        string outText = stdout.ToString();
        Assert.DoesNotContain("ascii.txt", outText, StringComparison.Ordinal);
        Assert.Contains("with-null.bin", outText, StringComparison.Ordinal);
    }

    // ── NDJSON shape regression guard ──────────────────────────────────────────────

    [Fact]
    public void Run_Ndjson_EachLineIsParseableJsonWithExpectedKeys()
    {
        // Existing FormattingTests substring-match for "path":"..." which would pass
        // for invalid JSON like "{,\"path\":\"..\"}" or for records that ALSO contain
        // "size_bytes":-1 if a regression added the sentinel back. Pin via real
        // JsonDocument.Parse + key inspection — a structural assertion.
        File.WriteAllText(Path.Combine(_tempDir, "alpha.txt"), "x");

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = Cli.Run(
            new[] { "--ndjson", _tempDir },
            stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(0, exit);
        string[] lines = stdout.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .ToArray();

        Assert.NotEmpty(lines);
        foreach (string line in lines)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(line);
            // Each record must carry the per-record fields, NOT the envelope fields
            // (post-F2 contract).
            Assert.True(doc.RootElement.TryGetProperty("path", out _));
            Assert.True(doc.RootElement.TryGetProperty("name", out _));
            Assert.True(doc.RootElement.TryGetProperty("type", out _));
            Assert.True(doc.RootElement.TryGetProperty("depth", out _));
            // Envelope fields must NOT appear per record.
            Assert.False(doc.RootElement.TryGetProperty("tool", out _));
            Assert.False(doc.RootElement.TryGetProperty("version", out _));
            Assert.False(doc.RootElement.TryGetProperty("exit_code", out _));
            Assert.False(doc.RootElement.TryGetProperty("exit_reason", out _));
        }
    }

    [Fact]
    public void Run_Ndjson_DirectoryEntry_HasNullSizeAndModifiedKeys()
    {
        // Post-F3 contract: directory entries emit size_bytes:null and modified:null
        // (rather than the -1 / 0001-01-01 sentinels). Pin structurally with JsonDocument.
        Directory.CreateDirectory(Path.Combine(_tempDir, "subdir"));
        File.WriteAllText(Path.Combine(_tempDir, "subdir", "x.txt"), "x");

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exit = Cli.Run(
            new[] { "--ndjson", "--type", "d", _tempDir },
            stdout, stderr, isStdoutRedirected: true);

        Assert.Equal(0, exit);
        string[] lines = stdout.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .ToArray();

        Assert.NotEmpty(lines);
        foreach (string line in lines)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(line);
            Assert.Equal(System.Text.Json.JsonValueKind.Null,
                doc.RootElement.GetProperty("size_bytes").ValueKind);
            Assert.Equal(System.Text.Json.JsonValueKind.Null,
                doc.RootElement.GetProperty("modified").ValueKind);
        }
    }
}

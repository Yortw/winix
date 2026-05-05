#nullable enable
using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Xunit;
using Winix.Squeeze;

namespace Winix.Squeeze.Tests;

// Round-1 review CR-I1 / TA-C1/C2/C3 — Cli.RunAsync is the new orchestration seam. These
// tests pin every dispatch path (mode dispatch, mutual-exclusion, error envelopes, exit
// codes, --keep/--remove precedence, --output validation, --brotli/--zstd conflict, etc).
// Pre-fix ~140 LOC of Program.cs was untested.
public class CliTests : IDisposable
{
    private readonly string _tempDir;

    public CliTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "squeeze-cli-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private string MakeFile(string name, string contents)
    {
        string p = Path.Combine(_tempDir, name);
        File.WriteAllText(p, contents);
        return p;
    }

    private async Task<(int exit, string stdout, string stderr, byte[] binary)> RunCliAsync(
        string[] args,
        byte[]? stdinBytes = null,
        bool stdinRedirected = false,
        bool stdoutIsTerminal = false)
    {
        using MemoryStream stdinStream = new(stdinBytes ?? Array.Empty<byte>());
        using MemoryStream stdoutStream = new();
        StringWriter stderrW = new();
        int exit = await Cli.RunAsync(
            args, stdinStream, stdoutStream, stderrW,
            stdinIsRedirected: stdinRedirected,
            stdoutIsTerminal: stdoutIsTerminal);
        // stdout for squeeze is binary in --stdout/pipe mode; the test never assigns text to it.
        return (exit, "", stderrW.ToString(), stdoutStream.ToArray());
    }

    // ── Happy path: file mode compress + decompress round trip ──

    [Fact]
    public async Task RunAsync_FileModeCompressGzip_ProducesGzFile()
    {
        string input = MakeFile("data.txt", "hello world");
        var r = await RunCliAsync(new[] { input });
        Assert.Equal(0, r.exit);
        Assert.True(File.Exists(input + ".gz"));
    }

    [Fact]
    public async Task RunAsync_FileModeCompressZstd_ProducesZstFile()
    {
        string input = MakeFile("data.txt", "hello world");
        var r = await RunCliAsync(new[] { input, "--zstd" });
        Assert.Equal(0, r.exit);
        Assert.True(File.Exists(input + ".zst"));
    }

    [Fact]
    public async Task RunAsync_FileModeDecompressGzip_RoundTrips()
    {
        string input = MakeFile("data.txt", "round-trip me");
        var c = await RunCliAsync(new[] { input });
        Assert.Equal(0, c.exit);
        File.Delete(input);

        var d = await RunCliAsync(new[] { input + ".gz", "-d" });
        Assert.Equal(0, d.exit);
        Assert.Equal("round-trip me", File.ReadAllText(input));
    }

    // ── Round-1 review TA-C2: --brotli and --zstd together must reject as usage error. ──

    [Fact]
    public async Task RunAsync_BrotliAndZstdTogether_RejectedAsUsageError()
    {
        string input = MakeFile("data.txt", "hello");
        var r = await RunCliAsync(new[] { input, "--brotli", "--zstd" });
        Assert.Equal(2, r.exit);
        Assert.Contains("mutually exclusive", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ZstdAndBrotliTogether_OrderIndependent()
    {
        string input = MakeFile("data.txt", "hello");
        var r = await RunCliAsync(new[] { input, "--zstd", "--brotli" });
        Assert.Equal(2, r.exit);
        Assert.Contains("mutually exclusive", r.stderr, StringComparison.Ordinal);
    }

    // ── Round-1 review SFH-C2: --output empty/whitespace must reject at parse time
    //    rather than crash with an Argument_EmptyString resource-key leak. ──

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task RunAsync_EmptyOrWhitespaceOutputPath_RejectedAsUsageError(string outputPath)
    {
        string input = MakeFile("data.txt", "hello");
        var r = await RunCliAsync(new[] { input, "--output", outputPath });
        Assert.Equal(2, r.exit);
        Assert.Contains("--output path must not be empty", r.stderr, StringComparison.Ordinal);
        // Pin the regression-detector for the resource-key leak class.
        Assert.DoesNotContain("Arg_ParamName_Name", r.stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("Argument_EmptyString", r.stderr, StringComparison.Ordinal);
    }

    // ── Round-1 review SFH-C3: missing parent directory should produce English, not
    //    'IO_PathNotFound_Path' resource key. ──

    [Fact]
    public async Task RunAsync_OutputToMissingParentDir_EmitsCleanEnglish()
    {
        string input = MakeFile("data.txt", "hello");
        string missing = Path.Combine(_tempDir, "no-such-dir", "out.gz");
        var r = await RunCliAsync(new[] { input, "--output", missing });
        Assert.Equal(1, r.exit);
        Assert.Contains("parent directory does not exist", r.stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("IO_PathNotFound_Path", r.stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("UnauthorizedAccess_IODenied_Path", r.stderr, StringComparison.Ordinal);
    }

    // ── Round-1 review SFH-C1: truncated gzip silently exited 0 with partial output. Now
    //    rejected as corrupt input → exit 1 with clean message. ──

    [Fact]
    public async Task RunAsync_TruncatedGzipFile_RejectedAsCorrupt()
    {
        string input = MakeFile("data.txt", "this is a longer payload that compresses nontrivially");
        var c = await RunCliAsync(new[] { input });
        Assert.Equal(0, c.exit);

        // Corrupt the .gz by truncating to a non-trivial prefix that has the magic bytes
        // but no valid trailer.
        byte[] gz = File.ReadAllBytes(input + ".gz");
        File.WriteAllBytes(input + ".gz", gz[..(gz.Length / 2)]);
        File.Delete(input);

        var d = await RunCliAsync(new[] { input + ".gz", "-d" });
        Assert.Equal(1, d.exit);
        Assert.Contains("corrupt", d.stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_EmptyGzipPipe_RejectedAsCorrupt()
    {
        // 0-byte stream to gzip decompress: pre-fix this exited 0 with no output. gzip(1)
        // rejects as "unexpected end of file"; we now do too.
        var r = await RunCliAsync(new[] { "-d" }, stdinBytes: Array.Empty<byte>(), stdinRedirected: true);
        Assert.Equal(1, r.exit);
    }

    [Fact]
    public async Task RunAsync_TruncatedGzipPipe_RejectedAsCorrupt()
    {
        // Compress something, take only the first 15 bytes (magic + partial header but
        // no valid deflate body), pipe to decompress. Should exit 1, not silently emit
        // partial output.
        using MemoryStream raw = new();
        using (GZipStream gz = new(raw, CompressionLevel.Optimal, leaveOpen: true))
        using (StreamWriter w = new(gz, leaveOpen: true))
        {
            w.Write("hello world this is a payload");
        }
        byte[] truncated = raw.ToArray()[..15];

        var r = await RunCliAsync(new[] { "-d" }, stdinBytes: truncated, stdinRedirected: true);
        Assert.Equal(1, r.exit);
    }

    // ── Round-1 review TA-I8: --keep takes precedence over --remove when both are passed
    //    (gzip(1) semantics). Pre-fix --keep was inert. ──

    [Fact]
    public async Task RunAsync_KeepAndRemoveTogether_KeepWinsAndOriginalRemains()
    {
        string input = MakeFile("data.txt", "keep me");
        var r = await RunCliAsync(new[] { input, "--keep", "--remove" });
        Assert.Equal(0, r.exit);
        Assert.True(File.Exists(input), "input should still exist after --keep --remove");
        Assert.Contains("--keep takes precedence", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_RemoveAlone_DeletesOriginal()
    {
        string input = MakeFile("data.txt", "delete me");
        var r = await RunCliAsync(new[] { input, "--remove" });
        Assert.Equal(0, r.exit);
        Assert.False(File.Exists(input), "input should be deleted after --remove without --keep");
    }

    // ── Pipe mode round trip ──

    [Fact]
    public async Task RunAsync_PipeModeCompressDecompress_RoundTrips()
    {
        byte[] payload = System.Text.Encoding.UTF8.GetBytes("pipe round trip");

        var c = await RunCliAsync(Array.Empty<string>(), stdinBytes: payload, stdinRedirected: true);
        Assert.Equal(0, c.exit);
        Assert.NotEmpty(c.binary);

        var d = await RunCliAsync(new[] { "-d" }, stdinBytes: c.binary, stdinRedirected: true);
        Assert.Equal(0, d.exit);
        Assert.Equal(payload, d.binary);
    }

    // ── Multi-input + --output rejection ──

    [Fact]
    public async Task RunAsync_OutputWithMultipleInputs_RejectedAsUsageError()
    {
        string a = MakeFile("a.txt", "a");
        string b = MakeFile("b.txt", "b");
        var r = await RunCliAsync(new[] { a, b, "-o", "out.gz" });
        Assert.Equal(2, r.exit);
        Assert.Contains("multiple input files", r.stderr, StringComparison.Ordinal);
    }

    // ── Level out of range ──

    [Fact]
    public async Task RunAsync_LevelOutOfRangeForGzip_RejectedAsUsageError()
    {
        string input = MakeFile("data.txt", "hello");
        var r = await RunCliAsync(new[] { input, "--level", "99" });
        Assert.Equal(2, r.exit);
        Assert.Contains("out of range", r.stderr, StringComparison.Ordinal);
    }

    // ── No input + no stdin redirection → usage error ──

    [Fact]
    public async Task RunAsync_NoInputNoPipe_ReturnsUsageError()
    {
        var r = await RunCliAsync(Array.Empty<string>(), stdinRedirected: false);
        Assert.Equal(2, r.exit);
        Assert.Contains("no input files", r.stderr, StringComparison.Ordinal);
    }

    // ── Output file overwrite refusal + --force ──

    [Fact]
    public async Task RunAsync_OutputExists_RefusedWithoutForce()
    {
        string input = MakeFile("data.txt", "hello");
        File.WriteAllBytes(input + ".gz", new byte[] { 0xDE, 0xAD });
        var r = await RunCliAsync(new[] { input });
        Assert.Equal(1, r.exit);
        Assert.Contains("already exists", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_OutputExists_ForceOverwrites()
    {
        string input = MakeFile("data.txt", "hello");
        File.WriteAllBytes(input + ".gz", new byte[] { 0xDE, 0xAD });
        var r = await RunCliAsync(new[] { input, "-f" });
        Assert.Equal(0, r.exit);
        // File should now contain real gzip, not the 2-byte stub.
        byte[] result = File.ReadAllBytes(input + ".gz");
        Assert.True(result.Length > 2);
        Assert.Equal(0x1F, result[0]); // gzip magic
        Assert.Equal(0x8B, result[1]);
    }

    // ── ShellKit handled flags ──

    [Fact]
    public async Task RunAsync_Help_ReturnsZero()
    {
        var r = await RunCliAsync(new[] { "--help" });
        Assert.Equal(0, r.exit);
    }

    [Fact]
    public async Task RunAsync_Version_ReturnsZero()
    {
        var r = await RunCliAsync(new[] { "--version" });
        Assert.Equal(0, r.exit);
    }

    [Fact]
    public async Task RunAsync_Describe_ReturnsZero()
    {
        var r = await RunCliAsync(new[] { "--describe" });
        Assert.Equal(0, r.exit);
    }

    // ── Hotfix (post-merge of 64fd7a5): multi-member gzip is now REJECTED as corrupt rather
    //    than round-tripped, because round-3's structural-header validation still produced
    //    silent corruption on incompressible truncated single-member input (10MB random
    //    truncated to 5MB → ~5MB garbled output, exit 0). The trade-off prefers loud false
    //    positive on rare multi-member input over silent corruption on common incompressible
    //    truncation. Documented limitation in docs/ai/squeeze.md; workaround is `gzip -dc
    //    concat.gz | squeeze`. ──
    [Fact]
    public async Task RunAsync_MultiMemberGzipFile_RejectedAsCorrupt()
    {
        string a = MakeFile("a.txt", "first member content");
        var ca = await RunCliAsync(new[] { a });
        Assert.Equal(0, ca.exit);

        string b = MakeFile("b.txt", "second member content");
        var cb = await RunCliAsync(new[] { b });
        Assert.Equal(0, cb.exit);

        string multi = Path.Combine(_tempDir, "multi.gz");
        byte[] aGz = File.ReadAllBytes(a + ".gz");
        byte[] bGz = File.ReadAllBytes(b + ".gz");
        byte[] combined = new byte[aGz.Length + bGz.Length];
        Buffer.BlockCopy(aGz, 0, combined, 0, aGz.Length);
        Buffer.BlockCopy(bGz, 0, combined, aGz.Length, bGz.Length);
        File.WriteAllBytes(multi, combined);

        var d = await RunCliAsync(new[] { "-d", multi, "-c" });
        Assert.Equal(1, d.exit);
        Assert.Contains("corrupt", d.stderr, StringComparison.OrdinalIgnoreCase);
    }

    // ── Hotfix regression test: large incompressible truncated input. The user-found
    //    case that exposed the round-3 multi-member-detector hole: 10MB random data
    //    truncated to 5MB → silent partial output. With multi-member detection dropped,
    //    this now exits 1 cleanly. ──
    [Fact]
    public async Task RunAsync_LargeIncompressibleTruncated_RejectedAsCorrupt()
    {
        // 1MB random data is enough to reliably reproduce the false-positive scenario
        // (any size > ~100KB has high probability of containing a structurally-plausible
        // spurious 1f 8b sequence in the deflate output).
        Random rnd = new(42);
        byte[] data = new byte[1024 * 1024];
        rnd.NextBytes(data);
        string raw = Path.Combine(_tempDir, "rand1mb.bin");
        File.WriteAllBytes(raw, data);

        var c = await RunCliAsync(new[] { raw });
        Assert.Equal(0, c.exit);

        byte[] gz = File.ReadAllBytes(raw + ".gz");
        // Truncate to 60% of the gz size to ensure mid-deflate truncation.
        int truncateLen = (int)(gz.Length * 0.6);
        File.WriteAllBytes(raw + ".gz", gz[..truncateLen]);
        File.Delete(raw);

        var d = await RunCliAsync(new[] { raw + ".gz", "-d", "-c" });
        // Pre-hotfix this exited 0 with ~truncateLen bytes of silent garbled output.
        Assert.Equal(1, d.exit);
    }

    // ── Round-2 review (closing SFH-C1 part 2): header-only truncation (e.g. 15-30 bytes
    //    of a longer gzip) previously slipped through the 'decompressed == 0 → skip' hack
    //    and exited 0 with empty output. The compress-side now emits canonical RFC 1952
    //    empty-gzip so the decompress side can be strict, AND the decompress side now
    //    rejects BytesRead < 18. ──
    [Theory]
    [InlineData(15)]
    [InlineData(20)]
    [InlineData(30)]
    public async Task RunAsync_ShortGzipTruncation_RejectedAsCorrupt(int bytesToKeep)
    {
        string input = MakeFile("data.txt", "the quick brown fox jumps over the lazy dog");
        var c = await RunCliAsync(new[] { input });
        Assert.Equal(0, c.exit);

        byte[] full = File.ReadAllBytes(input + ".gz");
        if (full.Length <= bytesToKeep) return; // Test data isn't long enough to truncate this short.
        File.WriteAllBytes(input + ".gz", full[..bytesToKeep]);
        File.Delete(input);

        var d = await RunCliAsync(new[] { input + ".gz", "-d" });
        Assert.Equal(1, d.exit);
        // Either "truncated", "corrupt", or "incomplete trailer" is acceptable; just must NOT exit 0.
    }

    // ── Round-2 review (closing the .NET-non-compliant empty-gzip workaround): empty
    //    input now produces a canonical 23-byte RFC 1952 gzip (not .NET's 15-byte
    //    non-compliant output). Round-trip must work cleanly. ──
    // ── Round-3 review TA-I4: pin the seekable-input rewind path in DecompressAutoDetectAsync.
    //    A file without a recognised compression extension (so DetectFromExtension fails)
    //    that has valid gzip magic bytes must still round-trip via auto-detect — this
    //    exercises the `if (input.CanSeek) { input.Position -= headerBytes.Length; ... }`
    //    branch added in round 2. ──
    [Fact]
    public async Task RunAsync_AutoDetectViaMagicBytes_NoExtension_RoundTripsCleanly()
    {
        string input = MakeFile("data.txt", "auto-detect via magic, not extension");
        var c = await RunCliAsync(new[] { input });
        Assert.Equal(0, c.exit);

        // Rename .gz file to a non-recognised extension so the FormatDetector falls back to magic bytes.
        string mysteryPath = Path.Combine(_tempDir, "mystery.bin");
        File.Move(input + ".gz", mysteryPath);

        // -d with explicit -o avoids GetDecompressOutputPath rejection (which strips by extension).
        string outPath = Path.Combine(_tempDir, "decoded");
        var d = await RunCliAsync(new[] { mysteryPath, "-d", "-o", outPath });
        Assert.Equal(0, d.exit);
        Assert.Equal("auto-detect via magic, not extension", File.ReadAllText(outPath));
    }

    // ── Round-3 review CR-C1 (Critical): the round-2 multi-member detector treated any
    //    `1f 8b` byte pair past offset 18 as a second member. For incompressible data
    //    (random/encrypted/already-compressed), spurious `1f 8b` pairs occur ~1/65k
    //    positions, so >64KB single-member gzip files often contained false positives →
    //    multi-member branch → ISIZE skipped → truncation silently accepted. Round-3
    //    closes by structurally validating CM=08 + FLG-reserved-bits-zero at the
    //    candidate header. This test pins the contract on incompressible-data truncation. ──
    [Fact]
    public async Task RunAsync_IncompressibleTruncated_RejectedAsCorrupt_NoFalseMultiMember()
    {
        // 64KB of pseudo-random incompressible data with deterministic seed so we can
        // reliably reproduce the spurious-1f-8b false-positive case the round-3 reviewer
        // identified. Seed 3, 64KB → produces a single-member gzip likely containing
        // spurious 1f 8b past offset 18.
        Random rnd = new(3);
        byte[] data = new byte[64 * 1024];
        rnd.NextBytes(data);
        string raw = Path.Combine(_tempDir, "rand.bin");
        File.WriteAllBytes(raw, data);

        var c = await RunCliAsync(new[] { raw });
        Assert.Equal(0, c.exit);

        byte[] gz = File.ReadAllBytes(raw + ".gz");
        Assert.True(gz.Length > 18000, $"Test data too small to exercise the spurious-magic case (got {gz.Length})");

        // Truncate to a length that cuts off the real trailer.
        int truncateLen = Math.Min(18237, gz.Length - 16);
        File.WriteAllBytes(raw + ".gz", gz[..truncateLen]);
        File.Delete(raw);

        var d = await RunCliAsync(new[] { raw + ".gz", "-d" });
        // Pre-fix this exited 0 with partial output. Now must exit 1.
        Assert.Equal(1, d.exit);
    }

    [Fact]
    public async Task RunAsync_EmptyInputRoundTrip_Canonical23Bytes()
    {
        string empty = MakeFile("empty.txt", "");
        var c = await RunCliAsync(new[] { empty });
        Assert.Equal(0, c.exit);

        byte[] gz = File.ReadAllBytes(empty + ".gz");
        Assert.Equal(23, gz.Length);
        // Verify magic and trailer pattern.
        Assert.Equal(0x1f, gz[0]);
        Assert.Equal(0x8b, gz[1]);
        // Trailer (last 8 bytes) should be all zeros for empty input (CRC32=0, ISIZE=0).
        for (int i = 15; i < 23; i++)
        {
            Assert.Equal(0, gz[i]);
        }

        File.Delete(empty);
        var d = await RunCliAsync(new[] { empty + ".gz", "-d" });
        Assert.Equal(0, d.exit);
        Assert.Equal("", File.ReadAllText(empty));
    }
}

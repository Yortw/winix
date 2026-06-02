#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;
using Winix.Digest;
using Winix.Digest.Tests.Fakes;
using Yort.ShellKit;

namespace Winix.Digest.Tests;

// Round-2 review test gap — pin the Program-level contracts that previously had no
// coverage: --verify exit code (1, distinct from runtime errors), JSON multi-file
// array glue (commas only between elements, brackets around), --key-stdin + stdin
// payload conflict (early reject, exit code), and the generic-catch fallback exit
// code (SFH-I2 — must be NotExecutable, not 1).
public class CliTests
{
    private static (int exit, string stdout, string stderr) RunCli(
        string[] args,
        string keyStdin = "",
        byte[]? payloadStdin = null)
    {
        var stdoutWriter = new StringWriter();
        var stderrWriter = new StringWriter();
        int exit = Cli.Run(
            args: args,
            keyStdin: new FakeTextReader(keyStdin),
            payloadStdin: new MemoryStream(payloadStdin ?? Array.Empty<byte>()),
            stdout: stdoutWriter,
            stderr: stderrWriter);
        return (exit, stdoutWriter.ToString(), stderrWriter.ToString());
    }

    [Fact]
    public void Verify_Match_ExitsZero()
    {
        // SHA-256("abc") = ba78... — pass that as --verify expected.
        var r = RunCli(new[]
        {
            "--verify",
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            "-s", "abc",
        });
        Assert.Equal(ExitCode.Success, r.exit);
    }

    [Fact]
    public void Verify_Mismatch_ExitsOne_NotNotExecutable()
    {
        // The exit code 1 is the documented --verify mismatch code — distinct from
        // ExitCode.UsageError (2) and ExitCode.NotExecutable (125). Scripts of the
        // shape `digest --verify ... || alert` rely on this.
        var r = RunCli(new[]
        {
            "--verify",
            "0000000000000000000000000000000000000000000000000000000000000000",
            "-s", "abc",
        });
        Assert.Equal(1, r.exit);
        Assert.NotEqual(ExitCode.NotExecutable, r.exit);
        Assert.Contains("verification failed", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyStdin_WithStdinPayload_ExitsUsageError()
    {
        // A single stdin can only be read once; the conflict is rejected before
        // any reads start. Pin the exit code (UsageError, not NotExecutable) so a
        // refactor that swaps the order of checks doesn't silently relax this.
        var r = RunCli(
            args: new[] { "--hmac", "sha256", "--key-stdin" },
            keyStdin: "irrelevant",
            payloadStdin: Array.Empty<byte>());
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Contains("--key-stdin cannot be combined with stdin payload", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonMultiFile_WrappedInBrackets_CommasBetweenOnly()
    {
        // Prove the bracket+comma glue at lines 109-115 of the original Program.cs is correct:
        // exactly one '[' at the start, one ']' at the end, count of ',' = N - 1.
        string p1 = Path.GetTempFileName();
        string p2 = Path.GetTempFileName();
        string p3 = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(p1, Encoding.UTF8.GetBytes("a"));
            File.WriteAllBytes(p2, Encoding.UTF8.GetBytes("b"));
            File.WriteAllBytes(p3, Encoding.UTF8.GetBytes("c"));
            var r = RunCli(new[] { "--json", p1, p2, p3 });
            Assert.Equal(ExitCode.Success, r.exit);
            string s = r.stdout.Trim();
            Assert.StartsWith("[", s);
            Assert.EndsWith("]", s);
            // Three elements → exactly two top-level commas. Counting all commas is fragile
            // because the inner JSON may include them; instead, count commas at depth 0 by
            // walking with a brace counter.
            int depth = 0;
            int topLevelCommas = 0;
            foreach (char c in s)
            {
                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') depth--;
                else if (c == ',' && depth == 1) topLevelCommas++;
            }
            Assert.Equal(2, topLevelCommas);
        }
        finally { File.Delete(p1); File.Delete(p2); File.Delete(p3); }
    }

    [Fact]
    public void JsonSingleFile_NoBrackets()
    {
        // Single-result JSON path emits the bare object, not a one-element array.
        string p1 = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(p1, Encoding.UTF8.GetBytes("a"));
            var r = RunCli(new[] { "--json", p1 });
            Assert.Equal(ExitCode.Success, r.exit);
            string s = r.stdout.Trim();
            Assert.StartsWith("{", s);
            Assert.EndsWith("}", s);
        }
        finally { File.Delete(p1); }
    }

    [Fact]
    public void UsageError_ReturnsExitTwo_AndPrintsHelpHint()
    {
        var r = RunCli(new[] { "--unknown-flag" });
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Contains("Run 'digest --help' for usage.", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Md5_EmitsLegacyWarning()
    {
        // I10 — verify the AlgorithmWarning helper is wired to stderr in Cli.Run.
        var r = RunCli(new[] { "--md5", "-s", "abc" });
        Assert.Equal(ExitCode.Success, r.exit);
        Assert.Contains("MD5 is cryptographically broken", r.stderr, StringComparison.Ordinal);
    }

    // -- Round-2 review CR-I3 — full end-to-end pin: feeding non-UTF-8 bytes through
    //    the Cli's stdin payload path produces the same hash as direct in-memory hashing
    //    of those bytes. This was the canonical sha256sum-compatibility break. --
    [Fact]
    public void StdinPayload_BinaryBytes_HashMatchesByteHash()
    {
        byte[] binary = new byte[] { 0xFF, 0xFE, 0x80, 0x81, 0x00, 0xC0, 0xC1 };
        byte[] expected = HashFactory.Create(HashAlgorithm.Sha256).Hash(binary);
        string expectedHex = Winix.Codec.Hex.Encode(expected);

        var r = RunCli(new[] { "--sha256" }, payloadStdin: binary);
        Assert.Equal(ExitCode.Success, r.exit);
        Assert.Equal(expectedHex, r.stdout.Trim());
    }

    // -- Resource-key-leak sweep — when a file passes the ArgParser File.Exists guard but
    //    OpenRead fails, the HashRunner read-catch reports the failure. Under
    //    UseSystemResourceKeys (which this test csproj mirrors) the framework
    //    exception .Message is a bare CoreLib resource key, not English. SafeError.Describe
    //    maps it to readable text and never leaks the key. The trigger differs per platform
    //    (exclusive lock on Windows → IOException; chmod 000 on Unix → UnauthorizedAccess)
    //    but the contract is identical: no resource-key fragment in stderr, readable text. --

    [SkippableFact]
    public void FileReadLocked_Windows_NoResourceKeyLeak_ReadableError()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows-only — exclusive file lock (FileShare.None) blocks a second OpenRead.");
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return; // redundant, satisfies analyzer parity with the Unix test

        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("secret"));
            // Hold an exclusive (no-share) handle so the tool's OpenRead hits a sharing violation.
            using (var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var r = RunCli(new[] { "--sha256", path });
                Assert.Equal(ExitCode.NotExecutable, r.exit);
                // Under UseSystemResourceKeys the leaked key would be a fragment like 'IO_SharingViolation_File'.
                Assert.DoesNotContain("IO_", r.stderr, StringComparison.Ordinal);
                Assert.DoesNotContain("_File", r.stderr, StringComparison.Ordinal);
                Assert.Contains("failed to read", r.stderr, StringComparison.Ordinal);
            }
        }
        finally { File.Delete(path); }
    }

    [SkippableFact]
    public void FileReadDenied_Unix_NoResourceKeyLeak_ReadableError()
    {
        Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Unix-only — uses File.SetUnixFileMode to force a deterministic read denial.");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return; // redundant, satisfies CA1416 analyzer

        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("secret"));
            File.SetUnixFileMode(path, UnixFileMode.None); // chmod 000 → OpenRead throws UnauthorizedAccessException
            var r = RunCli(new[] { "--sha256", path });
            Assert.Equal(ExitCode.NotExecutable, r.exit);
            Assert.DoesNotContain("UnauthorizedAccess", r.stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("_Path", r.stderr, StringComparison.Ordinal);
            Assert.Contains("access denied", r.stderr, StringComparison.Ordinal);
        }
        finally
        {
            // Restore owner write so Delete succeeds even if the chmod above stuck.
            try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
            File.Delete(path);
        }
    }
}

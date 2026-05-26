#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;
using Winix.Digest;
using Winix.Digest.Tests.Fakes;

namespace Winix.Digest.Tests;

public class KeyResolverTests
{
    [Fact]
    public void ResolveFromEnv_ReadsVariable()
    {
        Environment.SetEnvironmentVariable("DIGEST_TEST_KEY_1", "my-secret");
        try
        {
            var stderr = new StringWriter();
            byte[]? key = KeyResolver.Resolve(
                source: KeySource.EnvVariable("DIGEST_TEST_KEY_1"),
                stdin: new FakeTextReader(""),
                stripTrailingNewline: true,
                stderr: stderr,
                out string? error);
            Assert.Null(error);
            Assert.Equal(Encoding.UTF8.GetBytes("my-secret"), key);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DIGEST_TEST_KEY_1", null);
        }
    }

    [Fact]
    public void ResolveFromEnv_MissingVariable_Errors()
    {
        var stderr = new StringWriter();
        byte[]? key = KeyResolver.Resolve(
            source: KeySource.EnvVariable("DIGEST_TEST_KEY_DOES_NOT_EXIST_12345"),
            stdin: new FakeTextReader(""),
            stripTrailingNewline: true,
            stderr: stderr,
            out string? error);
        Assert.NotNull(error);
        Assert.Contains("not set", error, StringComparison.Ordinal);
        Assert.Null(key);
    }

    [Fact]
    public void ResolveFromFile_StripsTrailingNewline()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "my-secret\n");
            var stderr = new StringWriter();
            byte[]? key = KeyResolver.Resolve(
                source: KeySource.File(path),
                stdin: new FakeTextReader(""),
                stripTrailingNewline: true,
                stderr: stderr,
                out string? error);
            Assert.Null(error);
            Assert.Equal(Encoding.UTF8.GetBytes("my-secret"), key);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ResolveFromFile_KeyRaw_PreservesBytes()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "my-secret\n");
            var stderr = new StringWriter();
            byte[]? key = KeyResolver.Resolve(
                source: KeySource.File(path),
                stdin: new FakeTextReader(""),
                stripTrailingNewline: false,
                stderr: stderr,
                out string? error);
            Assert.Null(error);
            Assert.Equal(Encoding.UTF8.GetBytes("my-secret\n"), key);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ResolveFromStdin_StripsTrailingNewline()
    {
        var stderr = new StringWriter();
        byte[]? key = KeyResolver.Resolve(
            source: KeySource.Stdin(),
            stdin: new FakeTextReader("stdin-secret\n"),
            stripTrailingNewline: true,
            stderr: stderr,
            out string? error);
        Assert.Null(error);
        Assert.Equal(Encoding.UTF8.GetBytes("stdin-secret"), key);
    }

    [Fact]
    public void ResolveFromLiteral_EmitsWarning()
    {
        var stderr = new StringWriter();
        byte[]? key = KeyResolver.Resolve(
            source: KeySource.Literal("literal-secret"),
            stdin: new FakeTextReader(""),
            stripTrailingNewline: true,
            stderr: stderr,
            out string? error);
        Assert.Null(error);
        Assert.Equal(Encoding.UTF8.GetBytes("literal-secret"), key);
        string stderrText = stderr.ToString();
        Assert.Contains("--key exposes the key", stderrText, StringComparison.Ordinal);
        Assert.Contains("ps", stderrText, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveFromFile_MissingFile_Errors()
    {
        var stderr = new StringWriter();
        byte[]? key = KeyResolver.Resolve(
            source: KeySource.File("/nonexistent/path/to/secret-file-12345"),
            stdin: new FakeTextReader(""),
            stripTrailingNewline: true,
            stderr: stderr,
            out string? error);
        Assert.NotNull(error);
        Assert.Contains("not found", error, StringComparison.Ordinal);
        Assert.Null(key);
    }

    [SkippableFact]
    public void ResolveFromFile_GroupReadable_Unix_EmitsWarning()
    {
        Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Unix-only — uses File.SetUnixFileMode and asserts on the group/other-readable warning that doesn't apply on Windows.");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return; // redundant, satisfies analyzer

        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "my-secret");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            var stderr = new StringWriter();
            byte[]? key = KeyResolver.Resolve(
                source: KeySource.File(path),
                stdin: new FakeTextReader(""),
                stripTrailingNewline: true,
                stderr: stderr,
                out string? error);
            Assert.Null(error);
            Assert.Contains("readable by group/other", stderr.ToString(), StringComparison.Ordinal);
        }
        finally { File.Delete(path); }
    }

    // -- Round-1 review C1 — empty HMAC keys must be rejected at the resolver layer.
    //    The BCL HMAC* classes accept zero-length keys without complaint and produce
    //    a deterministic-but-cryptographically-meaningless tag an attacker can forge.
    //    All four key sources can produce a zero-length key (env empty, 0-byte file,
    //    EOF-only stdin, '--key ""'); each is rejected with a usage-error naming the
    //    source. These tests pin the rejection contract for each source. --

    [Fact]
    public void ResolveFromEnv_EmptyValue_Errors()
    {
        Environment.SetEnvironmentVariable("DIGEST_TEST_KEY_EMPTY", "");
        try
        {
            var stderr = new StringWriter();
            byte[]? key = KeyResolver.Resolve(
                source: KeySource.EnvVariable("DIGEST_TEST_KEY_EMPTY"),
                stdin: new FakeTextReader(""),
                stripTrailingNewline: true,
                stderr: stderr,
                out string? error);
            Assert.Null(key);
            Assert.NotNull(error);
            Assert.Contains("empty", error, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("DIGEST_TEST_KEY_EMPTY", error, StringComparison.Ordinal);
            Assert.Contains("forgeable", error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DIGEST_TEST_KEY_EMPTY", null);
        }
    }

    [Fact]
    public void ResolveFromFile_ZeroByteFile_Errors()
    {
        string path = Path.GetTempFileName();
        try
        {
            // Path.GetTempFileName creates a 0-byte file; perfect for this case.
            var stderr = new StringWriter();
            byte[]? key = KeyResolver.Resolve(
                source: KeySource.File(path),
                stdin: new FakeTextReader(""),
                stripTrailingNewline: true,
                stderr: stderr,
                out string? error);
            Assert.Null(key);
            Assert.NotNull(error);
            Assert.Contains("empty", error, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(path, error);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ResolveFromFile_OnlyTrailingNewline_AfterStrip_Errors()
    {
        // A file containing only "\n" or "\r\n" gets stripped to 0 bytes by the
        // post-read newline strip. Verify the rejection still fires.
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "\n");
            var stderr = new StringWriter();
            byte[]? key = KeyResolver.Resolve(
                source: KeySource.File(path),
                stdin: new FakeTextReader(""),
                stripTrailingNewline: true,
                stderr: stderr,
                out string? error);
            Assert.Null(key);
            Assert.NotNull(error);
            Assert.Contains("empty", error, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ResolveFromStdin_EmptyInput_Errors()
    {
        var stderr = new StringWriter();
        byte[]? key = KeyResolver.Resolve(
            source: KeySource.Stdin(),
            stdin: new FakeTextReader(""), // EOF immediately
            stripTrailingNewline: true,
            stderr: stderr,
            out string? error);
        Assert.Null(key);
        Assert.NotNull(error);
        Assert.Contains("empty", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stdin", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveLiteral_EmptyValue_Errors()
    {
        var stderr = new StringWriter();
        byte[]? key = KeyResolver.Resolve(
            source: KeySource.Literal(""),
            stdin: new FakeTextReader(""),
            stripTrailingNewline: true,
            stderr: stderr,
            out string? error);
        Assert.Null(key);
        Assert.NotNull(error);
        Assert.Contains("empty", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--key", error, StringComparison.Ordinal);
    }

    // -- Round-1 review I3 — size-cap key reads at 1 MB. Defends against accidental
    //    --key-file /dev/zero, /proc/kcore, or piping a multi-GB file via --key-stdin. --

    [Fact]
    public void ResolveFromFile_AtCap_Succeeds()
    {
        // A file whose payload (after newline strip) sits at the cap should still be accepted.
        // Use the cap-1 raw bytes so a no-newline file works the same.
        string path = Path.GetTempFileName();
        try
        {
            byte[] payload = new byte[KeyResolver.MaxKeySizeBytes];
            for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i % 251 + 1); // never 0, never \n
            File.WriteAllBytes(path, payload);
            var stderr = new StringWriter();
            byte[]? key = KeyResolver.Resolve(
                source: KeySource.File(path),
                stdin: new FakeTextReader(""),
                stripTrailingNewline: true,
                stderr: stderr,
                out string? error);
            Assert.Null(error);
            Assert.NotNull(key);
            Assert.Equal(KeyResolver.MaxKeySizeBytes, key!.Length);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ResolveFromFile_OverCap_Errors()
    {
        string path = Path.GetTempFileName();
        try
        {
            byte[] payload = new byte[KeyResolver.MaxKeySizeBytes + 1];
            File.WriteAllBytes(path, payload);
            var stderr = new StringWriter();
            byte[]? key = KeyResolver.Resolve(
                source: KeySource.File(path),
                stdin: new FakeTextReader(""),
                stripTrailingNewline: true,
                stderr: stderr,
                out string? error);
            Assert.Null(key);
            Assert.NotNull(error);
            Assert.Contains("cap", error, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(path, error, StringComparison.Ordinal);
        }
        finally { File.Delete(path); }
    }

    // -- Round-2 review CR-I2/SFH-I1 — File.OpenRead failures must produce a typed
    //    error through the resolver's out-param contract, not escape to the caller's
    //    outer catch. Reproduce by holding an exclusive lock so the resolver's open
    //    fails. On non-Windows the FileShare semantics differ; this is the most
    //    portable repro that fires deterministically. --
    [Fact]
    public void ResolveFromFile_LockedFile_TypedError()
    {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, "my-secret");
        try
        {
            // Open the file with no shared read access — Windows refuses concurrent
            // opens from File.OpenRead under FileShare.None, producing IOException.
            using var holder = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            var stderr = new StringWriter();
            byte[]? key = KeyResolver.Resolve(
                source: KeySource.File(path),
                stdin: new FakeTextReader(""),
                stripTrailingNewline: true,
                stderr: stderr,
                out string? error);

            // On platforms where FileShare.None doesn't block (some POSIX kernels)
            // the read may succeed — accept either outcome but require the error
            // path, when taken, to produce a typed error rather than throwing.
            if (key is null)
            {
                Assert.NotNull(error);
                Assert.Contains("failed to read key file", error, StringComparison.Ordinal);
                Assert.Contains(path, error, StringComparison.Ordinal);
            }
            else
            {
                Assert.Null(error);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ResolveFromStdin_OverCap_Errors()
    {
        // FakeTextReader returns the whole string on first ReadToEnd; for the bounded read
        // path we need the buffered Read(buffer, offset, count) overload to drain it. The
        // test fake's Read(...) is the standard TextReader behaviour (delegates to its
        // backing string), so this exercises the cap path.
        var stderr = new StringWriter();
        byte[]? key = KeyResolver.Resolve(
            source: KeySource.Stdin(),
            stdin: new FakeTextReader(new string('x', KeyResolver.MaxKeySizeBytes + 1)),
            stripTrailingNewline: true,
            stderr: stderr,
            out string? error);
        Assert.Null(key);
        Assert.NotNull(error);
        Assert.Contains("cap", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stdin", error, StringComparison.OrdinalIgnoreCase);
    }
}

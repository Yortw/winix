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
}

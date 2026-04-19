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
        Assert.Contains("not set", error);
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
        Assert.Contains("--key exposes the key", stderrText);
        Assert.Contains("ps", stderrText);
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
        Assert.Contains("not found", error);
        Assert.Null(key);
    }

    [Fact]
    public void ResolveFromFile_GroupReadable_Unix_EmitsWarning()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

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
            Assert.Contains("readable by group/other", stderr.ToString());
        }
        finally { File.Delete(path); }
    }
}

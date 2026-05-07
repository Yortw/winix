#nullable enable

using System.IO;
using Xunit;
using Winix.Clip;
using Yort.ShellKit;

namespace Winix.Clip.Tests;

/// <summary>
/// Integration-level tests for <see cref="Cli.Run"/> — exercise the buffer-and-decide
/// stdin flow, the dispatch into copy/paste/clear, and the exit-code mappings end-to-end
/// against fake backends. The strict-UTF-8 decoder has its own focused tests in
/// <see cref="StrictUtf8DecoderTests"/>.
/// </summary>
public class CliRunTests
{
    [Fact]
    public void Run_ExplicitCopyWithEmptyStdin_CopiesEmptyAndReturnsZero()
    {
        // Explicit --copy honours the user's intent regardless of stdin content.
        // Empty input → empty string copy. Pre-F1 a similar test would have surfaced
        // the BOM-bypass; here we just confirm the empty-input path is intact.
        var fake = new FakeBackend();
        int exit = RunCli(
            args: new[] { "-c" },
            stdinBytes: System.Array.Empty<byte>(),
            isRedirected: true,
            backend: fake);

        Assert.Equal(0, exit);
        Assert.Single(fake.CopiedTexts);
        Assert.Equal(string.Empty, fake.CopiedTexts[0]);
        Assert.Equal(0, fake.PasteCalls);
        Assert.Equal(0, fake.ClearCalls);
    }

    [Fact]
    public void Run_AutodetectEmptyRedirectedStdin_PastesPerNoContentRule()
    {
        // F2 byte-level pin: empty redirected stdin must route to PASTE (not copy-empty).
        // ModeResolver alone gets a bool; this test exercises the buffer-and-decide flow
        // that produces that bool. A future refactor that re-introduced
        // Console.IsInputRedirected directly (or any predicate that doesn't actually
        // inspect bytes) would silently regress F2; this test would catch it because
        // backend.PasteText must be the call observed, not backend.CopyText.
        var fake = new FakeBackend { PasteResult = "previous-clipboard-content" };
        var stdout = new StringWriter();

        int exit = RunCli(
            args: System.Array.Empty<string>(),
            stdinBytes: System.Array.Empty<byte>(),
            isRedirected: true,
            backend: fake,
            stdout: stdout);

        Assert.Equal(0, exit);
        Assert.Equal(1, fake.PasteCalls);
        Assert.Empty(fake.CopiedTexts);
        Assert.Equal("previous-clipboard-content", stdout.ToString());
    }

    [Fact]
    public void Run_AutodetectNonEmptyRedirectedStdin_CopiesContent()
    {
        var fake = new FakeBackend();

        int exit = RunCli(
            args: System.Array.Empty<string>(),
            stdinBytes: System.Text.Encoding.UTF8.GetBytes("hello world"),
            isRedirected: true,
            backend: fake);

        Assert.Equal(0, exit);
        Assert.Single(fake.CopiedTexts);
        Assert.Equal("hello world", fake.CopiedTexts[0]);
    }

    [Fact]
    public void Run_AutodetectNotRedirected_PastesWithoutTouchingStdin()
    {
        // Bare `clip` interactive — stdin not redirected. The buffer-and-decide flow
        // skips the read entirely (would block on a TTY) and routes to paste.
        var fake = new FakeBackend { PasteResult = "tty-paste-target" };
        var stdout = new StringWriter();

        int exit = RunCli(
            args: System.Array.Empty<string>(),
            stdinBytes: System.Array.Empty<byte>(),
            isRedirected: false,
            backend: fake,
            stdout: stdout);

        Assert.Equal(0, exit);
        Assert.Equal(1, fake.PasteCalls);
        Assert.Empty(fake.CopiedTexts);
        Assert.Equal("tty-paste-target", stdout.ToString());
    }

    [Fact]
    public void Run_CopyInvalidUtf8_ReturnsUsageErrorAndDoesNotCallBackend()
    {
        // The DoCopy path delegates to StrictUtf8Decoder. Confirm the dispatch:
        // invalid bytes → exit 125, error on stderr, backend.CopyText not invoked.
        var fake = new FakeBackend();
        var stderr = new StringWriter();

        int exit = RunCli(
            args: new[] { "-c" },
            stdinBytes: new byte[] { 0xFF, 0xFE, 0xFD },
            isRedirected: true,
            backend: fake,
            stderr: stderr);

        Assert.Equal(ExitCode.UsageError, exit);
        Assert.Empty(fake.CopiedTexts);
        Assert.Contains("invalid UTF-8", stderr.ToString());
    }

    [Fact]
    public void Run_CopyThrowsClipboardException_ReturnsNotExecutable()
    {
        // F-equivalent: ClipboardException → exit 126 mapping. The README's exit-codes
        // table promises 126 for "clipboard busy or helper failure" but no test
        // previously exercised the catch-and-map at the dispatch layer. A future
        // refactor mis-mapping the catch (to 0, 125, or any other code) would now fail.
        var fake = new FakeBackend
        {
            CopyThrows = new ClipboardException(
                "test: clipboard busy (another process holds it)",
                new System.ComponentModel.Win32Exception(5)),
        };
        var stderr = new StringWriter();

        int exit = RunCli(
            args: new[] { "-c" },
            stdinBytes: System.Text.Encoding.UTF8.GetBytes("payload"),
            isRedirected: true,
            backend: fake,
            stderr: stderr);

        Assert.Equal(ExitCode.NotExecutable, exit);
        Assert.Contains("clip:", stderr.ToString());
        Assert.Contains("clipboard busy", stderr.ToString());
    }

    [Fact]
    public void Run_PasteThrowsClipboardException_ReturnsNotExecutable()
    {
        var fake = new FakeBackend
        {
            PasteThrows = new ClipboardException("test: GlobalLock failed", new System.Exception()),
        };
        var stderr = new StringWriter();

        int exit = RunCli(
            args: new[] { "-p" },
            stdinBytes: System.Array.Empty<byte>(),
            isRedirected: false,
            backend: fake,
            stderr: stderr);

        Assert.Equal(ExitCode.NotExecutable, exit);
        Assert.Contains("GlobalLock failed", stderr.ToString());
    }

    [Fact]
    public void Run_BackendFactoryReturnsNull_ReturnsNotFoundWithError()
    {
        // Linux helper-not-installed path: factory returns (null, "install xclip / xsel...").
        var stderr = new StringWriter();

        int exit = Cli.Run(
            args: System.Array.Empty<string>(),
            payloadStdin: new MemoryStream(),
            isStdinRedirected: false,
            stdout: new StringWriter(),
            stderr: stderr,
            backendFactory: _ => new BackendResolution(null, "clip: install xclip or wl-clipboard"));

        Assert.Equal(ExitCode.NotFound, exit);
        Assert.Contains("install xclip", stderr.ToString());
    }

    // --- Helpers ---

    private static int RunCli(
        string[] args,
        byte[] stdinBytes,
        bool isRedirected,
        FakeBackend backend,
        TextWriter? stdout = null,
        TextWriter? stderr = null)
    {
        return Cli.Run(
            args: args,
            payloadStdin: new MemoryStream(stdinBytes),
            isStdinRedirected: isRedirected,
            stdout: stdout ?? new StringWriter(),
            stderr: stderr ?? new StringWriter(),
            backendFactory: _ => new BackendResolution(backend, null));
    }

    private sealed class FakeBackend : IClipboardBackend
    {
        public System.Collections.Generic.List<string> CopiedTexts { get; } = new();
        public string PasteResult { get; set; } = string.Empty;
        public int PasteCalls { get; private set; }
        public int ClearCalls { get; private set; }
        public ClipboardException? CopyThrows { get; set; }
        public ClipboardException? PasteThrows { get; set; }
        public ClipboardException? ClearThrows { get; set; }

        public void CopyText(string text)
        {
            if (CopyThrows is not null) { throw CopyThrows; }
            CopiedTexts.Add(text);
        }

        public string PasteText()
        {
            PasteCalls++;
            if (PasteThrows is not null) { throw PasteThrows; }
            return PasteResult;
        }

        public void Clear()
        {
            ClearCalls++;
            if (ClearThrows is not null) { throw ClearThrows; }
        }
    }
}

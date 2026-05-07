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
        Assert.Contains("invalid UTF-8", stderr.ToString(), StringComparison.Ordinal);
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
        Assert.Contains("clip:", stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("clipboard busy", stderr.ToString(), StringComparison.Ordinal);
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
        Assert.Contains("GlobalLock failed", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_StdinReadThrowsIoException_ReturnsNotExecutableWithClipPrefix()
    {
        // Round-2 SFH I2 regression: a producer that dies mid-stream raises IOException
        // out of Stream.CopyTo. Pre-fix that escaped Cli.Run unhandled and surfaced as
        // a runtime stack trace on stderr. Post-fix it exits 126 with the documented
        // "clip: ..." prefix.
        var fake = new FakeBackend();
        var stderr = new StringWriter();

        int exit = Cli.Run(
            args: System.Array.Empty<string>(),
            payloadStdin: new ThrowingStream(),
            isStdinRedirected: true,
            stdout: new StringWriter(),
            stderr: stderr,
            backendFactory: _ => new BackendResolution(fake, null));

        Assert.Equal(ExitCode.NotExecutable, exit);
        Assert.Contains("clip:", stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("failed to read stdin", stderr.ToString(), StringComparison.Ordinal);
        // Backend never reached — no clipboard mutation on a stdin failure.
        Assert.Empty(fake.CopiedTexts);
        Assert.Equal(0, fake.PasteCalls);
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
        Assert.Contains("install xclip", stderr.ToString(), StringComparison.Ordinal);
    }

    // ---- Fresh-eyes round (post-r4) TA I1: --raw end-to-end coverage -----------

    [Fact]
    public void Run_PasteWithRaw_PreservesTrailingNewline()
    {
        // Fresh-eyes test-analyzer caught that --raw / -r had no end-to-end pin
        // through Cli.Run. The README's flagship "Newline handling" contract says
        // paste strips one trailing \n by default, but --raw disables stripping.
        // NewlineStripping is unit-tested in isolation, but a refactor that dropped
        // the `if (!raw)` guard in DoPaste would silently break the documented
        // flag without breaking any prior test. The paired-test design below pins
        // the difference between the two modes against the same backend payload.
        var fake = new FakeBackend { PasteResult = "foo\n" };
        var stdout = new StringWriter();

        int exit = Cli.Run(
            args: new[] { "-p", "-r" },
            payloadStdin: new MemoryStream(),
            isStdinRedirected: false,
            stdout: stdout,
            stderr: new StringWriter(),
            backendFactory: _ => new BackendResolution(fake, null));

        Assert.Equal(0, exit);
        // --raw: byte-exact paste, trailing \n preserved.
        Assert.Equal("foo\n", stdout.ToString());
    }

    [Fact]
    public void Run_PasteWithoutRaw_StripsTrailingNewline()
    {
        // Sibling of Run_PasteWithRaw — same backend payload "foo\n", different
        // flag, asserts the strip happens. Together they pin the contract that
        // --raw is the ON/OFF lever for trailing-newline behaviour.
        var fake = new FakeBackend { PasteResult = "foo\n" };
        var stdout = new StringWriter();

        int exit = Cli.Run(
            args: new[] { "-p" },
            payloadStdin: new MemoryStream(),
            isStdinRedirected: false,
            stdout: stdout,
            stderr: new StringWriter(),
            backendFactory: _ => new BackendResolution(fake, null));

        Assert.Equal(0, exit);
        // No --raw: trailing newline stripped per $(...)-shell-substitution semantics.
        Assert.Equal("foo", stdout.ToString());
    }

    [Fact]
    public void Run_PasteWithoutRaw_StripsCrlfTrailing()
    {
        // CRLF case — README explicitly mentions that paste strips one trailing
        // \n OR \r\n. Without this row, a refactor that only handled \n would
        // pass Run_PasteWithoutRaw_StripsTrailingNewline.
        var fake = new FakeBackend { PasteResult = "foo\r\n" };
        var stdout = new StringWriter();

        int exit = Cli.Run(
            args: new[] { "-p" },
            payloadStdin: new MemoryStream(),
            isStdinRedirected: false,
            stdout: stdout,
            stderr: new StringWriter(),
            backendFactory: _ => new BackendResolution(fake, null));

        Assert.Equal(0, exit);
        Assert.Equal("foo", stdout.ToString());
    }

    // ---- Round-7 TA I2: --describe shape regression pin -------------------------

    [Fact]
    public void Run_Describe_EmitsStableJsonShapeWithExpectedReplacesAndComposesWith()
    {
        // Round-7 fresh-eyes test-analyzer I2: the --describe JSON output is the
        // AI-discoverability contract. Round-3 caught two broken composes_with
        // patterns (digest sha256 file, qr text "hello") that had been wrong since
        // round-1. Round-7 docs-auditor caught the --primary example was framed
        // narrowly (paste-only) when the flag is mode-independent. Both classes
        // would have been caught by a regression test against the JSON shape —
        // this test pins the load-bearing keys so future changes are deliberate
        // rather than accidental.
        //
        // We don't pin the entire JSON byte-for-byte (that would require touching
        // this test on every legitimate description tweak). Instead, pin:
        //   - tool name and the structural top-level keys
        //   - replaces[] contains the canonical helper-set tools (clip.exe pbcopy
        //     pbpaste xclip xsel wl-copy wl-paste)
        //   - composes_with[] has at least the three documented entries
        //     (ids, digest, qr) — catches accidental removal
        //   - exit_codes[] has all four documented codes (0, 125, 126, 127)
        // ShellKit handles --describe inside parser.Parse(args), writing directly to
        // Console.Out (not the passed stdout TextWriter). To assert on JSON content
        // we capture Console.Out for the duration of the call.
        TextWriter savedConsoleOut = Console.Out;
        var stdout = new StringWriter();
        Console.SetOut(stdout);
        int exit;
        try
        {
            exit = Cli.Run(
                args: new[] { "--describe" },
                payloadStdin: new MemoryStream(),
                isStdinRedirected: false,
                stdout: stdout,
                stderr: new StringWriter(),
                backendFactory: _ => throw new System.InvalidOperationException(
                    "backend factory must not be invoked for --describe"));
        }
        finally
        {
            Console.SetOut(savedConsoleOut);
        }

        Assert.Equal(0, exit);

        string json = stdout.ToString();
        // Tool identity
        Assert.Contains("\"tool\":\"clip\"", json, StringComparison.Ordinal);

        // Replaces array — load-bearing for the AI-agent "is this the right tool?"
        // decision tree. Removing any of these silently steers agents to the wrong
        // tool when they should have picked clip.
        Assert.Contains("clip.exe", json, StringComparison.Ordinal);
        Assert.Contains("pbcopy", json, StringComparison.Ordinal);
        Assert.Contains("pbpaste", json, StringComparison.Ordinal);
        Assert.Contains("xclip", json, StringComparison.Ordinal);
        Assert.Contains("xsel", json, StringComparison.Ordinal);
        Assert.Contains("wl-copy", json, StringComparison.Ordinal);
        Assert.Contains("wl-paste", json, StringComparison.Ordinal);

        // Composes-with — the patterns themselves are verified at the parser-grammar
        // level by the docs-auditor reviewer; this test only confirms they're
        // present in the JSON surface.
        Assert.Contains("\"tool\":\"ids\"", json, StringComparison.Ordinal);
        Assert.Contains("\"tool\":\"digest\"", json, StringComparison.Ordinal);
        Assert.Contains("\"tool\":\"qr\"", json, StringComparison.Ordinal);

        // Exit codes — all four documented codes must be enumerated. README and
        // man.1 both list 0 / 125 / 126 / 127; --describe is the third source of
        // truth for this contract.
        Assert.Contains("\"code\":0", json, StringComparison.Ordinal);
        Assert.Contains("\"code\":125", json, StringComparison.Ordinal);
        Assert.Contains("\"code\":126", json, StringComparison.Ordinal);
        Assert.Contains("\"code\":127", json, StringComparison.Ordinal);
    }

    // ---- Round-2 TA I1: Clear dispatch path coverage ----------------------------

    [Fact]
    public void Run_ClearMode_InvokesBackendClearAndDoesNotConsumeStdin()
    {
        // Round-2 TA I1: pre-fix CliRunTests covered Copy and Paste dispatch but not
        // the third arm. A refactor that broke Clear's stdin-skip predicate (the
        // `!options.Clear` part of needsStdinRead) would silently consume stdin and
        // could throw — uncaught from the integration tests.
        var fake = new FakeBackend();
        byte[] sentinelBytes = System.Text.Encoding.UTF8.GetBytes("not-consumed-by-clear");
        var stdinStream = new MemoryStream(sentinelBytes);

        int exit = Cli.Run(
            args: new[] { "--clear" },
            payloadStdin: stdinStream,
            isStdinRedirected: true,
            stdout: new StringWriter(),
            stderr: new StringWriter(),
            backendFactory: _ => new BackendResolution(fake, null));

        Assert.Equal(0, exit);
        Assert.Equal(1, fake.ClearCalls);
        Assert.Empty(fake.CopiedTexts);
        Assert.Equal(0, fake.PasteCalls);
        // Stdin bytes never consumed — predicate correctly skipped the read for Clear.
        Assert.Equal(0, stdinStream.Position);
    }

    [Fact]
    public void Run_ClearThrowsClipboardException_ReturnsNotExecutable()
    {
        var fake = new FakeBackend
        {
            ClearThrows = new ClipboardException(
                "test: EmptyClipboard failed",
                new System.ComponentModel.Win32Exception(5)),
        };
        var stderr = new StringWriter();

        int exit = Cli.Run(
            args: new[] { "--clear" },
            payloadStdin: new MemoryStream(),
            isStdinRedirected: false,
            stdout: new StringWriter(),
            stderr: stderr,
            backendFactory: _ => new BackendResolution(fake, null));

        Assert.Equal(ExitCode.NotExecutable, exit);
        Assert.Contains("clip:", stderr.ToString(), StringComparison.Ordinal);
        Assert.Contains("EmptyClipboard failed", stderr.ToString(), StringComparison.Ordinal);
    }

    // ---- Round-2 TA I2: parser early-return paths -------------------------------

    [Fact]
    public void Run_HelpFlag_ReturnsZeroWithoutInvokingBackend()
    {
        // Round-2 TA I2: the parser's IsHandled short-circuit was untested at the
        // Cli.Run integration layer. A backend factory that throws when called
        // proves the short-circuit fires before backend resolution.
        int exit = Cli.Run(
            args: new[] { "--help" },
            payloadStdin: new MemoryStream(),
            isStdinRedirected: false,
            stdout: new StringWriter(),
            stderr: new StringWriter(),
            backendFactory: _ => throw new System.InvalidOperationException(
                "backend factory must not be invoked for --help"));

        Assert.Equal(0, exit);
    }

    [Fact]
    public void Run_DescribeFlag_ReturnsZeroWithoutInvokingBackend()
    {
        int exit = Cli.Run(
            args: new[] { "--describe" },
            payloadStdin: new MemoryStream(),
            isStdinRedirected: false,
            stdout: new StringWriter(),
            stderr: new StringWriter(),
            backendFactory: _ => throw new System.InvalidOperationException(
                "backend factory must not be invoked for --describe"));

        Assert.Equal(0, exit);
    }

    [Fact]
    public void Run_VersionFlag_ReturnsZeroWithoutInvokingBackend()
    {
        int exit = Cli.Run(
            args: new[] { "--version" },
            payloadStdin: new MemoryStream(),
            isStdinRedirected: false,
            stdout: new StringWriter(),
            stderr: new StringWriter(),
            backendFactory: _ => throw new System.InvalidOperationException(
                "backend factory must not be invoked for --version"));

        Assert.Equal(0, exit);
    }

    [Fact]
    public void Run_UnknownFlag_ReturnsUsageErrorWithStderrMessage()
    {
        // Round-2 TA I2: parser-error route via result.HasErrors → result.WriteErrors.
        // Round-3 tightening: assert the user-supplied flag name appears in the error
        // output. NotEmpty alone is satisfied by any stderr write — including a stack
        // trace, a help banner accidentally printed, or an unrelated future message.
        // The flag name is the lowest-friction stable pin (user input echoed back),
        // not a localisable English phrase. Use 3-arg StringComparison.Ordinal per
        // feedback_xunit_assert_culture_aware.md so byte-precise matching applies
        // (the default culture-aware comparison treats some Unicode points as
        // ignorable, which can mask real diff failures).
        var stderr = new StringWriter();

        int exit = Cli.Run(
            args: new[] { "--frobnicate" },
            payloadStdin: new MemoryStream(),
            isStdinRedirected: false,
            stdout: new StringWriter(),
            stderr: stderr,
            backendFactory: _ => throw new System.InvalidOperationException(
                "backend factory must not be invoked on parser error"));

        Assert.Equal(ExitCode.UsageError, exit);
        Assert.Contains("--frobnicate", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_ConflictingModeFlags_ReturnsUsageErrorViaArgumentExceptionRoute()
    {
        // Round-2 TA I2: ClipOptions ArgumentException route via result.WriteError.
        // Pre-fix the catch was untested at the Cli.Run integration layer; only the
        // ClipOptions ctor itself was covered by ClipOptionsTests.
        var stderr = new StringWriter();

        int exit = Cli.Run(
            args: new[] { "-c", "-p" },
            payloadStdin: new MemoryStream(),
            isStdinRedirected: false,
            stdout: new StringWriter(),
            stderr: stderr,
            backendFactory: _ => throw new System.InvalidOperationException(
                "backend factory must not be invoked when ClipOptions rejects"));

        Assert.Equal(ExitCode.UsageError, exit);
        Assert.Contains("cannot be combined", stderr.ToString(), StringComparison.Ordinal);
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

    /// <summary>
    /// Test-only Stream that always throws <see cref="IOException"/> on read. Models a
    /// producer process whose pipe broke mid-stream (e.g. SIGPIPE from a killed
    /// upstream command) so we can exercise <see cref="Cli.Run"/>'s stdin-failure path
    /// deterministically without spawning a real broken pipe.
    /// </summary>
    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new System.NotSupportedException();
        public override long Position
        {
            get => throw new System.NotSupportedException();
            set => throw new System.NotSupportedException();
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
            => throw new IOException("test: simulated broken pipe");
        public override long Seek(long offset, SeekOrigin origin)
            => throw new System.NotSupportedException();
        public override void SetLength(long value)
            => throw new System.NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
            => throw new System.NotSupportedException();
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

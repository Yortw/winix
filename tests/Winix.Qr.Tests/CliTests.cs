#nullable enable
using System;
using System.IO;
using Xunit;
using Winix.Qr;
using Yort.ShellKit;

namespace Winix.Qr.Tests;

// Round-1 review TA-C1, TA-C2, TA-C3 — Cli.Run is the new orchestration seam. These tests pin
// every dispatch path (mode dispatch, mutual-exclusion, error-envelope shapes, exit-code matrix,
// file emission, TTY-binary refusal, format/extension contradiction, overwrite refusal). Pre-fix
// 110 LOC of Program.cs had zero unit-test coverage.
public class CliTests
{
    // Convenience runner: feeds Cli.Run from in-memory readers/writers.
    private static (int exit, string stdout, string stderr, byte[] binary) RunCli(
        string[] args,
        string stdin = "",
        bool stdinRedirected = false,
        bool stdoutIsTty = true)
    {
        StringReader reader = new(stdin);
        StringWriter outW = new();
        StringWriter errW = new();
        MemoryStream binW = new();
        int exit = Cli.Run(args, reader, outW, errW, binW, stdinRedirected, stdoutIsTty);
        return (exit, outW.ToString(), errW.ToString(), binW.ToArray());
    }

    // ── Happy-path conversion ──

    [Fact]
    public void Run_TextPositional_ReturnsZeroAndUnicodeOutput()
    {
        var r = RunCli(new[] { "hello" }, stdoutIsTty: true);
        Assert.Equal(0, r.exit);
        // Unicode renderer emits half-block characters; just assert it's non-empty.
        Assert.NotEmpty(r.stdout);
    }

    [Fact]
    public void Run_TextStdinViaDash_ReadsFromStdin()
    {
        var r = RunCli(new[] { "-" }, stdin: "from-stdin\n", stdinRedirected: true);
        Assert.Equal(0, r.exit);
        Assert.NotEmpty(r.stdout);
    }

    [Fact]
    public void Run_TextNoArgsNoStdin_ReturnsUsageError()
    {
        // No positional, no stdin redirection — Cli.Run treats this as empty payload.
        var r = RunCli(Array.Empty<string>(), stdinRedirected: false);
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Contains("payload is empty", r.stderr, StringComparison.Ordinal);
    }

    // ── Format selection ──

    [Fact]
    public void Run_FormatSvg_EmitsXml()
    {
        var r = RunCli(new[] { "hello", "--format", "svg" }, stdoutIsTty: true);
        Assert.Equal(0, r.exit);
        Assert.Contains("<svg", r.stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_FormatAscii_EmitsAsciiText()
    {
        var r = RunCli(new[] { "hello", "--format", "ascii" }, stdoutIsTty: true);
        Assert.Equal(0, r.exit);
        Assert.NotEmpty(r.stdout);
    }

    // ── TTY-binary refusal (TA-C3) — pre-fix this contract was completely untested. ──

    [Fact]
    public void Run_FormatPng_ToTty_RefusedWithUsageError()
    {
        var r = RunCli(new[] { "hello", "--format", "png" }, stdoutIsTty: true);
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Contains("refusing to write PNG to terminal", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_FormatPng_ToTty_ForceBinary_Allowed()
    {
        var r = RunCli(new[] { "hello", "--format", "png", "--force-binary" }, stdoutIsTty: true);
        Assert.Equal(0, r.exit);
        Assert.NotEmpty(r.binary);
        // PNG magic bytes: 89 50 4E 47 0D 0A 1A 0A
        Assert.Equal(0x89, r.binary[0]);
        Assert.Equal((byte)'P', r.binary[1]);
        Assert.Equal((byte)'N', r.binary[2]);
        Assert.Equal((byte)'G', r.binary[3]);
    }

    [Fact]
    public void Run_FormatPng_StdoutRedirected_AllowedWithoutForceBinary()
    {
        var r = RunCli(new[] { "hello", "--format", "png" }, stdoutIsTty: false);
        Assert.Equal(0, r.exit);
        Assert.NotEmpty(r.binary);
    }

    // ── SFH-I1: --format vs --output extension contradiction ──

    [Fact]
    public void Run_FormatSvgWithPngExtension_RejectedAsContradiction()
    {
        // Pre-fix: silently wrote SVG bytes to a .png file. Now refused at parse-time.
        string tmp = Path.Combine(Path.GetTempPath(), $"qr-mismatch-{Guid.NewGuid():N}.png");
        try
        {
            var r = RunCli(new[] { "hello", "--format", "svg", "--output", tmp });
            Assert.Equal(ExitCode.UsageError, r.exit);
            Assert.Contains("contradicts", r.stderr, StringComparison.Ordinal);
            Assert.False(File.Exists(tmp), "no file should be written when format/extension contradict");
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void Run_FormatPngWithSvgExtension_RejectedAsContradiction()
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"qr-mismatch-{Guid.NewGuid():N}.svg");
        try
        {
            var r = RunCli(new[] { "hello", "--format", "png", "--output", tmp });
            Assert.Equal(ExitCode.UsageError, r.exit);
            Assert.Contains("contradicts", r.stderr, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void Run_FormatMatchesExtension_NoContradiction()
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"qr-match-{Guid.NewGuid():N}.svg");
        try
        {
            var r = RunCli(new[] { "hello", "--format", "svg", "--output", tmp });
            Assert.Equal(0, r.exit);
            Assert.True(File.Exists(tmp));
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void Run_UnknownExtension_NoContradictionEvenWithExplicitFormat()
    {
        // Custom extension (.q) with --format svg should be allowed — only known extensions trigger
        // the mismatch check. Otherwise users couldn't write SVG to e.g. /dev/null or a custom name.
        string tmp = Path.Combine(Path.GetTempPath(), $"qr-custom-{Guid.NewGuid():N}.q");
        try
        {
            var r = RunCli(new[] { "hello", "--format", "svg", "--output", tmp });
            Assert.Equal(0, r.exit);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    // ── SFH-I2: --output overwrite refusal ──

    [Fact]
    public void Run_OutputExists_RefusedWithoutForce()
    {
        // Pre-fix: silently overwrote existing files. Now refused unless --force.
        string tmp = Path.Combine(Path.GetTempPath(), $"qr-existing-{Guid.NewGuid():N}.svg");
        try
        {
            File.WriteAllText(tmp, "user's existing content");
            var r = RunCli(new[] { "hello", "--format", "svg", "--output", tmp });
            Assert.Equal(ExitCode.UsageError, r.exit);
            Assert.Contains("refusing to overwrite", r.stderr, StringComparison.Ordinal);
            // Original content preserved.
            Assert.Equal("user's existing content", File.ReadAllText(tmp));
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void Run_OutputExists_ForceFlag_Overwrites()
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"qr-force-{Guid.NewGuid():N}.svg");
        try
        {
            File.WriteAllText(tmp, "stale");
            var r = RunCli(new[] { "hello", "--format", "svg", "--output", tmp, "--force" });
            Assert.Equal(0, r.exit);
            Assert.Contains("<svg", File.ReadAllText(tmp), StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void Run_OutputDoesNotExist_NoForceNeeded()
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"qr-fresh-{Guid.NewGuid():N}.svg");
        try
        {
            var r = RunCli(new[] { "hello", "--format", "svg", "--output", tmp });
            Assert.Equal(0, r.exit);
            Assert.True(File.Exists(tmp));
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    // ── TA-I5: --security xyz misclassified as runtime error pre-fix ──

    [Fact]
    public void Run_WifiBadSecurity_ReturnsUsageErrorNotRuntimeError()
    {
        // Pre-fix WifiPayload threw ArgumentException → routed to exit 126 (runtime). A bad flag
        // value is a usage error → exit 125. Now both InvalidOperationException AND ArgumentException
        // from PayloadBuilder.Build map to 125.
        var r = RunCli(new[] { "wifi", "--ssid", "Net", "--password", "pw", "--security", "xyz" });
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Contains("Unknown security type", r.stderr, StringComparison.Ordinal);
    }

    // ── Subcommand dispatch ──

    [Fact]
    public void Run_WifiSubcommand_EmitsWifiUri()
    {
        var r = RunCli(new[] { "wifi", "--ssid", "Net", "--password", "pw", "--security", "wpa2", "--format", "ascii" });
        Assert.Equal(0, r.exit);
        Assert.NotEmpty(r.stdout);
    }

    [Fact]
    public void Run_GeoSubcommand_LatOutOfRange_RejectedAtParseTime()
    {
        // Parser-time validator catches this — exit 125, no payload built.
        var r = RunCli(new[] { "geo", "--lat", "91", "--lon", "0" });
        Assert.Equal(ExitCode.UsageError, r.exit);
    }

    [Fact]
    public void Run_TelGarbage_RejectedAsUsageError()
    {
        // SFH-I3: pre-fix `qr tel --number "+1 555 abc;DROP"` silently produced an unscannable URI.
        // Now sanitiser rejects characters outside RFC 3966 grammar. Exit 125 (usage), not 126.
        var r = RunCli(new[] { "tel", "--number", "+1 555 abc" });
        Assert.Equal(ExitCode.UsageError, r.exit);
        Assert.Contains("disallowed character", r.stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_TelWithSpaces_StrippedAndAccepted()
    {
        // Spaces are common copy/paste artefacts; sanitiser strips them.
        var r = RunCli(new[] { "tel", "--number", "+1 555 1234567", "--format", "ascii" });
        Assert.Equal(0, r.exit);
    }

    // ── ShellKit-handled flags ──

    [Fact]
    public void Run_Help_ExitsZero()
    {
        var r = RunCli(new[] { "--help" });
        Assert.Equal(0, r.exit);
    }

    [Fact]
    public void Run_Version_ExitsZero()
    {
        var r = RunCli(new[] { "--version" });
        Assert.Equal(0, r.exit);
    }

    [Fact]
    public void Run_Describe_ExitsZero()
    {
        var r = RunCli(new[] { "--describe" });
        Assert.Equal(0, r.exit);
    }

    [Fact]
    public void Run_WifiHelp_ExitsZero_AndIsSubcommandSpecific()
    {
        // I1: per-subcommand --describe/--help should be distinct from text-mode.
        var r = RunCli(new[] { "wifi", "--help" });
        Assert.Equal(0, r.exit);
        // ShellKit writes help to stdout — and qr wifi --help should mention --ssid which text-mode doesn't.
        // The actual capture goes through ShellKit's Console writer not our test sink, so we only
        // assert the exit code here. (Subcommand-specific content is exercised by ArgParser tests.)
    }
}

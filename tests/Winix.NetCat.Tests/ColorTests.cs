#nullable enable

using System;
using Winix.NetCat;
using Xunit;

namespace Winix.NetCat.Tests;

/// <summary>
/// Regression tests locking nc's --color emission paths at the formatter layer.
/// Guards against a future regression where colour is silently unwired.
/// </summary>
/// <remarks>
/// Seam note: nc has no Cli.Run library seam — Program.cs is an async entry point that
/// references Console.OpenStandardInput/Output directly and uses live network I/O.
/// Colour is wired at:
///   Program.BuildOptions: bool useColor = result.ResolveColor(checkStdErr: true)
///   DispatchCoreAsync (check mode): Console.Out.WriteLine(Formatting.FormatOpenPortLine(port, options.UseColor))
///   DispatchCoreAsync (check mode): stderr.WriteLine(Formatting.FormatClosedPortLine(port, options.UseColor))
///   DispatchCoreAsync (error paths): stderr.WriteLine(Formatting.FormatErrorLine(msg, options.UseColor))
/// Output destination:
///   FormatOpenPortLine → stdout (check mode open port)
///   FormatClosedPortLine → stderr (check mode closed/verbose)
///   FormatErrorLine / FormatWarningLine → stderr
/// This test suite covers the formatter layer directly — confirming that each
/// formatted method emits ESC when useColor=true and suppresses it when false.
/// Production wiring is confirmed by code inspection: options.UseColor is the
/// single field populated from useColor in BuildOptions, forwarded to all Formatting
/// calls via options.UseColor. A process-spawn colour regression test would require
/// live network connections or a loopback listener, which is unsuitable for unit tests.
/// </remarks>
public sealed class ColorTests
{
    private static readonly string Esc = ((char)27).ToString();

    // ── FormatOpenPortLine (check mode, open port → stdout) ───────────────────────

    [Fact]
    public void FormatOpenPortLine_ColorTrue_ContainsEscape()
    {
        // Open-port line is green (AnsiColor.Green) in the production check-mode dispatch.
        string line = Formatting.FormatOpenPortLine(443, useColor: true);
        Assert.Contains(Esc, line, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatOpenPortLine_ColorFalse_NoEscape()
    {
        string line = Formatting.FormatOpenPortLine(443, useColor: false);
        Assert.DoesNotContain(Esc, line, StringComparison.Ordinal);
        Assert.Contains("443", line, StringComparison.Ordinal);
        Assert.Contains("open", line, StringComparison.Ordinal);
    }

    // ── FormatClosedPortLine (check mode, closed port → stderr via --verbose) ─────

    [Fact]
    public void FormatClosedPortLine_ColorTrue_ContainsEscape()
    {
        // Closed-port line is red (AnsiColor.Red) in verbose check-mode dispatch.
        string line = Formatting.FormatClosedPortLine(80, useColor: true);
        Assert.Contains(Esc, line, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatClosedPortLine_ColorFalse_NoEscape()
    {
        string line = Formatting.FormatClosedPortLine(80, useColor: false);
        Assert.DoesNotContain(Esc, line, StringComparison.Ordinal);
        Assert.Contains("80", line, StringComparison.Ordinal);
        Assert.Contains("closed", line, StringComparison.Ordinal);
    }

    // ── FormatErrorLine (error path → stderr) ─────────────────────────────────────

    [Fact]
    public void FormatErrorLine_ColorTrue_ContainsEscape()
    {
        // Error lines use AnsiColor.Red wrapping "nc: " + message.
        string line = Formatting.FormatErrorLine("connection refused", useColor: true);
        Assert.Contains(Esc, line, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatErrorLine_ColorFalse_NoEscape()
    {
        string line = Formatting.FormatErrorLine("connection refused", useColor: false);
        Assert.DoesNotContain(Esc, line, StringComparison.Ordinal);
        Assert.Contains("connection refused", line, StringComparison.Ordinal);
    }
}

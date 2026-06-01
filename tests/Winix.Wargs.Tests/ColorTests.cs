#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Winix.Wargs;
using Xunit;

namespace Winix.Wargs.Tests;

public class ColorTests
{
    private static readonly string Esc = ((char)27).ToString();

    private static WargsResult Failed()
        => new WargsResult(TotalJobs: 3, Succeeded: 1, Failed: 2, Skipped: 0,
                           WallTime: TimeSpan.Zero, Jobs: new List<JobResult>());

    // --- FormatHumanSummary formatter-level tests ---

    [Fact]
    public void FailureSummary_WithColor_EmitsAnsi()
    {
        string? s = Formatting.FormatHumanSummary(Failed(), useColor: true);
        Assert.NotNull(s);
        Assert.Contains(Esc, s!, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureSummary_NoColor_IsPlain()
    {
        string? s = Formatting.FormatHumanSummary(Failed(), useColor: false);
        Assert.NotNull(s);
        Assert.DoesNotContain(Esc, s!, StringComparison.Ordinal);
        Assert.Contains("2/3 jobs failed", s!, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureSummary_NoColor_ExactString()
    {
        // A3: pin the exact plain string so AnsiColor.X(false)="" is verified byte-precisely.
        string? s = Formatting.FormatHumanSummary(Failed(), useColor: false);
        Assert.Equal("wargs: 2/3 jobs failed", s);
    }

    [Fact]
    public void NoFailures_ReturnsNull()
    {
        var ok = new WargsResult(TotalJobs: 1, Succeeded: 1, Failed: 0, Skipped: 0,
                                 WallTime: TimeSpan.Zero, Jobs: new List<JobResult>());
        Assert.Null(Formatting.FormatHumanSummary(ok, useColor: true));
    }

    // --- HumanSummary.Emit seam tests (A2 / P2-A wiring guard) ---

    /// <summary>Builds a ParseResult that has resolved --color=always, sufficient for
    /// testing ResolveColor threading without constructing the full wargs parser.</summary>
    private static Yort.ShellKit.ParseResult ParsedWith(string colorFlag)
        => new Yort.ShellKit.CommandLineParser("wargs", "0.0.0")
               .StandardFlags()
               .Parse(new[] { colorFlag });

    [Fact]
    public void Emit_ColorAlways_WritesAnsiSummary()
    {
        var sw = new StringWriter();
        HumanSummary.Emit(ParsedWith("--color=always"), Failed(), sw);
        Assert.Contains(Esc, sw.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_NoColor_WritesPlainSummary()
    {
        var sw = new StringWriter();
        HumanSummary.Emit(ParsedWith("--no-color"), Failed(), sw);
        // A3: exact plain string including trailing newline from WriteLine.
        Assert.Equal("wargs: 2/3 jobs failed" + Environment.NewLine, sw.ToString());
        Assert.DoesNotContain(Esc, sw.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_NoFailures_WritesNothing()
    {
        var sw = new StringWriter();
        var ok = new WargsResult(TotalJobs: 1, Succeeded: 1, Failed: 0, Skipped: 0,
                                 WallTime: TimeSpan.Zero, Jobs: new List<JobResult>());
        HumanSummary.Emit(ParsedWith("--no-color"), ok, sw);
        Assert.Equal(string.Empty, sw.ToString());
    }
}

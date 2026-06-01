#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Winix.Less;
using Xunit;

namespace Winix.Less.Tests;

/// <summary>
/// Colour regression tests for <c>less</c>.
///
/// DESIGN FINDING — pass-through, not own-emission:
/// <c>less</c> does not generate its own coloured text. It is a pass-through pager.
/// ANSI SGR sequences (colour, bold, etc.) originate in the input content and are either
/// preserved or stripped depending on <see cref="LessOptions.StripAnsi"/>.
///
/// The interactive <see cref="Screen"/> class does write two terminal-control sequences
/// unconditionally to <see cref="Console"/> (reverse-video status bar: <c>\x1b[7m</c>,
/// cursor hide/show: <c>\x1b[?25l</c>/<c>\x1b[?25h</c>), but these are structural
/// terminal-control codes, not colour emission, and <see cref="Screen"/> writes directly
/// to <see cref="Console"/> with no injectable <see cref="TextWriter"/> seam — they cannot
/// be tested at renderer level without a real tty.
///
/// The testable colour-relevant code path is <see cref="Pager.DumpAllLines"/> /
/// <see cref="Pager.DumpFromViewport"/>, which respects <see cref="LessOptions.StripAnsi"/>
/// to control whether ANSI sequences in the input reach the output. These tests pin that
/// pass-through contract from a colour-regression perspective:
/// <list type="bullet">
///   <item>StripAnsi=false (colour allowed) — ESC sequences from input survive to output.</item>
///   <item>StripAnsi=true (no-colour mode) — ESC sequences are stripped before output.</item>
/// </list>
/// The <see cref="LessOptions.StripAnsi"/> flag is set by the caller (Cli) after consulting
/// <c>NO_COLOR</c>, <c>--no-color</c>, and <c>--color</c> via ShellKit's ResolveColor.
/// </summary>
public sealed class ColourRegressionTests
{
    private static readonly string Esc = ((char)27).ToString();

    private static LessOptions OptionsWithStripAnsi(bool stripAnsi)
    {
        return LessOptions.Resolve(Array.Empty<string>(), lessEnvVar: null, stripAnsi: stripAnsi);
    }

    // ── DumpAllLines — the testable colour-gated path ─────────────────────────────

    /// <summary>
    /// With StripAnsi=false (colour allowed), ANSI SGR sequences in input content
    /// must survive to output. This is the "colour on" regression pin.
    /// </summary>
    [Fact]
    public void DumpAllLines_StripAnsiFalse_EscSurvivesToOutput()
    {
        var pager = new Pager(OptionsWithStripAnsi(stripAnsi: false));
        var lines = new List<string>
        {
            Esc + "[32mhello" + Esc + "[0m",   // green "hello"
            "plain line",
        };

        var capture = new StringWriter();
        var savedOut = Console.Out;
        Console.SetOut(capture);
        try
        {
            pager.DumpAllLines(lines);
        }
        finally
        {
            Console.SetOut(savedOut);
        }

        string output = capture.ToString();
        Assert.Contains(Esc, output, StringComparison.Ordinal);
        Assert.Contains("hello", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// With StripAnsi=true (no-colour mode), ANSI SGR sequences in input content
    /// must be stripped before they reach output. This is the "colour off" regression pin.
    /// </summary>
    [Fact]
    public void DumpAllLines_StripAnsiTrue_EscRemovedFromOutput()
    {
        var pager = new Pager(OptionsWithStripAnsi(stripAnsi: true));
        var lines = new List<string>
        {
            Esc + "[32mhello" + Esc + "[0m",
            "plain line",
        };

        var capture = new StringWriter();
        var savedOut = Console.Out;
        Console.SetOut(capture);
        try
        {
            pager.DumpAllLines(lines);
        }
        finally
        {
            Console.SetOut(savedOut);
        }

        string output = capture.ToString();
        Assert.DoesNotContain(Esc, output, StringComparison.Ordinal);
        // Visible content is preserved after stripping
        Assert.Contains("hello", output, StringComparison.Ordinal);
        Assert.Contains("plain line", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Multiple ANSI sequences across multiple input lines — all survive when StripAnsi=false.
    /// Ensures the gate is not accidentally limited to the first line.
    /// </summary>
    [Fact]
    public void DumpAllLines_StripAnsiFalse_MultipleLines_AllEscSurvive()
    {
        var pager = new Pager(OptionsWithStripAnsi(stripAnsi: false));
        var lines = new List<string>
        {
            Esc + "[31mred" + Esc + "[0m",
            Esc + "[32mgreen" + Esc + "[0m",
            Esc + "[34mblue" + Esc + "[0m",
        };

        var capture = new StringWriter();
        var savedOut = Console.Out;
        Console.SetOut(capture);
        try
        {
            pager.DumpAllLines(lines);
        }
        finally
        {
            Console.SetOut(savedOut);
        }

        string output = capture.ToString();
        Assert.Contains(Esc + "[31m", output, StringComparison.Ordinal);
        Assert.Contains(Esc + "[32m", output, StringComparison.Ordinal);
        Assert.Contains(Esc + "[34m", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Multiple ANSI sequences across multiple input lines — all stripped when StripAnsi=true.
    /// </summary>
    [Fact]
    public void DumpAllLines_StripAnsiTrue_MultipleLines_AllEscRemoved()
    {
        var pager = new Pager(OptionsWithStripAnsi(stripAnsi: true));
        var lines = new List<string>
        {
            Esc + "[31mred" + Esc + "[0m",
            Esc + "[32mgreen" + Esc + "[0m",
            Esc + "[34mblue" + Esc + "[0m",
        };

        var capture = new StringWriter();
        var savedOut = Console.Out;
        Console.SetOut(capture);
        try
        {
            pager.DumpAllLines(lines);
        }
        finally
        {
            Console.SetOut(savedOut);
        }

        string output = capture.ToString();
        Assert.DoesNotContain(Esc, output, StringComparison.Ordinal);
        Assert.Contains("red", output, StringComparison.Ordinal);
        Assert.Contains("green", output, StringComparison.Ordinal);
        Assert.Contains("blue", output, StringComparison.Ordinal);
    }

    // ── AnsiText.StripAnsi — the underlying stripping primitive ──────────────────

    /// <summary>
    /// AnsiText.StripAnsi removes all SGR sequences, leaving only visible text.
    /// Pins the primitive that the StripAnsi=true path relies on.
    /// </summary>
    [Fact]
    public void StripAnsi_RemovesAllSgrSequences()
    {
        string input = Esc + "[1m" + Esc + "[32mbold green" + Esc + "[0m plain";

        string result = AnsiText.StripAnsi(input);

        Assert.DoesNotContain(Esc, result, StringComparison.Ordinal);
        Assert.Equal("bold green plain", result);
    }

    /// <summary>
    /// AnsiText.StripAnsi on plain text (no escapes) returns the input unchanged.
    /// </summary>
    [Fact]
    public void StripAnsi_PlainText_Unchanged()
    {
        string input = "no escapes here";

        string result = AnsiText.StripAnsi(input);

        Assert.Equal(input, result);
    }
}

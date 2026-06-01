#nullable enable

using System;
using System.IO;
using Winix.Peep;
using Xunit;

namespace Winix.Peep.Tests;

/// <summary>
/// Colour regression tests for <see cref="ScreenRenderer"/>: locks ESC emission so that
/// a future unwired-colour regression (useColor flag ignored) is caught immediately.
/// These tests target the renderer-level functions directly — the interactive screen loop
/// is not driven end-to-end.
/// </summary>
public sealed class ColourRegressionTests
{
    private static readonly string Esc = ((char)27).ToString();

    // ── FormatHeader ─────────────────────────────────────────────────────────────

    /// <summary>
    /// With useColor=true and a non-null exit code, FormatHeader must emit at least one
    /// ESC character (the ANSI colour wrapping the exit code).
    /// </summary>
    [Fact]
    public void FormatHeader_UseColorTrue_EmitsEsc()
    {
        string header = ScreenRenderer.FormatHeader(
            intervalSeconds: 2.0,
            command: "dotnet test",
            timestamp: new DateTime(2026, 6, 1, 9, 0, 0),
            exitCode: 0,
            runCount: 1,
            isPaused: false,
            useColor: true);

        Assert.Contains(Esc, header, StringComparison.Ordinal);
    }

    /// <summary>
    /// With useColor=false, FormatHeader must not emit any ESC character.
    /// </summary>
    [Fact]
    public void FormatHeader_UseColorFalse_NoEsc()
    {
        string header = ScreenRenderer.FormatHeader(
            intervalSeconds: 2.0,
            command: "dotnet test",
            timestamp: new DateTime(2026, 6, 1, 9, 0, 0),
            exitCode: 0,
            runCount: 1,
            isPaused: false,
            useColor: false);

        Assert.DoesNotContain(Esc, header, StringComparison.Ordinal);
    }

    /// <summary>
    /// Non-zero exit code with useColor=true should emit the red ANSI escape specifically.
    /// </summary>
    [Fact]
    public void FormatHeader_UseColorTrue_NonZeroExit_EmitsRedEsc()
    {
        string header = ScreenRenderer.FormatHeader(
            intervalSeconds: 5.0,
            command: "make",
            timestamp: new DateTime(2026, 6, 1, 9, 0, 0),
            exitCode: 2,
            runCount: 3,
            isPaused: false,
            useColor: true);

        // Red ANSI code follows ESC [31m
        Assert.Contains(Esc + "[31m", header, StringComparison.Ordinal);
    }

    /// <summary>
    /// Non-zero exit code with useColor=false must not emit any ANSI red code.
    /// </summary>
    [Fact]
    public void FormatHeader_UseColorFalse_NonZeroExit_NoRedEsc()
    {
        string header = ScreenRenderer.FormatHeader(
            intervalSeconds: 5.0,
            command: "make",
            timestamp: new DateTime(2026, 6, 1, 9, 0, 0),
            exitCode: 2,
            runCount: 3,
            isPaused: false,
            useColor: false);

        Assert.DoesNotContain(Esc, header, StringComparison.Ordinal);
    }

    // ── FormatWatchLine ───────────────────────────────────────────────────────────

    /// <summary>
    /// With useColor=true, FormatWatchLine must emit at least one ESC character
    /// (the dim ANSI escape wrapping "Watching:").
    /// </summary>
    [Fact]
    public void FormatWatchLine_UseColorTrue_EmitsEsc()
    {
        string? line = ScreenRenderer.FormatWatchLine(
            new[] { "src/**/*.cs" },
            useColor: true);

        Assert.NotNull(line);
        Assert.Contains(Esc, line, StringComparison.Ordinal);
    }

    /// <summary>
    /// With useColor=false, FormatWatchLine must not emit any ESC character.
    /// </summary>
    [Fact]
    public void FormatWatchLine_UseColorFalse_NoEsc()
    {
        string? line = ScreenRenderer.FormatWatchLine(
            new[] { "src/**/*.cs" },
            useColor: false);

        Assert.NotNull(line);
        Assert.DoesNotContain(Esc, line, StringComparison.Ordinal);
    }

    // ── RenderOutputWithDiff ─────────────────────────────────────────────────────

    /// <summary>
    /// Diff highlighting always emits the 256-colour background ANSI sequence on
    /// changed lines (no useColor gate — diff mode is explicitly activated by the user).
    /// </summary>
    [Fact]
    public void RenderOutputWithDiff_ChangedLine_EmitsEsc()
    {
        using var writer = new StringWriter();
        string current = "same\nchanged\nsame";
        string previous = "same\noriginal\nsame";

        ScreenRenderer.RenderOutputWithDiff(writer, current, previous, availableHeight: 10, scrollOffset: 0);

        string output = writer.ToString();
        // 256-colour background highlight: ESC [ 4 8 ; 5 ; 1 7 m
        Assert.Contains(Esc + "[48;5;17m", output, StringComparison.Ordinal);
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Winix.Less;
using Xunit;

namespace Winix.Less.Tests;

/// <summary>
/// Tests for round-1 fresh-eyes 2026-05-09 test-analyzer coverage gaps:
/// I1 (F2 StripAnsi rendered-path pin), I3 (LineWrapper ANSI-survives-wrap), I4
/// (PollForNewContent edge cases), W1 (AnsiText overflow), and the cleanup-triangle
/// closures from commit 2 (Pager.DumpFromViewport contract).
/// </summary>
public sealed class Round1CoverageTests : IDisposable
{
    private readonly string _tempDir;

    public Round1CoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "less-r1cov-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort.
        }
    }

    private static LessOptions OptionsWithStripAnsi(bool stripAnsi)
    {
        return LessOptions.Resolve(Array.Empty<string>(), lessEnvVar: null, stripAnsi: stripAnsi);
    }

    // ── F2: DumpAllLines / DumpFromViewport StripAnsi behaviour ─────────────────

    [Fact]
    public void DumpAllLines_StripAnsiTrue_RemovesEscapes()
    {
        // Round-1 fresh-eyes 2026-05-09 test-analyzer I1: F2 fix wired StripAnsi
        // through LessOptions, but the actual DumpAllLines path was never tested. A
        // regression that drops the `if (_options.StripAnsi)` branch would silently
        // leak ANSI to redirected stdout.
        var pager = new Pager(OptionsWithStripAnsi(true));
        var lines = new List<string> { "\x1b[31mred\x1b[0m text", "plain" };

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
        // No ESC characters in output.
        Assert.DoesNotContain("\x1b", output, StringComparison.Ordinal);
        Assert.Contains("red text", output, StringComparison.Ordinal);
        Assert.Contains("plain", output, StringComparison.Ordinal);
    }

    [Fact]
    public void DumpAllLines_StripAnsiFalse_PassesEscapesThrough()
    {
        var pager = new Pager(OptionsWithStripAnsi(false));
        var lines = new List<string> { "\x1b[31mred\x1b[0m text" };

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

        // ESC characters preserved.
        Assert.Contains("\x1b[31m", capture.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DumpAllLines_LastLineHasNoTrailingNewline()
    {
        // Pin existing contract: the final line is written via Write (no newline) so
        // content exactly filling the terminal height does not scroll past the prompt.
        var pager = new Pager(OptionsWithStripAnsi(false));
        var lines = new List<string> { "first", "last" };

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
        // "first" has trailing newline; "last" does not.
        Assert.EndsWith("last", output, StringComparison.Ordinal);
        Assert.DoesNotContain("last\r", output, StringComparison.Ordinal);
        Assert.DoesNotContain("last\n", output, StringComparison.Ordinal);
    }

    // ── DumpFromViewport: cleanup-triangle viewport-preserving fallback ────────

    [Fact]
    public void DumpFromViewport_NonZeroStartIndex_DumpsFromThere()
    {
        // Round-1 fresh-eyes 2026-05-09 CR I3: pre-fix the IOException catch called
        // DumpAllLines (from line 0), re-emitting content the user already scrolled
        // past. Now DumpFromViewport(lines, topLine) preserves the viewport.
        var pager = new Pager(OptionsWithStripAnsi(false));
        var lines = new List<string> { "alpha", "bravo", "charlie", "delta" };

        var capture = new StringWriter();
        var savedOut = Console.Out;
        Console.SetOut(capture);
        try
        {
            pager.DumpFromViewport(lines, startIndex: 2);
        }
        finally
        {
            Console.SetOut(savedOut);
        }

        string output = capture.ToString();
        // Earlier lines are NOT re-emitted.
        Assert.DoesNotContain("alpha", output, StringComparison.Ordinal);
        Assert.DoesNotContain("bravo", output, StringComparison.Ordinal);
        // From-viewport content IS emitted.
        Assert.Contains("charlie", output, StringComparison.Ordinal);
        Assert.Contains("delta", output, StringComparison.Ordinal);
    }

    [Fact]
    public void DumpFromViewport_StartIndexBeyondLength_EmitsNothing()
    {
        var pager = new Pager(OptionsWithStripAnsi(false));
        var lines = new List<string> { "alpha", "bravo" };

        var capture = new StringWriter();
        var savedOut = Console.Out;
        Console.SetOut(capture);
        try
        {
            pager.DumpFromViewport(lines, startIndex: 100);
        }
        finally
        {
            Console.SetOut(savedOut);
        }

        Assert.Equal(string.Empty, capture.ToString());
    }

    [Fact]
    public void DumpFromViewport_NegativeStartIndex_TreatedAsZero()
    {
        var pager = new Pager(OptionsWithStripAnsi(false));
        var lines = new List<string> { "alpha", "bravo" };

        var capture = new StringWriter();
        var savedOut = Console.Out;
        Console.SetOut(capture);
        try
        {
            pager.DumpFromViewport(lines, startIndex: -5);
        }
        finally
        {
            Console.SetOut(savedOut);
        }

        Assert.Contains("alpha", capture.ToString(), StringComparison.Ordinal);
    }

    // ── I3: LineWrapper ANSI-survives-wrap-boundary ─────────────────────────────

    [Fact]
    public void WrapLine_AnsiOpensInRow0_OpenEscapePreservedForRow0VisibleContent()
    {
        // Round-1 fresh-eyes 2026-05-09 test-analyzer I3: existing LineWrapperTests
        // assert visible-length but don't verify ANSI escapes are preserved alongside
        // the visible chars they apply to. A regression that dropped row 0's open
        // escape would render the bold styling lost.
        //
        // Contract pinned: the open escape (\x1b[1m) lands in row 0 with AAAAA
        // because the bold state opens before any visible char in that row.
        // Subsequent escapes that would have gone in the "skipped region" of row 1
        // are dropped by AnsiText.SubstringByVisibleOffset (lines 146-156) as a
        // deliberate design choice — see Watch finding W-AnsiResetDropped below.
        string input = "\x1b[1mAAAAA\x1b[0mBBBBB";
        var rows = LineWrapper.WrapLine(input, width: 5);

        Assert.Equal(2, rows.Count);
        // Row 0 contains the bold-open escape + the 5 visible chars.
        Assert.Contains("\x1b[1m", rows[0], StringComparison.Ordinal);
        Assert.Contains("AAAAA", rows[0], StringComparison.Ordinal);
        // Row 1 contains BBBBB.
        Assert.Contains("BBBBB", rows[1], StringComparison.Ordinal);
    }

    [Fact]
    public void WrapLine_AnsiResetInSkippedRegion_DroppedByCurrentDesign_WatchOnly()
    {
        // Round-1 fresh-eyes 2026-05-09 test-analyzer I3 follow-up: AnsiText
        // .SubstringByVisibleOffset deliberately drops "ANSI sequences sitting at the
        // current position [that] belonged to the skipped region" (lines 146-156).
        // For a region that opens bold then closes-and-plain on a wrap boundary, the
        // closing reset is dropped from row 1, which means BBBBB inherits AAAAA's
        // bold state when rendered.
        //
        // This test PINS the current behaviour so a future fix that DOES preserve
        // the reset gets caught by a deliberate test edit (per
        // feedback_test_modification_signals_contract_change.md). Whether to fix the
        // design is a Watch-class follow-up: changing it requires AnsiText to
        // distinguish "escape that continues current state" from "escape that resets
        // state at the boundary" — a non-trivial refactor.
        string input = "\x1b[1mAAAAA\x1b[0mBBBBB";
        var rows = LineWrapper.WrapLine(input, width: 5);

        Assert.Equal(2, rows.Count);
        // Row 1 does NOT carry the reset escape — it was dropped as part of the
        // skipped-region cleanup.
        Assert.DoesNotContain("\x1b[0m", rows[1], StringComparison.Ordinal);
    }

    // ── I4: PollForNewContent edge cases ─────────────────────────────────────────

    [Fact]
    public void PollForNewContent_AppendAfterTrailingNewline_NoSpuriousBlankLine()
    {
        // I4: existing test happens to write a trailing-newline file but doesn't
        // assert the no-spurious-blank-line contract. Pin: writing "a\n" then
        // appending "b\n" must yield Lines.Count == 2 (not 3).
        string path = Path.Combine(_tempDir, "append.log");
        File.WriteAllText(path, "a\n");

        var source = InputSource.FromFile(path);
        Assert.Single(source.Lines);

        File.AppendAllText(path, "b\n");
        bool grew = source.PollForNewContent();

        Assert.True(grew);
        Assert.Equal(2, source.Lines.Count);
        Assert.Equal("a", source.Lines[0]);
        Assert.Equal("b", source.Lines[1]);
    }

    [Fact]
    public void PollForNewContent_FileTruncated_ReturnsFalse()
    {
        // Truncation under the previous-known length isn't a "grew" signal — the
        // contract should be "return false, don't reload" (file rotated externally).
        string path = Path.Combine(_tempDir, "trunc.log");
        File.WriteAllText(path, new string('x', 100));

        var source = InputSource.FromFile(path);
        File.WriteAllText(path, new string('x', 10));  // truncate

        bool grew = source.PollForNewContent();
        Assert.False(grew);
    }

    [Fact]
    public void PollForNewContent_AppendPartialLineNoTrailingNewline_AddsAsFinalLine()
    {
        // Round-2 fresh-eyes 2026-05-09 test-analyzer I-R2-3: the no-trailing-newline
        // partial-line tail was not pinned. InputSource.cs:167-179 explicitly handles
        // this branch (final token is "partial line"). A regression that silently
        // dropped or duplicated the partial-line tail would corrupt tail -f output for
        // the common case of a writer mid-line.
        //
        // Pinning current contract: appending "partial" (no newline) to a file with
        // existing line "a\n" yields Lines = ["a", "partial"] (count == 2). Whether
        // a subsequent newline-terminated append should merge into that partial is a
        // separate Watch follow-up; this test guards the first half of the contract.
        string path = Path.Combine(_tempDir, "partial.log");
        File.WriteAllText(path, "a\n");

        var source = InputSource.FromFile(path);
        Assert.Single(source.Lines);

        File.AppendAllText(path, "partial");  // no trailing newline
        bool grew = source.PollForNewContent();

        Assert.True(grew);
        Assert.Equal(2, source.Lines.Count);
        Assert.Equal("a", source.Lines[0]);
        Assert.Equal("partial", source.Lines[1]);
    }

    [Fact]
    public void PollForNewContent_AppendMultipleLines_AllAdded()
    {
        string path = Path.Combine(_tempDir, "multi.log");
        File.WriteAllText(path, "a\n");

        var source = InputSource.FromFile(path);
        File.AppendAllText(path, "b\nc\nd\n");
        bool grew = source.PollForNewContent();

        Assert.True(grew);
        Assert.Equal(4, source.Lines.Count);
        Assert.Equal("a", source.Lines[0]);
        Assert.Equal("b", source.Lines[1]);
        Assert.Equal("c", source.Lines[2]);
        Assert.Equal("d", source.Lines[3]);
    }

    // ── W1: AnsiText overflow ────────────────────────────────────────────────────

    [Fact]
    public void SubstringByVisibleOffset_OffsetBeyondLength_ReturnsEmpty()
    {
        // Round-1 fresh-eyes 2026-05-09 test-analyzer W1: overflow case wasn't pinned.
        string result = AnsiText.SubstringByVisibleOffset("hello", visibleOffset: 100);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void TruncateToWidth_WidthZero_ReturnsEmpty()
    {
        // Defensive: passing maxWidth=0 should yield empty rather than throw or wrap-to-1.
        string result = AnsiText.TruncateToWidth("hello", maxWidth: 0);
        Assert.Equal(string.Empty, result);
    }
}

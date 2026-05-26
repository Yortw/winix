#nullable enable
using System;
using Winix.EnvVault;
using Xunit;

namespace Winix.EnvVault.Tests;

/// <summary>
/// Tests for the extracted echo-off-line-editor state machine. Previously this logic lived inline
/// in <c>DefaultConsolePrompt.ReadLineEchoOff</c> with zero unit coverage — a regression in
/// Ctrl+C detection, backspace underflow, or control-char filtering would have passed every test.
/// </summary>
public class KeyAccumulatorTests
{
    private static ConsoleKeyInfo K(char ch, ConsoleKey key = default, ConsoleModifiers mods = 0)
        => new(ch, key == default ? (ConsoleKey)ch : key, false, false, mods.HasFlag(ConsoleModifiers.Control));

    [Fact]
    public void Apply_PlainCharacter_AppendsToBuffer()
    {
        KeyAccumulator acc = new();
        KeyOutcome outcome = acc.Apply(K('a', ConsoleKey.A));
        Assert.Equal(KeyOutcome.Edit, outcome);
        Assert.Equal("a", acc.Current);
    }

    [Fact]
    public void Apply_Sequence_AccumulatesBuffer()
    {
        KeyAccumulator acc = new();
        acc.Apply(K('a', ConsoleKey.A));
        acc.Apply(K('b', ConsoleKey.B));
        acc.Apply(K('c', ConsoleKey.C));
        Assert.Equal("abc", acc.Current);
    }

    [Fact]
    public void Apply_Enter_ReportsSubmitAndPreservesBuffer()
    {
        KeyAccumulator acc = new();
        acc.Apply(K('x', ConsoleKey.X));
        KeyOutcome outcome = acc.Apply(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        Assert.Equal(KeyOutcome.Submit, outcome);
        Assert.Equal("x", acc.Current);
    }

    [Fact]
    public void Apply_CtrlC_ReportsCancelRegardlessOfBufferContent()
    {
        // Regression: on Linux the tty is in raw mode during Console.ReadKey, so Ctrl+C arrives
        // as a keystroke rather than a signal. Without explicit detection the KeyChar '\\u0003'
        // would be IsControl == true and silently dropped, leaving the user unable to abort.
        KeyAccumulator acc = new();
        acc.Apply(K('h', ConsoleKey.H));
        acc.Apply(K('i', ConsoleKey.I));
        KeyOutcome outcome = acc.Apply(new ConsoleKeyInfo(
            '', ConsoleKey.C, shift: false, alt: false, control: true));
        Assert.Equal(KeyOutcome.Cancel, outcome);
        Assert.Equal("hi", acc.Current);  // buffer not mutated on cancel
    }

    [Fact]
    public void Apply_Backspace_RemovesLastCharacter()
    {
        KeyAccumulator acc = new();
        acc.Apply(K('a', ConsoleKey.A));
        acc.Apply(K('b', ConsoleKey.B));
        KeyOutcome outcome = acc.Apply(new ConsoleKeyInfo(
            '\b', ConsoleKey.Backspace, false, false, false));
        Assert.Equal(KeyOutcome.Edit, outcome);
        Assert.Equal("a", acc.Current);
    }

    [Fact]
    public void Apply_BackspaceOnEmptyBuffer_IsNoOp()
    {
        // Regression: buffer.Length-- on an empty StringBuilder would be an underflow. The guard
        // makes the backspace a no-op rather than a silent corruption.
        KeyAccumulator acc = new();
        KeyOutcome outcome = acc.Apply(new ConsoleKeyInfo(
            '\b', ConsoleKey.Backspace, false, false, false));
        Assert.Equal(KeyOutcome.Ignore, outcome);
        Assert.Equal("", acc.Current);
    }

    [Fact]
    public void Apply_ControlCharacterThatIsntCtrlCOrBackspace_IsDropped()
    {
        // Ctrl+V pastes into raw-mode tty as  (SYN). Ctrl+Z as  (SUB). Ctrl+\ as
        //  (FS). None of these are the special Ctrl+C/Enter/Backspace cases and none
        // should corrupt the passphrase buffer. This IS a known UX gap documented in the
        // fresh-eyes silent-failure audit (Ctrl+V paste is silently discarded), but the
        // correctness guarantee is that they don't end up as literal bytes in the buffer.
        KeyAccumulator acc = new();
        acc.Apply(K('s', ConsoleKey.S));
        acc.Apply(new ConsoleKeyInfo('', ConsoleKey.V, false, false, true));  // Ctrl+V
        acc.Apply(new ConsoleKeyInfo('', ConsoleKey.Z, false, false, true));  // Ctrl+Z
        acc.Apply(K('e', ConsoleKey.E));
        Assert.Equal("se", acc.Current);  // control chars filtered out
    }

    [Fact]
    public void Apply_TabKey_IsDroppedAsControlCharacter()
    {
        // Tab's KeyChar is '\t' which is char.IsControl == true. Documented tty-raw-mode limitation.
        KeyAccumulator acc = new();
        acc.Apply(new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false));
        Assert.Equal("", acc.Current);
    }

    [Fact]
    public void Apply_SpecialCharactersInPassphrase_AllAccepted()
    {
        // A realistic passphrase with punctuation/symbols should round-trip unchanged.
        KeyAccumulator acc = new();
        string phrase = "p@ssw0rd!#$%^&*()-_=+[]{};':\",.<>/?`~";
        foreach (char c in phrase)
        {
            acc.Apply(new ConsoleKeyInfo(c, (ConsoleKey)char.ToUpperInvariant(c), false, false, false));
        }
        Assert.Equal(phrase, acc.Current);
    }

    [Fact]
    public void Apply_UnicodeCharacters_AllAccepted()
    {
        // Non-ASCII passphrases ("café-日本-🔐") must round-trip. This is a correctness
        // guarantee — silently dropping non-ASCII would truncate secrets.
        // Note: for BMP-plus chars (🔐 = U+1F510, outside BMP), the tty produces surrogate pairs
        // one KeyChar at a time; the accumulator appends each surrogate and they combine.
        KeyAccumulator acc = new();
        string phrase = "café-日本";
        foreach (char c in phrase)
        {
            acc.Apply(new ConsoleKeyInfo(c, ConsoleKey.A /* irrelevant */, false, false, false));
        }
        Assert.Equal(phrase, acc.Current);
    }

    [Fact]
    public void Apply_CancelAfterPartialInput_BufferIsPreserved()
    {
        // The outcome is Cancel but the buffer retains what was typed. Production code uses this
        // to decide whether to emit a "stored [K1]; remaining not written" line before throwing.
        KeyAccumulator acc = new();
        acc.Apply(K('p', ConsoleKey.P));
        acc.Apply(K('w', ConsoleKey.W));
        KeyOutcome outcome = acc.Apply(new ConsoleKeyInfo(
            '', ConsoleKey.C, false, false, true));
        Assert.Equal(KeyOutcome.Cancel, outcome);
        Assert.Equal("pw", acc.Current);
    }
}

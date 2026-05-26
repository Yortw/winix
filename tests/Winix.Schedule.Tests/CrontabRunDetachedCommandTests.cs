#nullable enable

using Winix.Schedule;
using Xunit;

namespace Winix.Schedule.Tests;

/// <summary>
/// R3 regression pins for the BuildRunDetachedCommand brace-wrap helper. Pins the
/// terminator-skip rules: cmd ending in '&amp;' (bare, backgrounded) or ';' must NOT have
/// '; ' appended, or bash/dash reject with 'syntax error near unexpected token'.
/// Caught by R3 silent-failure-hunter (F7).
/// </summary>
public sealed class CrontabRunDetachedCommandTests
{
    [Fact]
    public void Naked_AppendsSemicolonTerminator()
    {
        // Plain command with no terminator: needs our '; ' before the closing '}'.
        Assert.Equal(
            "{ /usr/bin/run ; } </dev/null >/dev/null 2>&1 &",
            CrontabBackend.BuildRunDetachedCommand("/usr/bin/run"));
    }

    [Fact]
    public void EndingInBackgroundAmpersand_SkipsTerminator()
    {
        // 'cmd &' (already backgrounded by user) — '&' is itself a terminator. Adding our ';'
        // would produce '{ cmd & ; }' which bash/dash reject as a parse error.
        Assert.Equal(
            "{ /usr/bin/long-job & } </dev/null >/dev/null 2>&1 &",
            CrontabBackend.BuildRunDetachedCommand("/usr/bin/long-job &"));
    }

    [Fact]
    public void EndingInLogicalAnd_AppendsTerminator()
    {
        // 'cmd1 && cmd2' — '&&' is logical-AND, NOT a terminator. We still need to add our ';'
        // because '{ cmd1 && cmd2 }' without a terminator is a parse error.
        // (cmd2 here is the trailing literal — verify the detection treats this as 'needs terminator'.)
        Assert.Equal(
            "{ a && b ; } </dev/null >/dev/null 2>&1 &",
            CrontabBackend.BuildRunDetachedCommand("a && b"));
    }

    [Fact]
    public void EndingInSemicolon_SkipsTerminator()
    {
        // 'cmd ;' — user already terminated; adding our ';' is redundant though not erroneous.
        // We skip purely for cleanliness (and to keep the wrap shape predictable for tests).
        Assert.Equal(
            "{ cmd ; } </dev/null >/dev/null 2>&1 &",
            CrontabBackend.BuildRunDetachedCommand("cmd ;"));
    }

    [Fact]
    public void Pipeline_AppendsTerminator()
    {
        // 'cmd1 | cmd2' — needs terminator AND the brace-wrap is what makes the redirects
        // bind to the whole compound rather than just cmd2.
        Assert.Equal(
            "{ ls -la | grep foo ; } </dev/null >/dev/null 2>&1 &",
            CrontabBackend.BuildRunDetachedCommand("ls -la | grep foo"));
    }

    [Fact]
    public void TrailingWhitespace_TreatedSameAsNoWhitespace()
    {
        // 'cmd & ' (with trailing whitespace) should still detect the bare '&' and skip.
        // TrimEnd is applied to the inspected form so editor-introduced trailing whitespace
        // doesn't change the wrap decision.
        Assert.Equal(
            "{ /usr/bin/job &   } </dev/null >/dev/null 2>&1 &",
            CrontabBackend.BuildRunDetachedCommand("/usr/bin/job &  "));
    }

    [Fact]
    public void EmptyCommand_StillProducesValidShape()
    {
        // Edge case: empty command. The wrap is still well-formed shell ('{  ; }' is a
        // group containing only an empty statement) — pointless but doesn't crash bash.
        // Real Add-time validation should reject empty commands; this just pins the
        // helper's edge behaviour so a future refactor doesn't accidentally produce
        // unbalanced braces.
        string result = CrontabBackend.BuildRunDetachedCommand("");
        Assert.StartsWith("{ ", result);
        Assert.EndsWith(" </dev/null >/dev/null 2>&1 &", result);
    }
}

using System.Linq;
using Yort.ShellKit;
using Xunit;

namespace Yort.ShellKit.Tests;

public class RawCommandLineTokenizerTests
{
    private static (string Text, bool WasQuoted)[] Tok(string raw)
        => RawCommandLineTokenizer.Tokenize(raw).Select(t => (t.Text, t.WasQuoted)).ToArray();

    [Fact]
    public void Argv0_Unquoted_EndsAtWhitespace()
    {
        var t = Tok(@"C:\bin\tool.exe a b");
        Assert.Equal((@"C:\bin\tool.exe", false), t[0]);
        Assert.Equal(3, t.Length);
    }

    [Fact]
    public void Argv0_Quoted_NoEscapeProcessing_EndsAtQuote()
    {
        // argv[0] rule is simpler: backslashes are literal, token runs to the closing quote.
        var t = Tok("\"C:\\Program Files\\tool.exe\" x");
        Assert.Equal(("C:\\Program Files\\tool.exe", true), t[0]);
        Assert.Equal(("x", false), t[1]);
    }

    [Fact]
    public void PlainArgs_SplitOnSpacesAndTabs()
    {
        var t = Tok("t.exe a\tb  c");
        Assert.Equal(new[] { ("t.exe", false), ("a", false), ("b", false), ("c", false) }, t);
    }

    [Fact]
    public void QuotedArg_PreservesSpaces_FlagsQuoted()
    {
        var t = Tok("t.exe \"a b\" c");
        Assert.Equal(("a b", true), t[1]);
        Assert.Equal(("c", false), t[2]);
    }

    [Fact]
    public void PartiallyQuotedArg_IsQuoted()
    {
        // foo"*"bar — any quoted region marks the whole token quoted (suppression-safe).
        var t = Tok("t.exe foo\"*\"bar");
        Assert.Equal(("foo*bar", true), t[1]);
    }

    [Fact]
    public void BackslashesNotBeforeQuote_AreLiteral()
    {
        var t = Tok(@"t.exe a\\\b dir\");
        Assert.Equal((@"a\\\b", false), t[1]);
        Assert.Equal((@"dir\", false), t[2]);
    }

    [Fact]
    public void OddBackslashesBeforeQuote_EscapeTheQuote()
    {
        // 2n+1 backslashes + " → n backslashes + literal quote.  a\"b stays one token.
        var t = Tok("t.exe a\\\"b");
        Assert.Equal(("a\"b", false), t[1]);
    }

    [Fact]
    public void EvenBackslashesBeforeQuote_QuoteToggles()
    {
        // 2n backslashes + " → n backslashes, quote is a delimiter:  a\\"b c" → a\b c
        var t = Tok("t.exe a\\\\\"b c\"");
        Assert.Equal(("a\\b c", true), t[1]);
    }

    [Fact]
    public void EmptyQuotedArg_YieldsEmptyToken()
    {
        var t = Tok("t.exe \"\" x");
        Assert.Equal(("", true), t[1]);
        Assert.Equal(("x", false), t[2]);
    }

    [Fact]
    public void DoubledQuoteInsideQuotes_EmitsLiteralQuote()
    {
        // Post-2008 CRT rule — VERIFIED against the runtime by the Task 3 oracle test.
        var t = Tok("t.exe \"a\"\"b\"");
        Assert.Equal(("a\"b", true), t[1]);
    }

    [Fact]
    public void UnterminatedQuote_RunsToEnd()
    {
        var t = Tok("t.exe \"open ended");
        Assert.Equal(("open ended", true), t[1]);
        Assert.Equal(2, t.Length);
    }

    [Fact]
    public void TrailingWhitespace_NoEmptyToken()
    {
        var t = Tok("t.exe a  ");
        Assert.Equal(2, t.Length);
    }

    [Fact]
    public void GlobUseCase_QuotedVsUnquoted()
    {
        var t = Tok("digest.exe *.txt \"*.txt\"");
        Assert.Equal(("*.txt", false), t[1]);
        Assert.Equal(("*.txt", true), t[2]);
    }

    [Fact]
    public void EmptyRawLine_ReturnsEmpty()
    {
        Assert.Empty(RawCommandLineTokenizer.Tokenize(""));
    }

    [Fact]
    public void NonAsciiAndSurrogatePairTokens_RoundTrip()
    {
        // Review F5: non-BMP filenames (surrogate pairs) and accented chars must survive
        // tokenization unmangled. Strings built from code points, not source literals, so
        // the test is immune to source-file encoding round-trips (same rationale as the
        // (char)27 convention for ESC).
        string emoji = char.ConvertFromUtf32(0x1F600);                       // non-BMP, surrogate pair
        string accented = "h" + (char)0xE9 + "llo w" + (char)0xF6 + "rld";   // héllo wörld
        var t = Tok($"t.exe \"{accented}\" a{emoji}.txt");
        Assert.Equal((accented, true), t[1]);
        Assert.Equal(($"a{emoji}.txt", false), t[2]);
    }
}

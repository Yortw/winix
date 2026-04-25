using Winix.Wargs;
using Xunit;

namespace Winix.Wargs.Tests;

public class InputReaderTests
{
    [Fact]
    public void ReadItems_LineMode_SplitsOnNewline()
    {
        var reader = new InputReader(new StringReader("alpha\nbeta\ngamma"), DelimiterMode.Line);
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, items);
    }

    [Fact]
    public void ReadItems_LineMode_TrimsCarriageReturn()
    {
        var reader = new InputReader(new StringReader("alpha\r\nbeta\r\n"), DelimiterMode.Line);
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "alpha", "beta" }, items);
    }

    [Fact]
    public void ReadItems_LineMode_SkipsEmptyLines()
    {
        var reader = new InputReader(new StringReader("alpha\n\n\nbeta\n"), DelimiterMode.Line);
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "alpha", "beta" }, items);
    }

    [Fact]
    public void ReadItems_LineMode_SkipsWhitespaceOnlyLines()
    {
        var reader = new InputReader(new StringReader("alpha\n   \n\t\nbeta"), DelimiterMode.Line);
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "alpha", "beta" }, items);
    }

    [Fact]
    public void ReadItems_LineMode_EmptyInput_YieldsNothing()
    {
        var reader = new InputReader(new StringReader(""), DelimiterMode.Line);
        var items = reader.ReadItems().ToList();
        Assert.Empty(items);
    }

    [Fact]
    public void ReadItems_NullMode_SplitsOnNullChar()
    {
        var reader = new InputReader(new StringReader("alpha\0beta\0gamma"), DelimiterMode.Null);
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, items);
    }

    [Fact]
    public void ReadItems_NullMode_SkipsEmptyItems()
    {
        var reader = new InputReader(new StringReader("alpha\0\0beta\0"), DelimiterMode.Null);
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "alpha", "beta" }, items);
    }

    [Fact]
    public void ReadItems_NullMode_PreservesNewlinesInItems()
    {
        var reader = new InputReader(new StringReader("line one\nstill one\0two"), DelimiterMode.Null);
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "line one\nstill one", "two" }, items);
    }

    [Fact]
    public void ReadItems_CustomDelimiter_SplitsOnChar()
    {
        var reader = new InputReader(new StringReader("alpha,beta,gamma"), DelimiterMode.Custom, ',');
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, items);
    }

    [Fact]
    public void ReadItems_CustomDelimiter_SkipsEmptyItems()
    {
        var reader = new InputReader(new StringReader("alpha,,beta,"), DelimiterMode.Custom, ',');
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "alpha", "beta" }, items);
    }

    [Fact]
    public void ReadItems_Whitespace_SplitsOnSpacesAndTabs()
    {
        var reader = new InputReader(new StringReader("alpha beta\tgamma"), DelimiterMode.Whitespace);
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, items);
    }

    [Fact]
    public void ReadItems_Whitespace_SplitsAcrossNewlines()
    {
        var reader = new InputReader(new StringReader("alpha\nbeta gamma"), DelimiterMode.Whitespace);
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, items);
    }

    [Fact]
    public void ReadItems_Whitespace_SingleQuotesPreserveLiteral()
    {
        var reader = new InputReader(new StringReader("'hello world' beta"), DelimiterMode.Whitespace);
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "hello world", "beta" }, items);
    }

    [Fact]
    public void ReadItems_Whitespace_DoubleQuotesPreserveSpaces()
    {
        var reader = new InputReader(new StringReader("\"hello world\" beta"), DelimiterMode.Whitespace);
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "hello world", "beta" }, items);
    }

    [Fact]
    public void ReadItems_Whitespace_BackslashEscapesSpace()
    {
        var reader = new InputReader(new StringReader("hello\\ world beta"), DelimiterMode.Whitespace);
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "hello world", "beta" }, items);
    }

    [Fact]
    public void ReadItems_Whitespace_DoubleQuotesAllowBackslashEscapes()
    {
        var reader = new InputReader(new StringReader("\"hello \\\"world\\\"\" beta"), DelimiterMode.Whitespace);
        var items = reader.ReadItems().ToList();
        Assert.Equal(new[] { "hello \"world\"", "beta" }, items);
    }

    [Fact]
    public void ReadItems_Whitespace_EmptyInput_YieldsNothing()
    {
        var reader = new InputReader(new StringReader("   \n\t  "), DelimiterMode.Whitespace);
        var items = reader.ReadItems().ToList();
        Assert.Empty(items);
    }

    // -- Round-7 review: pin the cancellation-observability contract added to InputReader.
    //    The materialisation path in Program.RunAsync depends on this contract — without it,
    //    a Ctrl+C-driven Console.In.Close() that produces EOF would silently land in the
    //    empty-input branch as no_input/exit-0 instead of cancelled/exit-130. This is the
    //    deterministic library-level pin the round-7 SFH/test-analyzer agents flagged as
    //    needed alongside the Linux-only subprocess SIGINT test. --

    [Fact]
    public void ReadItems_LineMode_TokenAlreadyCancelled_ThrowsOperationCanceledException()
    {
        var reader = new InputReader(new StringReader("alpha\nbeta\ngamma"), DelimiterMode.Line);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => reader.ReadItems(cts.Token).ToList());
    }

    [Fact]
    public void ReadItems_NullMode_TokenAlreadyCancelled_ThrowsOperationCanceledException()
    {
        var reader = new InputReader(new StringReader("a\0b\0c"), DelimiterMode.Null);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => reader.ReadItems(cts.Token).ToList());
    }

    [Fact]
    public void ReadItems_WhitespaceMode_TokenAlreadyCancelled_ThrowsOperationCanceledException()
    {
        var reader = new InputReader(new StringReader("a b c"), DelimiterMode.Whitespace);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => reader.ReadItems(cts.Token).ToList());
    }

    [Fact]
    public void ReadItems_LineMode_TokenCancelledAfterFirstRead_ThrowsOperationCanceledException()
    {
        // Pin the between-reads observability — the loop must check the token between each
        // ReadLine, not just at the start. This catches a future change that hoists the
        // token check out of the loop body.
        var reader = new InputReader(new StringReader("alpha\nbeta\ngamma"), DelimiterMode.Line);
        using var cts = new CancellationTokenSource();

        var items = new List<string>();
        Assert.Throws<OperationCanceledException>(() =>
        {
            foreach (string item in reader.ReadItems(cts.Token))
            {
                items.Add(item);
                cts.Cancel();
            }
        });
        Assert.Single(items); // first item yielded before cancel observed
    }

    [Fact]
    public void ReadItems_DefaultToken_DoesNotThrowOnEmptyInput()
    {
        // Pin the default-CancellationToken path — backwards compatibility for callers
        // that don't pass a token. This is the codepath everything pre-round-7 used.
        var reader = new InputReader(new StringReader(""), DelimiterMode.Line);
        var items = reader.ReadItems().ToList();
        Assert.Empty(items);
    }
}

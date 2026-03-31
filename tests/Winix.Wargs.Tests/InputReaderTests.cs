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
}

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
}

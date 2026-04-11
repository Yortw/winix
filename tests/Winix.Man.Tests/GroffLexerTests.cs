#nullable enable

using Winix.Man;
using Xunit;

namespace Winix.Man.Tests;

public sealed class GroffLexerTests
{
    private readonly GroffLexer _lexer = new();

    [Fact]
    public void Tokenise_RequestLine_ProducesRequestToken()
    {
        var tokens = _lexer.Tokenise(".SH NAME").ToList();

        Assert.Single(tokens);
        var token = Assert.IsType<RequestToken>(tokens[0]);
        Assert.Equal("SH", token.MacroName);
        Assert.Equal("NAME", token.Arguments);
    }

    [Fact]
    public void Tokenise_RequestWithNoArgs_ProducesEmptyArguments()
    {
        var tokens = _lexer.Tokenise(".PP").ToList();

        Assert.Single(tokens);
        var token = Assert.IsType<RequestToken>(tokens[0]);
        Assert.Equal("PP", token.MacroName);
        Assert.Equal("", token.Arguments);
    }

    [Fact]
    public void Tokenise_TextLine_ProducesTextLineToken()
    {
        var tokens = _lexer.Tokenise("This is body text.").ToList();

        Assert.Single(tokens);
        var token = Assert.IsType<TextLineToken>(tokens[0]);
        Assert.Equal("This is body text.", token.Text);
    }

    [Fact]
    public void Tokenise_Comment_ProducesCommentToken()
    {
        var tokens = _lexer.Tokenise(".\\\" This is a comment").ToList();

        Assert.Single(tokens);
        Assert.IsType<CommentToken>(tokens[0]);
    }

    [Fact]
    public void Tokenise_MultipleLines_ProducesMultipleTokens()
    {
        var source = ".SH NAME\n.PP\nThis is body text.";

        var tokens = _lexer.Tokenise(source).ToList();

        Assert.Equal(3, tokens.Count);
        Assert.IsType<RequestToken>(tokens[0]);
        Assert.IsType<RequestToken>(tokens[1]);
        Assert.IsType<TextLineToken>(tokens[2]);
    }

    [Fact]
    public void Tokenise_EmptyLines_ProducesEmptyTextLineTokens()
    {
        var source = "text\n\nmore text";

        var tokens = _lexer.Tokenise(source).ToList();

        Assert.Equal(3, tokens.Count);
        var middle = Assert.IsType<TextLineToken>(tokens[1]);
        Assert.Equal("", middle.Text);
    }

    [Fact]
    public void Tokenise_ApostropheRequest_ProducesRequestToken()
    {
        var tokens = _lexer.Tokenise("'br").ToList();

        Assert.Single(tokens);
        var token = Assert.IsType<RequestToken>(tokens[0]);
        Assert.Equal("br", token.MacroName);
        Assert.Equal("", token.Arguments);
    }

    [Fact]
    public void Tokenise_RequestWithQuotedArguments_PreservesQuotedString()
    {
        var tokens = _lexer.Tokenise(".TH TIMEIT 1 \"March 2026\" Winix").ToList();

        Assert.Single(tokens);
        var token = Assert.IsType<RequestToken>(tokens[0]);
        Assert.Equal("TH", token.MacroName);
        Assert.Equal("TIMEIT 1 \"March 2026\" Winix", token.Arguments);
    }
}

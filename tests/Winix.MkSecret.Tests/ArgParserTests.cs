using Winix.MkSecret;
using Xunit;

namespace Winix.MkSecret.Tests;

public class ArgParserTests
{
    [Fact]
    public void Bare_invocation_defaults_to_password_mode()
    {
        var r = ArgParser.Parse(new string[] { });
        Assert.True(r.Success);
        Assert.Equal(SecretMode.Password, r.Options!.Mode);
        Assert.Equal(20, r.Options.Length);
        Assert.Equal(Charset.Alphanumeric, r.Options.Charset);
    }

    [Fact]
    public void Phrase_subcommand_selects_phrase_mode_with_defaults()
    {
        var r = ArgParser.Parse(new[] { "phrase" });
        Assert.True(r.Success);
        Assert.Equal(SecretMode.Phrase, r.Options!.Mode);
        Assert.Equal(6, r.Options.Words);
        Assert.Equal("-", r.Options.Separator);
    }

    [Fact]
    public void Key_subcommand_defaults_to_32_bytes_base64url()
    {
        var r = ArgParser.Parse(new[] { "key" });
        Assert.True(r.Success);
        Assert.Equal(SecretMode.Key, r.Options!.Mode);
        Assert.Equal(32, r.Options.Bytes);
        Assert.Equal(KeyEncoding.Base64Url, r.Options.Encoding);
    }

    [Fact]
    public void Password_length_and_charset_flags_parse()
    {
        var r = ArgParser.Parse(new[] { "password", "--length", "12", "--charset", "full" });
        Assert.True(r.Success);
        Assert.Equal(12, r.Options!.Length);
        Assert.Equal(Charset.Full, r.Options.Charset);
    }

    [Fact]
    public void Unknown_charset_is_a_usage_error()
    {
        var r = ArgParser.Parse(new[] { "password", "--charset", "klingon" });
        Assert.False(r.Success);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void Unknown_encoding_is_a_usage_error()
    {
        var r = ArgParser.Parse(new[] { "key", "--encoding", "rot13" });
        Assert.False(r.Success);
    }

    [Theory]
    [InlineData("password", "--length", "0")]
    [InlineData("key", "--bytes", "0")]
    [InlineData("phrase", "--words", "0")]
    [InlineData("password", "--count", "0")]
    public void Non_positive_sizes_are_usage_errors(params string[] args)
    {
        var r = ArgParser.Parse(args);
        Assert.False(r.Success);
    }

    [Theory]
    [InlineData("password", "--length", "99999999")]
    [InlineData("key", "--bytes", "99999999")]
    [InlineData("phrase", "--words", "99999999")]
    [InlineData("password", "--count", "99999999")]
    public void Oversized_sizes_are_usage_errors(params string[] args)
    {
        var r = ArgParser.Parse(args);
        Assert.False(r.Success);
    }

    [Theory]
    [InlineData("\n")]       // separator IS a newline
    [InlineData("\r")]       // separator IS a carriage return
    [InlineData("a\nb")]     // newline in the MIDDLE — distinguishes Contains('\n') from == "\n"
    public void Separator_containing_a_newline_is_a_usage_error(string sep)
    {
        // A newline in --sep would split one passphrase across multiple output lines, silently
        // corrupting line-per-secret consumers. Reject at parse time. The mid-string case is the
        // discriminator: a naive equality check (sep == "\n") would let "a\nb" through.
        var r = ArgParser.Parse(new[] { "phrase", "--sep", sep });
        Assert.False(r.Success);
        Assert.NotNull(r.Error);
        // Pin the error CONTENT so the user is told which flag to fix.
        Assert.Contains("--sep", r.Error!, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-")]        // the default
    [InlineData(" ")]
    [InlineData("::")]
    [InlineData("")]         // empty separator is legitimate (concatenated words)
    public void Separator_without_newlines_parses(string sep)
    {
        // Invariant: the newline guard must not reject ordinary separators.
        var r = ArgParser.Parse(new[] { "phrase", "--sep", sep });
        Assert.True(r.Success);
        Assert.Equal(sep, r.Options!.Separator);
    }
}

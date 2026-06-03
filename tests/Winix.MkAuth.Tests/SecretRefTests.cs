#nullable enable
using Winix.MkAuth;
using Xunit;

public class SecretRefTests
{
    [Theory]
    [InlineData("env:TOK", SecretRefKind.Env, "TOK")]
    [InlineData("file:/etc/secret", SecretRefKind.File, "/etc/secret")]
    [InlineData("vault:api/consumer", SecretRefKind.Vault, "api/consumer")]
    [InlineData("literal:hunter2", SecretRefKind.Literal, "hunter2")]
    [InlineData("stdin", SecretRefKind.Stdin, "")]
    [InlineData("-", SecretRefKind.Stdin, "")]
    public void Parse_recognises_each_scheme(string input, SecretRefKind kind, string value)
    {
        var r = SecretRef.Parse(input);
        Assert.Equal(kind, r.Kind);
        Assert.Equal(value, r.Value);
    }

    [Fact]
    public void Parse_unknown_scheme_throws_MkAuthException()
    {
        // MkAuthException carries readable English; framework FormatException would leak SR keys under
        // UseSystemResourceKeys when surfaced by Cli.
        Assert.Throws<MkAuthException>(() => SecretRef.Parse("bogus:x"));
    }

    [Fact]
    public void Parse_bare_value_without_scheme_throws()
    {
        // No implicit literal — a bare value is ambiguous and rejected.
        Assert.Throws<MkAuthException>(() => SecretRef.Parse("justavalue"));
    }
}

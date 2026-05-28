using Winix.MkSecret;
using Xunit;

namespace Winix.MkSecret.Tests;

public class PasswordGeneratorTests
{
    private static MkSecretOptions Opts(int length, Charset cs) =>
        MkSecretOptions.Defaults with { Mode = SecretMode.Password, Length = length, Charset = cs };

    [Fact]
    public void Generate_maps_bytes_to_charset_indices()
    {
        // alphanumeric = "ABC...Zabc...z0..9". Indices 0,1,5 -> 'A','B','F'.
        var rng = new SequenceRandom(0, 1, 5);
        var gen = new PasswordGenerator(rng);
        Assert.Equal("ABF", gen.Generate(Opts(3, Charset.Alphanumeric)));
    }

    [Fact]
    public void Generate_only_emits_charset_members()
    {
        var rng = new SequenceRandom(new byte[64]); // all zeros -> index 0 repeatedly
        var gen = new PasswordGenerator(rng);
        string pw = gen.Generate(Opts(20, Charset.Digits));
        Assert.Equal(20, pw.Length);
        Assert.All(pw, c => Assert.Contains(c, Charsets.ToChars(Charset.Digits)));
    }
}

using Winix.MkSecret;
using Xunit;

namespace Winix.MkSecret.Tests;

public class CharsetTests
{
    [Theory]
    [InlineData(Charset.Alphanumeric, 62)]
    [InlineData(Charset.Full, 94)]
    [InlineData(Charset.Alpha, 52)]
    [InlineData(Charset.Digits, 10)]
    [InlineData(Charset.Safe, 56)]
    public void ToChars_has_expected_size(Charset cs, int expected)
    {
        Assert.Equal(expected, Charsets.ToChars(cs).Length);
    }

    [Fact]
    public void Safe_excludes_visually_ambiguous_chars()
    {
        string safe = Charsets.ToChars(Charset.Safe);
        foreach (char c in "l1IO0o")
        {
            Assert.DoesNotContain(c, safe);
        }
    }

    [Fact]
    public void Full_is_printable_ascii_33_to_126()
    {
        string full = Charsets.ToChars(Charset.Full);
        for (char c = (char)33; c <= 126; c++)
        {
            Assert.Contains(c, full);
        }
    }

    [Fact]
    public void No_charset_has_duplicate_characters()
    {
        foreach (Charset cs in System.Enum.GetValues<Charset>())
        {
            string chars = Charsets.ToChars(cs);
            Assert.Equal(chars.Length, new System.Collections.Generic.HashSet<char>(chars).Count);
        }
    }
}

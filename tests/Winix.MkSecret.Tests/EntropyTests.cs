using System;
using Winix.MkSecret;
using Xunit;

namespace Winix.MkSecret.Tests;

public class EntropyTests
{
    [Fact]
    public void Password_bits_is_length_times_log2_charset()
    {
        var o = MkSecretOptions.Defaults with { Mode = SecretMode.Password, Length = 20, Charset = Charset.Alphanumeric };
        Assert.Equal(20 * Math.Log2(62), Entropy.BitsFor(o), 3);
    }

    [Fact]
    public void Key_bits_is_bytes_times_eight()
    {
        var o = MkSecretOptions.Defaults with { Mode = SecretMode.Key, Bytes = 32 };
        Assert.Equal(256.0, Entropy.BitsFor(o), 3);
    }

    [Fact]
    public void Phrase_bits_is_words_times_log2_wordcount_plus_digit()
    {
        var o = MkSecretOptions.Defaults with { Mode = SecretMode.Phrase, Words = 6, Number = false };
        Assert.Equal(6 * Math.Log2(7776), Entropy.BitsFor(o), 3);
        var withNum = o with { Number = true };
        Assert.Equal(6 * Math.Log2(7776) + Math.Log2(10), Entropy.BitsFor(withNum), 3);
    }
}

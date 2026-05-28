using Winix.MkSecret;
using Xunit;

namespace Winix.MkSecret.Tests;

public class PhraseGeneratorTests
{
    private static MkSecretOptions Opts(int words, string sep, bool cap = false, bool num = false) =>
        MkSecretOptions.Defaults with
        { Mode = SecretMode.Phrase, Words = words, Separator = sep, Capitalize = cap, Number = num };

    [Fact]
    public void Generate_selects_words_by_index_and_joins_with_separator()
    {
        // count=7776 -> 2 bytes each. {0,0}->index 0, {0,5}->index 5.
        var rng = new SequenceRandom(0, 0, 0, 5);
        var gen = new PhraseGenerator(rng);
        string expected = EffWordList.Words[0] + "-" + EffWordList.Words[5];
        Assert.Equal(expected, gen.Generate(Opts(2, "-")));
    }

    [Fact]
    public void Capitalize_uppercases_each_word_initial()
    {
        var rng = new SequenceRandom(0, 0, 0, 5);
        var gen = new PhraseGenerator(rng);
        string w0 = EffWordList.Words[0], w5 = EffWordList.Words[5];
        string expected = char.ToUpperInvariant(w0[0]) + w0.Substring(1) + " " +
                          char.ToUpperInvariant(w5[0]) + w5.Substring(1);
        Assert.Equal(expected, gen.Generate(Opts(2, " ", cap: true)));
    }

    [Fact]
    public void Number_appends_a_single_digit()
    {
        // two words (4 bytes) then one byte for the digit: 7 -> '7'.
        var rng = new SequenceRandom(0, 0, 0, 5, 7);
        var gen = new PhraseGenerator(rng);
        string result = gen.Generate(Opts(2, "-", num: true));
        Assert.EndsWith("7", result);
        Assert.Equal(EffWordList.Words[0] + "-" + EffWordList.Words[5] + "7", result);
    }

    [Fact]
    public void Empty_separator_concatenates_words_on_one_line()
    {
        var rng = new SequenceRandom(0, 0, 0, 5);
        var gen = new PhraseGenerator(rng);
        string result = gen.Generate(Opts(2, ""));
        Assert.Equal(EffWordList.Words[0] + EffWordList.Words[5], result);
        Assert.DoesNotContain("\n", result);
    }

    [Fact]
    public void Multi_char_separator_joins_words()
    {
        var rng = new SequenceRandom(0, 0, 0, 5);
        var gen = new PhraseGenerator(rng);
        Assert.Equal(EffWordList.Words[0] + "::" + EffWordList.Words[5], gen.Generate(Opts(2, "::")));
    }
}

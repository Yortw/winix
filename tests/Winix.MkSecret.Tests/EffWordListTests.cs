using System.Collections.Generic;
using System.Linq;
using Winix.MkSecret;
using Xunit;

namespace Winix.MkSecret.Tests;

public class EffWordListTests
{
    [Fact]
    public void Has_exactly_7776_words()
        => Assert.Equal(7776, EffWordList.Words.Length);

    [Fact]
    public void Words_are_unique()
        => Assert.Equal(EffWordList.Words.Length, new HashSet<string>(EffWordList.Words).Count);

    [Fact]
    public void Words_are_lowercase_ascii_no_whitespace()
    {
        foreach (string w in EffWordList.Words)
        {
            Assert.False(string.IsNullOrWhiteSpace(w));
            // EFF large wordlist contains a small number of hyphenated words (e.g. "drop-down").
            // Permit lowercase a-z and hyphen; reject anything else (digits, whitespace, upper-case).
            Assert.All(w, c => Assert.True((c >= 'a' && c <= 'z') || c == '-', $"unexpected char in '{w}'"));
        }
    }
}

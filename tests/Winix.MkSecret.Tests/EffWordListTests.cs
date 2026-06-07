using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Winix.MkSecret;
using Xunit;

namespace Winix.MkSecret.Tests;

public class EffWordListTests
{
    [Fact]
    public void Has_exactly_7776_words()
        => Assert.Equal(7776, EffWordList.Words.Length);

    [Fact]
    public void First_and_last_words_are_the_canonical_diceware_anchors()
    {
        // Provenance anchors: the EFF large list begins "abacus" (diceware 11111) and ends "zoom"
        // (66666). Pins the array's ordering at both ends — a shuffled or off-by-one import fails here.
        Assert.Equal("abacus", EffWordList.Words[0]);
        Assert.Equal("zoom", EffWordList.Words[7775]);
    }

    [Fact]
    public void Full_content_matches_the_canonical_published_list()
    {
        // Wire-correctness pin against the actual EFF publication, NOT our embedded copy. The expected
        // SHA-256 was computed from the file fetched from
        // https://www.eff.org/files/2016/07/18/eff_large_wordlist.txt (2026-06-07): each line is
        // "<dice-index>\t<word>"; the dice column was stripped and the 7776 words joined with "\n".
        // Hashing string.Join("\n", Words) the same way must reproduce it — any substituted, reordered,
        // or mistyped word changes the digest. INDEPENDENCE: the expectation comes from the download,
        // never from this array.
        const string expected = "abae49761b88f3f1ba31ef944bea1f61b795a3cd7e1cfb7d276ed45bf77967ba";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", EffWordList.Words)));
        Assert.Equal(expected, System.Convert.ToHexStringLower(hash));
    }

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

using System;
using System.Text;
using Winix.Codec;

namespace Winix.MkSecret;

/// <summary>Generates a diceware passphrase: <c>Words</c> words drawn unbiasedly from the EFF long
/// list, joined by <c>Separator</c>. Optional initial-capitalisation and a trailing random digit.</summary>
public sealed class PhraseGenerator : ISecretGenerator
{
    private const string Digits = "0123456789";
    private readonly ISecureRandom _random;

    /// <summary>Constructs a generator over an injectable CSPRNG.</summary>
    /// <exception cref="ArgumentNullException">If <paramref name="random"/> is null.</exception>
    public PhraseGenerator(ISecureRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    /// <inheritdoc/>
    public string Generate(MkSecretOptions options)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < options.Words; i++)
        {
            if (i > 0) { sb.Append(options.Separator); }
            string word = EffWordList.Words[Sampling.UniformIndex(_random, EffWordList.Words.Length)];
            if (options.Capitalize && word.Length > 0)
            {
                sb.Append(char.ToUpperInvariant(word[0])).Append(word, 1, word.Length - 1);
            }
            else
            {
                sb.Append(word);
            }
        }
        if (options.Number)
        {
            sb.Append(Digits[Sampling.UniformIndex(_random, Digits.Length)]);
        }
        return sb.ToString();
    }
}

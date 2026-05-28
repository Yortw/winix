using System;
using Winix.Codec;

namespace Winix.MkSecret;

/// <summary>Generates a random-character password: each character is an independent unbiased
/// draw from the selected <see cref="Charset"/> (pure random — no forced class composition).</summary>
public sealed class PasswordGenerator : ISecretGenerator
{
    private readonly ISecureRandom _random;

    /// <summary>Constructs a generator over an injectable CSPRNG.</summary>
    /// <exception cref="ArgumentNullException">If <paramref name="random"/> is null.</exception>
    public PasswordGenerator(ISecureRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    /// <inheritdoc/>
    public string Generate(MkSecretOptions options)
    {
        string alphabet = Charsets.ToChars(options.Charset);
        char[] output = new char[options.Length];
        for (int i = 0; i < output.Length; i++)
        {
            output[i] = alphabet[Sampling.UniformIndex(_random, alphabet.Length)];
        }
        return new string(output);
    }
}

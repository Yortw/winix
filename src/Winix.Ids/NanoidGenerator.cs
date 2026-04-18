using System;
using Winix.Codec;

namespace Winix.Ids;

/// <summary>
/// NanoID generator: random indices into a named alphabet, rendered as a string
/// of the requested length. Uses rejection sampling against a power-of-two mask
/// to eliminate modulo bias for non-power-of-two alphabet sizes.
/// </summary>
public sealed class NanoidGenerator : IIdGenerator
{
    private readonly ISecureRandom _random;

    /// <summary>Constructs a new generator with an injectable random source.</summary>
    public NanoidGenerator(ISecureRandom random)
    {
        _random = random;
    }

    /// <inheritdoc />
    public string Generate(IdsOptions options)
    {
        ReadOnlySpan<char> alphabet = options.Alphabet.ToChars();
        int mask = NextPowerOfTwo(alphabet.Length) - 1;

        char[] output = new char[options.Length];
        Span<byte> draw = stackalloc byte[1];

        int written = 0;
        while (written < options.Length)
        {
            _random.Fill(draw);
            int idx = draw[0] & mask;
            if (idx < alphabet.Length)
            {
                output[written++] = alphabet[idx];
            }
            // else reject: byte maps outside the alphabet — draw again to avoid modulo bias
        }

        return new string(output);
    }

    private static int NextPowerOfTwo(int n)
    {
        // Smallest power of two ≥ n. For our named alphabets n ∈ {16, 36, 62, 64}.
        int p = 1;
        while (p < n) p <<= 1;
        return p;
    }
}

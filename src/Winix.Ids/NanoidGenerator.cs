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
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="random"/> is null.</exception>
    public NanoidGenerator(ISecureRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        _random = random;
    }

    /// <summary>
    /// Generates a NanoID of <see cref="IdsOptions.Length"/> characters drawn from
    /// <see cref="IdsOptions.Alphabet"/>. Uses per-byte rejection sampling: one CSPRNG
    /// byte per attempt, masked to the next power of two, retried if the masked value
    /// lands outside the alphabet. Expected iterations are <c>Length × (nextPow2 / alphabetSize)</c>
    /// — at most ~1.78× for the named alphabets (Lower/Upper at 36 chars), so the
    /// loop always terminates in bounded expected time. Thread-safe if the injected
    /// <see cref="ISecureRandom"/> is thread-safe.
    /// </summary>
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

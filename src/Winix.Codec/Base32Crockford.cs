#nullable enable

using System;
using System.Text;

namespace Winix.Codec;

/// <summary>
/// Crockford base32 encode/decode. Alphabet "0123456789ABCDEFGHJKMNPQRSTVWXYZ"
/// (no I, L, O, U to avoid ambiguity). Encoding produces uppercase, no padding.
/// Decoding is case-insensitive and maps Crockford's human-tolerance aliases
/// (I → 1, L → 1, O → 0).
/// </summary>
public static class Base32Crockford
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>
    /// Encodes the input bytes as a Crockford base32 string. Output length is
    /// <c>ceil(input.Length * 8 / 5)</c>.
    /// </summary>
    public static string Encode(ReadOnlySpan<byte> input)
    {
        if (input.IsEmpty)
        {
            return string.Empty;
        }

        int outLen = (input.Length * 8 + 4) / 5;
        var sb = new StringBuilder(outLen);

        int buffer = 0;
        int bitsLeft = 0;
        foreach (byte b in input)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                int index = (buffer >> bitsLeft) & 0x1F;
                sb.Append(Alphabet[index]);
            }
        }

        if (bitsLeft > 0)
        {
            int index = (buffer << (5 - bitsLeft)) & 0x1F;
            sb.Append(Alphabet[index]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Decodes a Crockford base32 string back to bytes. Case-insensitive.
    /// Throws <see cref="FormatException"/> if the input contains a character
    /// that is not in the alphabet or a recognised alias.
    /// </summary>
    public static byte[] Decode(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return Array.Empty<byte>();
        }

        int outLen = input.Length * 5 / 8;
        byte[] output = new byte[outLen];
        int buffer = 0;
        int bitsLeft = 0;
        int outIndex = 0;

        foreach (char c in input)
        {
            int value = LookupValue(c);
            buffer = (buffer << 5) | value;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                output[outIndex++] = (byte)((buffer >> bitsLeft) & 0xFF);
            }
        }

        return output;
    }

    private static int LookupValue(char c)
    {
        // Normalise to upper-case via a cheap arithmetic shift (ASCII range).
        char upper = (c >= 'a' && c <= 'z') ? (char)(c - 32) : c;
        return upper switch
        {
            >= '0' and <= '9' => upper - '0',
            'A' => 10,
            'B' => 11,
            'C' => 12,
            'D' => 13,
            'E' => 14,
            'F' => 15,
            'G' => 16,
            'H' => 17,
            'I' or 'L' => 1,  // Crockford human-tolerance aliases.
            'J' => 18,
            'K' => 19,
            'M' => 20,
            'N' => 21,
            'O' => 0,         // Crockford human-tolerance alias.
            'P' => 22,
            'Q' => 23,
            'R' => 24,
            'S' => 25,
            'T' => 26,
            'V' => 27,
            'W' => 28,
            'X' => 29,
            'Y' => 30,
            'Z' => 31,
            _ => throw new FormatException($"invalid Crockford base32 character: '{c}'"),
        };
    }
}

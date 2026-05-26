#nullable enable

using System;

namespace Winix.Codec;

/// <summary>
/// Hex encoding/decoding for cryptographic output. Lowercase by default;
/// <c>upper: true</c> for uppercase. Decode is case-insensitive.
/// </summary>
public static class Hex
{
    private const string LowerAlphabet = "0123456789abcdef";
    private const string UpperAlphabet = "0123456789ABCDEF";

    /// <summary>Encodes bytes as a hex string. Returns empty string for empty input.</summary>
    public static string Encode(ReadOnlySpan<byte> input, bool upper = false)
    {
        if (input.IsEmpty) return string.Empty;
        string alphabet = upper ? UpperAlphabet : LowerAlphabet;
        // Stack-allocate for small inputs to avoid heap pressure on typical digest sizes (≤256 bytes → 512 chars).
        Span<char> chars = input.Length <= 128
            ? stackalloc char[input.Length * 2]
            : new char[input.Length * 2];
        for (int i = 0; i < input.Length; i++)
        {
            byte b = input[i];
            chars[i * 2] = alphabet[b >> 4];
            chars[i * 2 + 1] = alphabet[b & 0x0F];
        }
        return new string(chars);
    }

    /// <summary>
    /// Decodes a hex string to bytes. Case-insensitive.
    /// Returns an empty array for null or empty input.
    /// </summary>
    /// <exception cref="FormatException">Thrown if the string has odd length or contains non-hex characters.</exception>
    public static byte[] Decode(string input)
    {
        if (string.IsNullOrEmpty(input)) return Array.Empty<byte>();
        if ((input.Length & 1) != 0)
        {
            throw new FormatException("hex string must have even length");
        }

        byte[] result = new byte[input.Length / 2];
        for (int i = 0; i < result.Length; i++)
        {
            int high = DigitValue(input[i * 2]);
            int low = DigitValue(input[i * 2 + 1]);
            result[i] = (byte)((high << 4) | low);
        }
        return result;
    }

    private static int DigitValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => throw new FormatException($"invalid hex character: '{c}'"),
    };
}

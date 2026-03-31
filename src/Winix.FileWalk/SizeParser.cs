using System.Globalization;

namespace Winix.FileWalk;

/// <summary>
/// Parses human-friendly size strings (e.g. "100k", "10M", "1G") to byte counts.
/// Suffixes are case-insensitive and use binary units (k=1024, M=1024^2, G=1024^3).
/// </summary>
public static class SizeParser
{
    /// <summary>
    /// Parses a size string to a byte count.
    /// </summary>
    /// <param name="value">A non-negative integer optionally followed by a suffix: k, K, m, M, g, or G.</param>
    /// <returns>The equivalent byte count.</returns>
    /// <exception cref="FormatException">Thrown when <paramref name="value"/> is empty, contains non-digit characters (other than a recognised suffix), uses an unrecognised suffix, or is negative.</exception>
    public static long Parse(string value)
    {
        if (!TryParse(value, out long bytes))
        {
            throw new FormatException($"Invalid size: '{value}'. Expected a non-negative integer optionally followed by k, M, or G.");
        }
        return bytes;
    }

    /// <summary>
    /// Tries to parse a size string to a byte count.
    /// </summary>
    /// <param name="value">A non-negative integer optionally followed by a suffix: k, K, m, M, g, or G.</param>
    /// <param name="bytes">When this method returns <see langword="true"/>, the equivalent byte count; otherwise 0.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(string value, out long bytes)
    {
        bytes = 0;

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        long multiplier = 1;
        ReadOnlySpan<char> digits = value.AsSpan();

        char last = value[value.Length - 1];
        if (!char.IsDigit(last))
        {
            multiplier = char.ToLowerInvariant(last) switch
            {
                'k' => 1024L,
                'm' => 1024L * 1024,
                'g' => 1024L * 1024 * 1024,
                _ => -1
            };

            if (multiplier < 0)
            {
                return false;
            }

            digits = value.AsSpan(0, value.Length - 1);
        }

        if (digits.Length == 0)
        {
            return false;
        }

        // NumberStyles.None rejects leading signs, whitespace, and decimal points — exactly what we want.
        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long raw))
        {
            return false;
        }

        try
        {
            bytes = checked(raw * multiplier);
        }
        catch (OverflowException)
        {
            return false;
        }
        return true;
    }
}

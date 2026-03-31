#nullable enable

using System.Globalization;

namespace Winix.FileWalk;

/// <summary>
/// Parses human-friendly duration strings (e.g. "30s", "5m", "1h", "7d", "2w") to <see cref="TimeSpan"/>.
/// A suffix is required: s (seconds), m (minutes), h (hours), d (days), w (weeks).
/// The numeric part must be a non-negative integer with no leading sign or decimal point.
/// </summary>
public static class DurationParser
{
    /// <summary>
    /// Parses a duration string to a <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="value">A non-negative integer followed by a required suffix: s, m, h, d, or w.</param>
    /// <returns>The equivalent <see cref="TimeSpan"/>.</returns>
    /// <exception cref="FormatException">Thrown when <paramref name="value"/> is empty, has no suffix, uses an unrecognised suffix, contains non-digit characters in the numeric part, or is negative.</exception>
    public static TimeSpan Parse(string value)
    {
        if (!TryParse(value, out TimeSpan duration))
        {
            throw new FormatException($"Invalid duration: '{value}'. Expected a non-negative integer followed by s, m, h, d, or w.");
        }
        return duration;
    }

    /// <summary>
    /// Tries to parse a duration string to a <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="value">A non-negative integer followed by a required suffix: s, m, h, d, or w.</param>
    /// <param name="duration">When this method returns <see langword="true"/>, the equivalent <see cref="TimeSpan"/>; otherwise <see cref="TimeSpan.Zero"/>.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(string value, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;

        // Need at least one digit and one suffix character.
        if (string.IsNullOrEmpty(value) || value.Length < 2)
        {
            return false;
        }

        char suffix = value[value.Length - 1];
        ReadOnlySpan<char> digits = value.AsSpan(0, value.Length - 1);

        // NumberStyles.None rejects leading signs, whitespace, and decimal points — exactly what we want.
        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long raw))
        {
            return false;
        }

        // Use MinValue as a sentinel for an unrecognised suffix; it can't arise from valid input.
        duration = suffix switch
        {
            's' => TimeSpan.FromSeconds(raw),
            'm' => TimeSpan.FromMinutes(raw),
            'h' => TimeSpan.FromHours(raw),
            'd' => TimeSpan.FromDays(raw),
            'w' => TimeSpan.FromDays(raw * 7),
            _ => TimeSpan.MinValue
        };

        if (duration == TimeSpan.MinValue)
        {
            duration = TimeSpan.Zero;
            return false;
        }

        return true;
    }
}

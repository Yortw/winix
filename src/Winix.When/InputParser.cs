// src/Winix.When/InputParser.cs
using System.Globalization;

namespace Winix.When;

/// <summary>
/// Detects and parses timestamp input formats. Returns a <see cref="DateTimeOffset"/>.
/// Parsing follows a priority order: <c>now</c> keyword, Unix epoch, ISO 8601,
/// space-separated ISO-like, named-month formats. Ambiguous numeric-only formats
/// (e.g. <c>06/12/2024</c>) are rejected.
/// </summary>
public static class InputParser
{
    private static readonly string[] NamedMonthFormats = new[]
    {
        "MMM d yyyy",
        "MMM dd yyyy",
        "d MMM yyyy",
        "dd MMM yyyy",
        "MMM d, yyyy",
        "MMM dd, yyyy",
    };

    /// <summary>
    /// Returns true if the input is the "now" keyword (case-insensitive).
    /// </summary>
    public static bool IsNow(string input)
    {
        return input.Equals("now", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses a timestamp string, trying formats in priority order.
    /// When input is "now", returns <see cref="DateTimeOffset.MinValue"/> as a sentinel —
    /// the caller should substitute the actual current time.
    /// </summary>
    /// <param name="input">The raw timestamp string supplied by the user.</param>
    /// <param name="result">
    /// The parsed timestamp on success, or <see cref="DateTimeOffset.MinValue"/> for the "now" sentinel.
    /// </param>
    /// <param name="error">A human-readable error message on failure; null on success.</param>
    /// <returns>True if parsing succeeded; false otherwise.</returns>
    public static bool TryParse(string input, out DateTimeOffset result, out string? error)
    {
        result = default;
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Empty input.";
            return false;
        }

        // 1. "now" keyword
        if (IsNow(input))
        {
            result = DateTimeOffset.MinValue;
            return true;
        }

        // Reject ambiguous formats before numeric parse
        if (IsAmbiguousDateFormat(input))
        {
            error = $"Cannot parse '{input}' — ambiguous date format. Use ISO 8601: 2024-06-12 or 2024-12-06";
            return false;
        }

        // 2. Unix epoch
        if (TryParseEpoch(input, out result, out error))
        {
            return true;
        }
        if (error != null)
        {
            return false;
        }

        // 3. ISO 8601 datetime
        if (TryParseIso8601(input, out result))
        {
            return true;
        }

        // 4. Space-separated ISO-like
        if (TryParseSpaceSeparated(input, out result))
        {
            return true;
        }

        // 5. Named-month formats
        if (TryParseNamedMonth(input, out result))
        {
            return true;
        }

        error = $"Cannot parse '{input}'. Supported formats: Unix epoch, ISO 8601, 'YYYY-MM-DD HH:MM:SS', 'Jun 18 2024', or 'now'.";
        return false;
    }

    private static bool IsAmbiguousDateFormat(string input)
    {
        if (input.Contains('/'))
        {
            ReadOnlySpan<char> s = input.AsSpan();
            bool allDigitsOrSlash = true;
            int slashCount = 0;
            foreach (char c in s)
            {
                if (c == '/') { slashCount++; }
                else if (c < '0' || c > '9') { allDigitsOrSlash = false; break; }
            }
            if (allDigitsOrSlash && slashCount == 2)
            {
                return true;
            }
        }

        if (input.Contains('-') && input.Length >= 8 && input.Length <= 10)
        {
            int firstDash = input.IndexOf('-');
            if (firstDash >= 1 && firstDash <= 2)
            {
                ReadOnlySpan<char> s = input.AsSpan();
                bool allDigitsOrDash = true;
                int dashCount = 0;
                foreach (char c in s)
                {
                    if (c == '-') { dashCount++; }
                    else if (c < '0' || c > '9') { allDigitsOrDash = false; break; }
                }
                if (allDigitsOrDash && dashCount == 2)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryParseEpoch(string input, out DateTimeOffset result, out string? error)
    {
        result = default;
        error = null;

        // Decimal point always means fractional seconds regardless of magnitude
        if (input.Contains('.'))
        {
            if (double.TryParse(input, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out double decimalEpoch))
            {
                try
                {
                    result = DateTimeOffset.UnixEpoch.AddSeconds(decimalEpoch);
                    return true;
                }
                catch (ArgumentOutOfRangeException)
                {
                    error = $"Epoch value '{input}' is out of range.";
                    return false;
                }
            }
            return false;
        }

        if (!long.TryParse(input, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long epoch))
        {
            return false;
        }

        // Negative values are always seconds — no millisecond ambiguity
        if (epoch < 0)
        {
            try
            {
                result = DateTimeOffset.UnixEpoch.AddSeconds(epoch);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                error = $"Epoch value '{input}' is out of range.";
                return false;
            }
        }

        // ≤10 digits: seconds (covers 0 through 9,999,999,999 — year 2286)
        if (epoch <= 9_999_999_999L)
        {
            try
            {
                result = DateTimeOffset.UnixEpoch.AddSeconds(epoch);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                error = $"Epoch value '{input}' is out of range.";
                return false;
            }
        }

        // 11-13 digits: milliseconds (covers up to 9,999,999,999,999 ms — year 2286)
        if (epoch <= 9_999_999_999_999L)
        {
            try
            {
                result = DateTimeOffset.UnixEpoch.AddMilliseconds(epoch);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                error = $"Epoch value '{input}' is out of range.";
                return false;
            }
        }

        // 14+ digits: reject — too large to be a valid epoch in seconds or milliseconds
        error = $"Epoch value '{input}' is out of range (max 13 digits for milliseconds).";
        return false;
    }

    private static bool TryParseIso8601(string input, out DateTimeOffset result)
    {
        // Date-only: YYYY-MM-DD — treat as midnight UTC explicitly to avoid local-time assumption
        if (input.Length == 10 && input[4] == '-' && input[7] == '-' && !input.Contains('T'))
        {
            if (DateTimeOffset.TryParseExact(input, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result))
            {
                return true;
            }
        }

        if (input.Contains('T'))
        {
            if (HasExplicitOffset(input))
            {
                // Explicit offset present (Z, +HH:MM, -HH:MM) — parse without assumptions
                if (DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out result))
                {
                    return true;
                }
            }
            else
            {
                // No offset — treat as UTC per design decision; AssumeUniversal alone is sufficient
                if (DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out result))
                {
                    return true;
                }
            }
        }

        result = default;
        return false;
    }

    private static bool TryParseSpaceSeparated(string input, out DateTimeOffset result)
    {
        result = default;
        if (!input.Contains(' '))
        {
            return false;
        }

        if (HasExplicitOffset(input))
        {
            // Explicit offset present (e.g. "2024-06-18 20:00:00+12:00") — parse as-is
            if (DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out result))
            {
                return true;
            }
        }
        else
        {
            // No offset — treat as UTC per design decision
            if (DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out result))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true if the input string contains an explicit UTC/offset indicator
    /// (trailing 'Z', or a '+'/'-' sign after the time component).
    /// Used to decide whether to apply AssumeUniversal or parse as-is.
    /// </summary>
    private static bool HasExplicitOffset(string input)
    {
        // Z suffix
        if (input.EndsWith('Z') || input.EndsWith('z'))
        {
            return true;
        }

        // +HH:MM or -HH:MM somewhere after the date (position 10+)
        // Scan from the right for + or -, stopping before the date/time digits
        for (int i = input.Length - 1; i >= 10; i--)
        {
            char c = input[i];
            if (c == '+' || c == '-')
            {
                return true;
            }
            // Stop scanning if we hit a letter (like 'T') or a space — offset must be at the end
            if (c == 'T' || c == ' ')
            {
                break;
            }
        }

        return false;
    }

    private static bool TryParseNamedMonth(string input, out DateTimeOffset result)
    {
        if (DateTimeOffset.TryParseExact(input, NamedMonthFormats,
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out result))
        {
            return true;
        }

        result = default;
        return false;
    }
}

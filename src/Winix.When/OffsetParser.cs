using System.Globalization;
using Yort.ShellKit;

namespace Winix.When;

/// <summary>
/// Parses duration/offset strings (without sign prefix — the caller strips +/-).
/// Tries in order: ShellKit simple duration, ISO 8601 duration, .NET TimeSpan, HH:MM:SS.
/// </summary>
public static class OffsetParser
{
    private static readonly string[] TimeSpanFormats = new[] { "c", "g", "G" };

    /// <summary>
    /// Parses a duration string, trying all supported formats.
    /// </summary>
    /// <param name="input">The duration string to parse (without a leading +/- sign).</param>
    /// <param name="result">The parsed duration on success; <see cref="TimeSpan.Zero"/> on failure.</param>
    /// <param name="error">A human-readable error message on failure; <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// Formats tried in order:
    /// <list type="number">
    ///   <item>ShellKit simple duration: <c>7d</c>, <c>3h</c>, <c>500ms</c>, <c>2w</c></item>
    ///   <item>ISO 8601 duration: <c>P3DT4H12M</c>, <c>PT1H30M</c></item>
    ///   <item>.NET TimeSpan exact formats: <c>1.02:30:00</c></item>
    ///   <item>General TimeSpan parse: <c>01:30:00</c></item>
    /// </list>
    /// ISO inputs (starting with 'P') short-circuit on parse failure rather than falling through
    /// to TimeSpan, which cannot interpret ISO syntax.
    /// </remarks>
    public static bool TryParse(string input, out TimeSpan result, out string? error)
    {
        result = TimeSpan.Zero;
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Empty offset.";
            return false;
        }

        // 1. ShellKit simple duration (7d, 3h, 500ms, 2w) — single chunk only.
        if (DurationParser.TryParse(input, out result))
        {
            return true;
        }

        // 2. Combined shorthand (1d12h, 2h30m, 1d12h30m45s) — multiple [digits][suffix] chunks.
        //    DurationParser is single-chunk only; this fills the gap that was documented but
        //    never implemented. Suffixes match DurationParser: ms, s, m, h, d, w.
        if (TryParseCombinedShorthand(input, out result))
        {
            return true;
        }

        // 3. ISO 8601 duration (P3DT4H12M) — short-circuit on failure because TimeSpan.TryParse
        //    cannot make sense of ISO syntax and would produce a spurious error message.
        if (input[0] == 'P')
        {
            if (IsoDurationParser.TryParse(input, out result, out string? isoError))
            {
                return true;
            }
            error = isoError;
            return false;
        }

        // 3 & 4. TimeSpan formats (1.02:30:00, 01:30:00).
        // Guard: TimeSpan.TryParseExact("c") and TryParse both accept a bare integer as a day count
        // (e.g. "42" → 42 days). That silently swallows typos — require at least one colon so the
        // caller's intent is unambiguous.
        if (input.Contains(':'))
        {
            if (TimeSpan.TryParseExact(input, TimeSpanFormats, CultureInfo.InvariantCulture, out result))
            {
                return true;
            }
            if (TimeSpan.TryParse(input, CultureInfo.InvariantCulture, out result))
            {
                return true;
            }
        }

        error = $"Cannot parse offset '{input}'. Supported: 7d, 3h, 500ms, 1d12h30m, P3DT4H12M, 1.02:30:00, 01:30:00.";
        return false;
    }

    // Parses combined shorthand like "1d12h30m" by scanning [digits][suffix] chunks and summing.
    // Requires at least two chunks (single-chunk inputs go through DurationParser).
    // Each chunk's digits must parse as a non-negative integer; suffixes are: ms, s, m, h, d, w.
    // Whole-input failure (overflow, malformed chunk, unknown suffix) returns false with
    // result=Zero; the caller falls through to the next format or to the user-facing error.
    private static bool TryParseCombinedShorthand(string input, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        if (input.Length < 4) return false; // shortest combined: "1d2h" = 4 chars

        TimeSpan total = TimeSpan.Zero;
        int chunkCount = 0;
        int i = 0;

        while (i < input.Length)
        {
            int digitStart = i;
            while (i < input.Length && char.IsDigit(input[i])) { i++; }
            if (i == digitStart) return false;
            if (i >= input.Length) return false;

            int suffixLen;
            if (input[i] == 'm' && i + 1 < input.Length && input[i + 1] == 's')
            {
                suffixLen = 2;
            }
            else if (input[i] is 's' or 'm' or 'h' or 'd' or 'w')
            {
                suffixLen = 1;
            }
            else
            {
                return false;
            }

            string chunk = input.Substring(digitStart, (i - digitStart) + suffixLen);
            if (!DurationParser.TryParse(chunk, out TimeSpan chunkSpan)) return false;

            try { total += chunkSpan; }
            catch (OverflowException) { return false; }

            i += suffixLen;
            chunkCount++;
        }

        if (chunkCount < 2) return false;
        result = total;
        return true;
    }
}

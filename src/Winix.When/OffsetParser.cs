#nullable enable
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

        // 1. ShellKit simple duration (7d, 3h, 500ms, 2w)
        if (DurationParser.TryParse(input, out result))
        {
            return true;
        }

        // 2. ISO 8601 duration (P3DT4H12M) — short-circuit on failure because TimeSpan.TryParse
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

        error = $"Cannot parse offset '{input}'. Supported: 7d, 3h, 500ms, P3DT4H12M, 1.02:30:00, 01:30:00.";
        return false;
    }
}

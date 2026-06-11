#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Winix.Online;

/// <summary>
/// An HTTP status-code matcher parsed from a <c>--status</c> spec. Supports a class shorthand
/// (<c>2xx</c>), explicit codes (<c>200,204</c>), inclusive ranges (<c>200-299</c>), and any
/// comma-separated mix of those. Default is <c>2xx</c>.
/// </summary>
public sealed class StatusSpec
{
    private readonly IReadOnlyList<(int Lo, int Hi)> _ranges;

    private StatusSpec(IReadOnlyList<(int, int)> ranges)
    {
        _ranges = ranges;
    }

    /// <summary>The default spec: any 2xx status (200–299).</summary>
    public static StatusSpec Default { get; } = new(new (int, int)[] { (200, 299) });

    /// <summary>Returns <see langword="true"/> when <paramref name="code"/> falls in any parsed range.</summary>
    public bool Matches(int code)
    {
        foreach ((int lo, int hi) in _ranges)
        {
            if (code >= lo && code <= hi)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Parses a status spec. On failure, <paramref name="error"/> describes the offending token and
    /// the method returns <see langword="false"/> (caller maps to a usage error).
    /// </summary>
    public static bool TryParse(string spec, out StatusSpec result, out string? error)
    {
        result = Default;
        error = null;
        var ranges = new List<(int, int)>();

        foreach (string token in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Class shorthand: a single digit 1-5 followed by "xx" (case-insensitive).
            if (token.Length == 3 && token[0] >= '1' && token[0] <= '5'
                && (token[1] == 'x' || token[1] == 'X') && (token[2] == 'x' || token[2] == 'X'))
            {
                int hundreds = (token[0] - '0') * 100;
                ranges.Add((hundreds, hundreds + 99));
                continue;
            }

            int dash = token.IndexOf('-');
            if (dash > 0 && dash < token.Length - 1)
            {
                string loText = token.Substring(0, dash);
                string hiText = token.Substring(dash + 1);
                if (TryCode(loText, out int lo) && TryCode(hiText, out int hi) && lo <= hi)
                {
                    ranges.Add((lo, hi));
                    continue;
                }
                error = $"invalid status range '{token}'";
                return false;
            }

            if (TryCode(token, out int code))
            {
                ranges.Add((code, code));
                continue;
            }

            error = $"invalid status '{token}'";
            return false;
        }

        if (ranges.Count == 0)
        {
            error = "empty status spec";
            return false;
        }

        result = new StatusSpec(ranges);
        return true;
    }

    private static bool TryCode(string text, out int code)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out code)
            && code >= 100 && code <= 599)
        {
            return true;
        }
        code = 0;
        return false;
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Winix.NetCat;

/// <summary>
/// Parses port specifiers like "80", "80,443,8080", "80-1000", or mixed
/// "80-100,443,8080-8090" into a list of <see cref="PortRange"/> values.
/// </summary>
public static class PortRangeParser
{
    /// <summary>
    /// Parses the specifier string. Throws <see cref="FormatException"/> with a
    /// human-readable message if the specifier is malformed or contains a port
    /// outside 1-65535 or a range with low &gt; high.
    /// </summary>
    /// <param name="specifier">e.g. "80", "80,443", "80-1000", "80-100,443,8080-8090".</param>
    public static IReadOnlyList<PortRange> Parse(string specifier)
    {
        if (string.IsNullOrWhiteSpace(specifier))
        {
            throw new FormatException("Port specifier must not be empty.");
        }

        var ranges = new List<PortRange>();
        string[] segments = specifier.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (string segment in segments)
        {
            ranges.Add(ParseSegment(segment.Trim()));
        }
        if (ranges.Count == 0)
        {
            throw new FormatException("Port specifier must contain at least one port.");
        }
        return ranges;
    }

    private static PortRange ParseSegment(string segment)
    {
        int dashIndex = segment.IndexOf('-');
        if (dashIndex < 0)
        {
            int port = ParsePort(segment, segment);
            return new PortRange(port);
        }

        string lowText = segment.Substring(0, dashIndex);
        string highText = segment.Substring(dashIndex + 1);
        int low = ParsePort(lowText, segment);
        int high = ParsePort(highText, segment);
        if (low > high)
        {
            throw new FormatException($"Port range \"{segment}\" has low > high.");
        }
        return new PortRange(low, high);
    }

    private static int ParsePort(string text, string sourceSegment)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port))
        {
            throw new FormatException($"Invalid port \"{text}\" in \"{sourceSegment}\".");
        }
        if (port < 1 || port > 65535)
        {
            throw new FormatException($"Port \"{text}\" must be 1-65535.");
        }
        return port;
    }
}

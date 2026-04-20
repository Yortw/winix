#nullable enable
using System;
using System.Collections.Generic;

namespace Winix.Url;

/// <summary>Parses URL strings into <see cref="ParsedUrl"/>. Pure — no I/O.</summary>
public static class UrlParser
{
    /// <summary>Parse result: <see cref="Url"/> populated on success, <see cref="Error"/> populated on failure.</summary>
    public sealed record Result(ParsedUrl? Url, string? Error)
    {
        /// <summary>True if parsing succeeded.</summary>
        public bool Success => Url is not null;
    }

    /// <summary>Parse <paramref name="input"/> as an absolute URL; returns a <see cref="Result"/> with <see cref="Result.Error"/> populated on failure.</summary>
    public static Result Parse(string input)
    {
        try
        {
            var uri = new Uri(input, UriKind.Absolute);
            // Port normalisation: report null if the scheme has a known default and it matches.
            int? port = IsDefaultPort(uri.Scheme, uri.Port) ? null : uri.Port;
            string? userInfo = string.IsNullOrEmpty(uri.UserInfo) ? null : uri.UserInfo;
            string? fragment = string.IsNullOrEmpty(uri.Fragment)
                ? null
                : Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));

            var pairs = ParseQueryPairs(uri.Query);
            // Preserve the original query string (minus leading '?') so --field query returns
            // the exact URL-original bytes rather than our form-encoded re-serialisation. The
            // decoded QueryPairs are still available for JSON output and query get/set/delete.
            string rawQuery = string.IsNullOrEmpty(uri.Query)
                ? ""
                : (uri.Query.StartsWith('?') ? uri.Query.Substring(1) : uri.Query);

            return new Result(new ParsedUrl(
                Scheme: uri.Scheme,
                UserInfo: userInfo,
                Host: uri.Host,
                Port: port,
                Path: uri.AbsolutePath,
                QueryPairs: pairs,
                RawQuery: rawQuery,
                Fragment: fragment), null);
        }
        catch (UriFormatException ex)
        {
            return new Result(null, $"invalid URL: {ex.Message}");
        }
    }

    // Parse a query string (with or without leading '?') into ordered (key, value) tuples.
    // Duplicate keys preserved in order. Empty query → empty list.
    internal static IReadOnlyList<(string Key, string Value)> ParseQueryPairs(string query)
    {
        var pairs = new List<(string, string)>();
        if (string.IsNullOrEmpty(query))
        {
            return pairs;
        }
        string stripped = query.StartsWith('?') ? query.Substring(1) : query;
        if (stripped.Length == 0)
        {
            return pairs;
        }
        string[] parts = stripped.Split('&');
        foreach (string part in parts)
        {
            // Skip empty segments from adjacent separators (e.g. "?a=1&&b=2" → skip the middle).
            // Emitting ("","") pairs would round-trip as "=&=" — distortion, not preservation.
            if (part.Length == 0)
            {
                continue;
            }
            int eq = part.IndexOf('=');
            string key, value;
            if (eq < 0)
            {
                key = Uri.UnescapeDataString(part);
                value = "";
            }
            else
            {
                key = Uri.UnescapeDataString(part.Substring(0, eq));
                value = Uri.UnescapeDataString(part.Substring(eq + 1));
            }
            pairs.Add((key, value));
        }
        return pairs;
    }

    private static bool IsDefaultPort(string scheme, int port) => (scheme, port) switch
    {
        ("http", 80) => true,
        ("https", 443) => true,
        ("ftp", 21) => true,
        ("ssh", 22) => true,
        _ => false,
    };
}

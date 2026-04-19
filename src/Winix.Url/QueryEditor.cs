#nullable enable
using System.Collections.Generic;
using System.Text;

namespace Winix.Url;

/// <summary>Query-string editing: get / set / delete, preserving order of other keys.</summary>
public static class QueryEditor
{
    /// <summary>Result carrying either a value (get) or a URL (set/delete), or an error.</summary>
    public sealed record Result(string? Url, string? Value, string? Error)
    {
        /// <summary>True if the operation succeeded.</summary>
        public bool Success => Error is null;
    }

    /// <summary>Return the first value for <paramref name="key"/> in the URL's query.</summary>
    public static Result Get(string url, string key)
    {
        var parse = UrlParser.Parse(url);
        if (!parse.Success)
        {
            return new Result(null, null, parse.Error);
        }
        foreach (var (k, v) in parse.Url!.QueryPairs)
        {
            if (k == key)
            {
                return new Result(null, v, null);
            }
        }
        return new Result(null, null, $"key not found: {key}");
    }

    /// <summary>Set <paramref name="key"/> to <paramref name="value"/>. Replaces all existing occurrences; appends if absent.</summary>
    public static Result Set(string url, string key, string value, bool raw)
    {
        var parse = UrlParser.Parse(url);
        if (!parse.Success)
        {
            return new Result(null, null, parse.Error);
        }
        var updated = new List<(string, string)>();
        bool replaced = false;
        foreach (var (k, v) in parse.Url!.QueryPairs)
        {
            if (k == key)
            {
                if (!replaced)
                {
                    updated.Add((key, value));
                    replaced = true;
                }
                // else: skip additional duplicates (collapse)
            }
            else
            {
                updated.Add((k, v));
            }
        }
        if (!replaced)
        {
            updated.Add((key, value));
        }
        return SpliceQuery(parse.Url, updated, raw);
    }

    /// <summary>Delete all occurrences of <paramref name="key"/>. No-op if key absent (still success).</summary>
    public static Result Delete(string url, string key, bool raw)
    {
        var parse = UrlParser.Parse(url);
        if (!parse.Success)
        {
            return new Result(null, null, parse.Error);
        }
        var updated = new List<(string, string)>();
        foreach (var (k, v) in parse.Url!.QueryPairs)
        {
            if (k != key)
            {
                updated.Add((k, v));
            }
        }
        return SpliceQuery(parse.Url, updated, raw);
    }

    /// <summary>Serialise (key, value) pairs to a form-encoded query string (without leading '?'). Empty list → empty string.</summary>
    public static string SerialiseQuery(IReadOnlyList<(string Key, string Value)> pairs)
    {
        if (pairs.Count == 0)
        {
            return "";
        }
        var sb = new StringBuilder();
        for (int i = 0; i < pairs.Count; i++)
        {
            if (i > 0) sb.Append('&');
            sb.Append(Encoder.Encode(pairs[i].Key, EncodeMode.Form, form: true));
            sb.Append('=');
            sb.Append(Encoder.Encode(pairs[i].Value, EncodeMode.Form, form: true));
        }
        return sb.ToString();
    }

    // Rebuild the URL with the edited query, delegating to UrlBuilder for consistent normalisation.
    // UserInfo preserved — losing credentials silently during a query edit would be a data-loss bug.
    private static Result SpliceQuery(ParsedUrl original, IReadOnlyList<(string, string)> newQuery, bool raw)
    {
        var build = UrlBuilder.Build(
            scheme: original.Scheme,
            host: original.Host,
            port: original.Port,
            path: original.Path,
            query: newQuery,
            fragment: original.Fragment,
            raw: raw,
            userInfo: original.UserInfo);
        if (!build.Success)
        {
            return new Result(null, null, build.Error);
        }
        return new Result(build.Url, null, null);
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace Winix.Url;

/// <summary>Query-string editing: get / set / delete, preserving order of other keys.</summary>
public static class QueryEditor
{
    /// <summary>Result carrying either a value (get) or a URL (set/delete), or an error.</summary>
    /// <remarks>
    /// Round-1 review SFH-I1 — <see cref="AffectedCount"/> reports how many existing values for
    /// the supplied key were affected (replaced for Set, removed for Delete). The Cli layer
    /// emits a stderr warning when this is &gt; 1 so users are aware of HTTP duplicate-key
    /// silent-collapse semantics. 0 means key was absent (Set appended; Delete no-op).
    /// </remarks>
    public sealed record Result(string? Url, string? Value, string? Error, int AffectedCount = 0)
    {
        /// <summary>True if the operation succeeded.</summary>
        public bool Success => Error is null;
    }

    /// <summary>
    /// Round-1 review SFH-I2 — multi-value get result. Carries every value matching the
    /// requested key (in URL order), so the Cli can distinguish first-only output (default)
    /// from --all output, AND warn the user when duplicates exist.
    /// </summary>
    public sealed record GetManyResult(IReadOnlyList<string>? Values, string? Error)
    {
        /// <summary>True if at least one matching value was found.</summary>
        public bool Success => Error is null;
    }

    /// <summary>Return the first value for <paramref name="key"/> in the URL's query.</summary>
    public static Result Get(string url, string key)
    {
        var many = GetMany(url, key);
        if (!many.Success) return new Result(null, null, many.Error);
        return new Result(null, many.Values![0], null, AffectedCount: many.Values.Count);
    }

    /// <summary>Return every value matching <paramref name="key"/> in the URL's query, in URL order.</summary>
    public static GetManyResult GetMany(string url, string key)
    {
        var parse = UrlParser.Parse(url);
        if (!parse.Success)
        {
            return new GetManyResult(null, parse.Error);
        }
        // Round-1 review CR-I1 — UrlParser.ParseQueryPairs decodes both keys and values.
        // The user-supplied key may be in encoded form (e.g. copied verbatim from a URL),
        // so decode it before comparison so encoded-form and plain-text inputs match the
        // same parsed key. Without this, `query get URL "a%3Db"` would never match the
        // parsed key `a=b`, producing a spurious "key not found" for the canonical use
        // case of round-tripping a key through --field query.
        string decodedKey = Uri.UnescapeDataString(key);
        var hits = new List<string>();
        foreach (var (k, v) in parse.Url!.QueryPairs)
        {
            if (k == decodedKey)
            {
                hits.Add(v);
            }
        }
        if (hits.Count == 0)
        {
            return new GetManyResult(null, $"key not found: {key}");
        }
        return new GetManyResult(hits, null);
    }

    /// <summary>Set <paramref name="key"/> to <paramref name="value"/>. Replaces all existing occurrences; appends if absent.</summary>
    public static Result Set(string url, string key, string value, bool raw)
    {
        var parse = UrlParser.Parse(url);
        if (!parse.Success)
        {
            return new Result(null, null, parse.Error);
        }
        // CR-I1 — same key-decoding rule as Get/Delete. See GetMany.
        string decodedKey = Uri.UnescapeDataString(key);
        var updated = new List<(string, string)>();
        bool replaced = false;
        int matched = 0;
        foreach (var (k, v) in parse.Url!.QueryPairs)
        {
            if (k == decodedKey)
            {
                matched++;
                if (!replaced)
                {
                    updated.Add((decodedKey, value));
                    replaced = true;
                }
                // else: skip additional duplicates (collapse) — the Cli warns on AffectedCount > 1.
            }
            else
            {
                updated.Add((k, v));
            }
        }
        if (!replaced)
        {
            updated.Add((decodedKey, value));
        }
        var splice = SpliceQuery(parse.Url, updated, raw);
        if (!splice.Success) return splice;
        return new Result(splice.Url, null, null, AffectedCount: matched);
    }

    /// <summary>Delete all occurrences of <paramref name="key"/>. No-op if key absent (still success).</summary>
    public static Result Delete(string url, string key, bool raw)
    {
        var parse = UrlParser.Parse(url);
        if (!parse.Success)
        {
            return new Result(null, null, parse.Error);
        }
        // CR-I1 — same key-decoding rule as Get/Set. See GetMany.
        string decodedKey = Uri.UnescapeDataString(key);
        var updated = new List<(string, string)>();
        int matched = 0;
        foreach (var (k, v) in parse.Url!.QueryPairs)
        {
            if (k == decodedKey)
            {
                matched++;
            }
            else
            {
                updated.Add((k, v));
            }
        }
        var splice = SpliceQuery(parse.Url, updated, raw);
        if (!splice.Success) return splice;
        return new Result(splice.Url, null, null, AffectedCount: matched);
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

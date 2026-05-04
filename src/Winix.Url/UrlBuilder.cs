#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace Winix.Url;

/// <summary>Assembles URL strings from constituent parts. Pure — no I/O.</summary>
public static class UrlBuilder
{
    /// <summary>Result of a build attempt.</summary>
    public sealed record Result(string? Url, string? Error)
    {
        /// <summary>True if building succeeded.</summary>
        public bool Success => Url is not null;
    }

    /// <summary>Assemble a URL. <paramref name="host"/> is required (non-empty).</summary>
    /// <param name="scheme">Scheme; defaults to <c>https</c> if null.</param>
    /// <param name="host">Host (required).</param>
    /// <param name="port">Port, or null for scheme default.</param>
    /// <param name="path">Path; leading slash added if missing. Pre-encoded <c>%XX</c> triplets are preserved.</param>
    /// <param name="query">Ordered (key, value) pairs; form-encoded on serialisation.</param>
    /// <param name="fragment">Fragment (without leading '#'), or null.</param>
    /// <param name="raw">When true, skip normalisation (preserve default ports, case). Still validated for syntax.</param>
    /// <param name="userInfo">Optional userinfo ("user:pass") to inject between scheme and host. Passed through opaquely; the caller is expected to supply an already-encoded value (for round-tripping a ParsedUrl.UserInfo).</param>
    public static Result Build(
        string? scheme,
        string host,
        int? port,
        string? path,
        IReadOnlyList<(string Key, string Value)> query,
        string? fragment,
        bool raw,
        string? userInfo = null)
    {
        if (string.IsNullOrEmpty(host))
        {
            return new Result(null, "host is required");
        }

        // Round-1 review CR-I2 — reject host strings that contain URL component separators.
        // Without this, `--host "evil.com/@trusted.com"` produces `https://evil.com/@trusted.com/...`
        // where Uri normalisation silently parses the `@` as userinfo or splits at the `/`,
        // so the assembled URL's host is NOT what the user intended. This is a script-injection
        // concern when --host is fed from less-trusted input. We check the literal characters
        // first (clear error message), then defer to Uri.CheckHostName for general well-formedness.
        // Round-tripping a host with `[ipv6]` brackets is intentionally allowed — Uri.CheckHostName
        // accepts those for IPv6 hosts.
        foreach (char c in host)
        {
            if (c == '/' || c == '?' || c == '#' || c == '@' || c == ' ' || c == '\t' || c == '\r' || c == '\n')
            {
                return new Result(null,
                    $"--host contains a URL-component separator ('{(c == ' ' ? "space" : c == '\t' ? "tab" : c == '\r' ? "CR" : c == '\n' ? "LF" : c.ToString())}'); " +
                    "the host must not include path, query, fragment, userinfo, or whitespace characters");
            }
        }
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            return new Result(null, $"--host '{host}' is not a well-formed hostname");
        }

        scheme ??= "https";

        if (port is int p && (p < 1 || p > 65535))
        {
            return new Result(null, $"port must be in [1, 65535] (got {p})");
        }

        // Path: ensure leading slash; encode while preserving pre-encoded triplets.
        string encodedPath;
        if (string.IsNullOrEmpty(path))
        {
            encodedPath = "/";
        }
        else
        {
            string withLeading = path.StartsWith('/') ? path : "/" + path;
            encodedPath = Encoder.Encode(withLeading, EncodeMode.Path, form: false);
        }

        // Query: form-encode each key and value.
        string queryString = "";
        if (query.Count > 0)
        {
            var sb = new StringBuilder();
            sb.Append('?');
            for (int i = 0; i < query.Count; i++)
            {
                if (i > 0) sb.Append('&');
                sb.Append(Encoder.Encode(query[i].Key, EncodeMode.Form, form: true));
                sb.Append('=');
                sb.Append(Encoder.Encode(query[i].Value, EncodeMode.Form, form: true));
            }
            queryString = sb.ToString();
        }

        // Fragment: component-encode.
        string fragmentString = string.IsNullOrEmpty(fragment)
            ? ""
            : "#" + Encoder.Encode(fragment, EncodeMode.Component, form: false);

        // Port: include if explicitly given and not the scheme default (unless raw).
        string portString = "";
        if (port is int pp)
        {
            if (raw || !IsDefaultPort(scheme, pp))
            {
                portString = ":" + pp;
            }
        }

        string userInfoPart = string.IsNullOrEmpty(userInfo) ? "" : userInfo + "@";

        string result = $"{scheme}://{userInfoPart}{host}{portString}{encodedPath}{queryString}{fragmentString}";

        // Validate syntax via Uri.TryCreate. --raw keeps the original (unnormalised) string;
        // non-raw returns AbsoluteUri (canonical escaped form).
        if (!Uri.TryCreate(result, UriKind.Absolute, out Uri? parsed))
        {
            return new Result(null, "invalid URL: assembled components do not form a valid absolute URI");
        }
        return new Result(raw ? result : parsed.AbsoluteUri, null);
    }

    private static bool IsDefaultPort(string scheme, int port) => (scheme.ToLowerInvariant(), port) switch
    {
        ("http", 80) => true,
        ("https", 443) => true,
        ("ftp", 21) => true,
        ("ssh", 22) => true,
        _ => false,
    };
}

using System.Security.Cryptography;
using System.Text;

namespace Winix.MkAuth;

/// <summary>The OAuth 1.0a signature method (RFC 5849 §3.4).</summary>
public enum OAuth1SignatureMethod
{
    /// <summary>HMAC-SHA1 (the most widely deployed; e.g. Twitter's reference vector).</summary>
    HmacSha1,
    /// <summary>HMAC-SHA256.</summary>
    HmacSha256,
    /// <summary>PLAINTEXT (signature is the signing key verbatim; only safe over TLS).</summary>
    Plaintext,
}

/// <summary>Inputs for an OAuth 1.0a signature. Timestamp/Nonce are pre-resolved by the caller
/// (from <see cref="IClock"/>/<see cref="INonceSource"/> or explicit flags) so signing is pure.</summary>
public sealed class OAuth1Request
{
    /// <summary>HTTP method (case-insensitive; upper-cased into the base string).</summary>
    public required string Method { get; init; }

    /// <summary>The full request URL. Any query string is folded into the signature base parameters;
    /// the base-URL portion is normalized (lower-cased scheme/host, default port dropped, no
    /// fragment).</summary>
    public required string Url { get; init; }

    /// <summary>The OAuth consumer (client) key.</summary>
    public required string ConsumerKey { get; init; }

    /// <summary>The OAuth consumer (client) secret; forms the first half of the signing key.</summary>
    public required string ConsumerSecret { get; init; }

    /// <summary>The OAuth access token, or <c>null</c>/empty for two-legged OAuth (no token).</summary>
    public string? Token { get; init; }

    /// <summary>The OAuth token secret; forms the second half of the signing key. Empty when there
    /// is no token.</summary>
    public string TokenSecret { get; init; } = "";

    /// <summary>The signature method. Defaults to <see cref="OAuth1SignatureMethod.HmacSha1"/>.</summary>
    public OAuth1SignatureMethod SignatureMethod { get; init; } = OAuth1SignatureMethod.HmacSha1;

    /// <summary>Additional protocol parameters that participate in the signature (e.g. POST form
    /// body parameters). Supplied raw (not percent-encoded); encoded once during normalization.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> ExtraParams { get; init; } = Array.Empty<KeyValuePair<string, string>>();

    /// <summary>Optional protection realm. Emitted in the header but excluded from the signature
    /// base string (RFC 5849 §3.4.1.3.1).</summary>
    public string? Realm { get; init; }

    /// <summary>The <c>oauth_timestamp</c> value (seconds since the Unix epoch).</summary>
    public required long Timestamp { get; init; }

    /// <summary>The <c>oauth_nonce</c> value.</summary>
    public required string Nonce { get; init; }
}

/// <summary>The signing result: the computed base string (for diagnostics), the raw signature, and
/// the assembled <c>Authorization</c> header.</summary>
public sealed class OAuth1Result
{
    /// <summary>The signature base string that was signed (RFC 5849 §3.4.1). Useful for diagnosing
    /// signature mismatches against a server.</summary>
    public required string BaseString { get; init; }

    /// <summary>The computed signature (base64 for HMAC methods; the signing key itself for
    /// PLAINTEXT). This is the raw value before percent-encoding into the header.</summary>
    public required string Signature { get; init; }

    /// <summary>The assembled <c>Authorization: OAuth ...</c> header.</summary>
    public required HeaderResult Header { get; init; }
}

/// <summary>OAuth 1.0a request signer (RFC 5849, §3.4). Pure given the pre-resolved
/// timestamp/nonce on the request.</summary>
public static class OAuth1Signer
{
    /// <summary>
    /// Computes the OAuth 1.0a signature for <paramref name="req"/> and assembles the
    /// <c>Authorization</c> header. The signature base string is built from the upper-cased method,
    /// the normalized base URL, and the percent-encoded, sorted, double-encoded parameter block
    /// (URL query + <see cref="OAuth1Request.ExtraParams"/> + the <c>oauth_*</c> params, excluding
    /// <c>realm</c> and <c>oauth_signature</c>).
    /// </summary>
    /// <param name="req">The signing inputs.</param>
    /// <returns>The base string, signature, and header.</returns>
    public static OAuth1Result Sign(OAuth1Request req)
    {
        var uri = new Uri(req.Url);

        // oauth_* params (signature method name on the wire)
        string sigMethodName = req.SignatureMethod switch
        {
            OAuth1SignatureMethod.HmacSha1 => "HMAC-SHA1",
            OAuth1SignatureMethod.HmacSha256 => "HMAC-SHA256",
            OAuth1SignatureMethod.Plaintext => "PLAINTEXT",
            _ => throw new ArgumentOutOfRangeException(nameof(req)),
        };

        var oauthParams = new List<KeyValuePair<string, string>>
        {
            new("oauth_consumer_key", req.ConsumerKey),
            new("oauth_nonce", req.Nonce),
            new("oauth_signature_method", sigMethodName),
            new("oauth_timestamp", req.Timestamp.ToString()),
            new("oauth_version", "1.0"),
        };
        if (!string.IsNullOrEmpty(req.Token))
        {
            oauthParams.Add(new("oauth_token", req.Token!));
        }

        // All params that go into the signature base: URL query + extra/body + oauth_* (NOT realm/signature)
        var allParams = new List<KeyValuePair<string, string>>();
        allParams.AddRange(ParseQuery(uri.Query));
        allParams.AddRange(req.ExtraParams);
        allParams.AddRange(oauthParams);

        string normalizedParams = string.Join("&", allParams
            .Select(p => new KeyValuePair<string, string>(PercentEncoder.Encode(p.Key), PercentEncoder.Encode(p.Value)))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ThenBy(p => p.Value, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={p.Value}"));

        string baseUrl = NormalizeBaseUrl(uri);
        string baseString = string.Concat(
            req.Method.ToUpperInvariant(), "&",
            PercentEncoder.Encode(baseUrl), "&",
            PercentEncoder.Encode(normalizedParams));

        string signingKey = $"{PercentEncoder.Encode(req.ConsumerSecret)}&{PercentEncoder.Encode(req.TokenSecret)}";

        string signature = req.SignatureMethod switch
        {
            OAuth1SignatureMethod.Plaintext => signingKey,
            OAuth1SignatureMethod.HmacSha1 => HmacBase64<HMACSHA1>(signingKey, baseString),
            OAuth1SignatureMethod.HmacSha256 => HmacBase64<HMACSHA256>(signingKey, baseString),
            _ => throw new ArgumentOutOfRangeException(nameof(req)),
        };

        // Header: realm (not signed) + oauth_* + oauth_signature, each value pct-encoded and quoted.
        var headerParams = new List<KeyValuePair<string, string>>();
        if (!string.IsNullOrEmpty(req.Realm))
        {
            headerParams.Add(new("realm", req.Realm!));
        }
        headerParams.AddRange(oauthParams);
        headerParams.Add(new("oauth_signature", signature));

        string headerValue = "OAuth " + string.Join(", ",
            headerParams.Select(p => $"{PercentEncoder.Encode(p.Key)}=\"{PercentEncoder.Encode(p.Value)}\""));

        return new OAuth1Result
        {
            BaseString = baseString,
            Signature = signature,
            Header = new HeaderResult("Authorization", headerValue, baseString),
        };
    }

    private static string HmacBase64<T>(string key, string data) where T : HMAC, new()
    {
        using var h = new T { Key = Encoding.UTF8.GetBytes(key) };
        return Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(data)));
    }

    private static string NormalizeBaseUrl(Uri uri)
    {
        string scheme = uri.Scheme.ToLowerInvariant();
        string host = uri.Host.ToLowerInvariant();
        bool defaultPort = (scheme == "http" && uri.Port == 80) || (scheme == "https" && uri.Port == 443);
        string authority = defaultPort ? host : $"{host}:{uri.Port}";
        return $"{scheme}://{authority}{uri.AbsolutePath}";
    }

    private static IEnumerable<KeyValuePair<string, string>> ParseQuery(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            yield break;
        }
        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            // Query arrives percent-encoded on the URL; decode so it is encoded exactly once in the base string.
            yield return eq < 0
                ? new(Uri.UnescapeDataString(pair), "")
                : new(Uri.UnescapeDataString(pair.Substring(0, eq)), Uri.UnescapeDataString(pair.Substring(eq + 1)));
        }
    }
}

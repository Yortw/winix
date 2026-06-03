using System.Security.Cryptography;
using System.Text;

namespace Winix.MkAuth;

/// <summary>
/// Azure Storage Shared Key signing inputs (Blob/Queue/File). The header values supplied here must
/// match exactly what the HTTP client actually sends, because the <c>x-ms-*</c> headers and the
/// content headers participate in the signature; any divergence between the signed request and the
/// transmitted request yields a 403 at the service.
/// </summary>
public sealed class AzureStorageRequest
{
    /// <summary>The storage account name (the leading <c>/{account}</c> of the canonicalized resource
    /// and the principal in the <c>SharedKey {account}:{sig}</c> header).</summary>
    public required string Account { get; init; }

    /// <summary>The account access key, base64-encoded (the raw HMAC-SHA256 key after decoding).</summary>
    public required string KeyBase64 { get; init; }

    /// <summary>HTTP verb (case-insensitive; upper-cased into the StringToSign).</summary>
    public required string Method { get; init; }

    /// <summary>The full request URL. Its path becomes the canonicalized resource and its query is
    /// folded in (lower-cased names, ordinal-sorted, duplicate values comma-joined).</summary>
    public required string Url { get; init; }

    /// <summary>The value sent as the <c>x-ms-date</c> header (RFC 1123 GMT, e.g.
    /// <c>Fri, 26 Jun 2026 00:00:00 GMT</c>). When present the StringToSign <c>Date</c> slot is empty.</summary>
    public required string XmsDate { get; init; }

    /// <summary>The value sent as the <c>x-ms-version</c> header (e.g. <c>2021-08-06</c>). The version
    /// governs version-sensitive rules such as Content-Length being empty when zero.</summary>
    public required string XmsVersion { get; init; }

    /// <summary>Additional headers that participate in the signature: any further <c>x-ms-*</c>
    /// headers (folded into the canonicalized header block) and the fixed content headers
    /// (<c>Content-Encoding</c>, <c>Content-Language</c>, <c>Content-Length</c>, <c>Content-MD5</c>,
    /// <c>Content-Type</c>, the conditional headers, and <c>Range</c>). Keys are matched
    /// case-insensitively for the <c>x-ms-*</c> prefix test.</summary>
    public Dictionary<string, string> Headers { get; init; } = new();
}

/// <summary>The SharedKey signing result: the assembled <c>Authorization</c> header plus the
/// StringToSign that produced it (useful for diagnosing 403s).</summary>
public sealed class AzureStorageResult
{
    /// <summary>The exact StringToSign that was HMAC'd. Newline-delimited per the documented layout.</summary>
    public required string StringToSign { get; init; }

    /// <summary>The assembled <c>Authorization: SharedKey {account}:{signature}</c> header.</summary>
    public required HeaderResult Header { get; init; }
}

/// <summary>
/// Azure Storage Shared Key authorization for Blob/Queue/File (NOT Table, which uses a different
/// StringToSign; NOT SharedKeyLite). Implements the StringToSign layout from Microsoft's
/// "Authorize with Shared Key" documentation: the 12 fixed header slots, the canonicalized
/// <c>x-ms-*</c> header block, and the canonicalized resource. Verified byte-for-byte against
/// <c>StorageSharedKeyCredential</c> (Azure.Storage.Blobs) via an independent oracle during the
/// implementation spike.
/// </summary>
public static class AzureStorageSigner
{
    /// <summary>
    /// Computes the SharedKey signature for <paramref name="req"/> and assembles the
    /// <c>Authorization</c> header.
    /// </summary>
    /// <param name="req">The signing inputs. <c>x-ms-date</c> and <c>x-ms-version</c> are always
    /// included in the canonicalized header block; any extra <c>x-ms-*</c> entries in
    /// <see cref="AzureStorageRequest.Headers"/> are folded in (lower-cased, value-trimmed, ordinal
    /// sorted). The content headers and conditional headers are read from the same dictionary by
    /// their canonical names.</param>
    /// <returns>The StringToSign and the assembled header.</returns>
    /// <exception cref="FormatException">If <see cref="AzureStorageRequest.KeyBase64"/> is not valid base64.</exception>
    /// <exception cref="UriFormatException">If <see cref="AzureStorageRequest.Url"/> is not a valid absolute URI.</exception>
    public static AzureStorageResult Sign(AzureStorageRequest req)
    {
        var uri = new Uri(req.Url);

        // Canonicalized x-ms-* headers: lower-cased name, ordinal-sorted, value-trimmed, "name:value\n".
        // x-ms-date / x-ms-version are always present.
        var xms = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["x-ms-date"] = req.XmsDate,
            ["x-ms-version"] = req.XmsVersion,
        };
        foreach (var kv in req.Headers)
        {
            string name = kv.Key.ToLowerInvariant();
            if (name.StartsWith("x-ms-", StringComparison.Ordinal))
            {
                xms[name] = kv.Value.Trim();
            }
        }

        var canonicalizedHeaders = new StringBuilder();
        foreach (var kv in xms)
        {
            canonicalizedHeaders.Append(kv.Key).Append(':').Append(kv.Value).Append('\n');
        }

        // Content-Length is sent as "" (not "0") when zero for x-ms-version 2015-02-21 and later.
        string contentLength = Get(req, "Content-Length");
        if (contentLength.Length == 0 || contentLength == "0")
        {
            contentLength = "";
        }

        // The 12 fixed StringToSign slots. The Date slot is empty because x-ms-date is used instead.
        string stringToSign = string.Join("\n",
            req.Method.ToUpperInvariant(),
            Get(req, "Content-Encoding"),
            Get(req, "Content-Language"),
            contentLength,
            Get(req, "Content-MD5"),
            Get(req, "Content-Type"),
            "",                                   // Date (empty — x-ms-date is in the canonicalized headers)
            Get(req, "If-Modified-Since"),
            Get(req, "If-Match"),
            Get(req, "If-None-Match"),
            Get(req, "If-Unmodified-Since"),
            Get(req, "Range"))
            + "\n" + canonicalizedHeaders.ToString() + CanonicalizedResource(uri, req.Account);

        using var hmac = new HMACSHA256(Convert.FromBase64String(req.KeyBase64));
        string sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
        string headerValue = "SharedKey " + req.Account + ":" + sig;

        return new AzureStorageResult
        {
            StringToSign = stringToSign,
            Header = new HeaderResult("Authorization", headerValue, stringToSign),
        };
    }

    /// <summary>Reads a fixed header slot value (case-insensitive), returning "" when absent.</summary>
    private static string Get(AzureStorageRequest req, string header)
    {
        foreach (var kv in req.Headers)
        {
            if (string.Equals(kv.Key, header, StringComparison.OrdinalIgnoreCase))
            {
                return kv.Value;
            }
        }
        return "";
    }

    /// <summary>
    /// Builds the canonicalized resource string: <c>/{account}{path}</c>, then for each query
    /// parameter a <c>\n{lower-name}:{value}</c> line, names ordinal-sorted, duplicate values
    /// comma-joined (themselves sorted), values URL-decoded.
    /// </summary>
    private static string CanonicalizedResource(Uri uri, string account)
    {
        var sb = new StringBuilder();
        sb.Append('/').Append(account).Append(uri.AbsolutePath);

        string q = uri.Query.TrimStart('?');
        if (q.Length > 0)
        {
            var byName = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string pair in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                string name = (eq < 0 ? pair : pair.Substring(0, eq)).ToLowerInvariant();
                string val = eq < 0 ? "" : Uri.UnescapeDataString(pair.Substring(eq + 1));

                if (!byName.TryGetValue(name, out var values))
                {
                    values = new List<string>();
                    byName[name] = values;
                }
                values.Add(val);
            }

            foreach (var kv in byName)
            {
                kv.Value.Sort(StringComparer.Ordinal);
                sb.Append('\n').Append(kv.Key).Append(':').Append(string.Join(",", kv.Value));
            }
        }

        return sb.ToString();
    }
}

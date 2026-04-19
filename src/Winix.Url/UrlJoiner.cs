#nullable enable
using System;

namespace Winix.Url;

/// <summary>Resolves a relative URL reference against an absolute base (RFC 3986 §5). Pure — no I/O.</summary>
public static class UrlJoiner
{
    /// <summary>Result of a join attempt.</summary>
    public sealed record Result(string? Url, string? Error)
    {
        /// <summary>True if joining succeeded.</summary>
        public bool Success => Url is not null;
    }

    /// <summary>Resolve <paramref name="relative"/> against <paramref name="baseUrl"/>.</summary>
    /// <remarks>
    /// Handles dot-segments, absolute relatives, query-only refs, fragment-only refs,
    /// and protocol-relative URLs per RFC 3986 §5. <paramref name="baseUrl"/> must be absolute.
    /// </remarks>
    public static Result Join(string baseUrl, string relative)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? baseUri))
        {
            return new Result(null, "base URL must be absolute");
        }

        try
        {
            var resolved = new Uri(baseUri, relative);
            // Use AbsoluteUri — ToString unescapes some characters for readability and can break round-tripping.
            return new Result(resolved.AbsoluteUri, null);
        }
        catch (UriFormatException ex)
        {
            return new Result(null, $"invalid URL: {ex.Message}");
        }
    }
}

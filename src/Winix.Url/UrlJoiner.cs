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
    /// <para>
    /// Handles dot-segments, absolute relatives, query-only refs, fragment-only refs,
    /// and protocol-relative URLs per RFC 3986 §5. <paramref name="baseUrl"/> must be absolute
    /// AND its scheme must appear explicitly in the input string itself.
    /// </para>
    /// <para>
    /// The "scheme appeared explicitly in the input" check is load-bearing for
    /// cross-platform consistency: <see cref="Uri.TryCreate(string, UriKind, out Uri)"/>
    /// silently auto-converts Unix-style absolute paths (<c>/foo</c> on Linux/macOS) and
    /// Windows drive paths (<c>C:\foo</c> on Windows) into <c>file://</c> URIs. Without
    /// this guard, "base URL must be absolute" would be platform-dependent and the CLI
    /// would silently accept local file paths as web bases.
    /// </para>
    /// </remarks>
    public static Result Join(string baseUrl, string relative)
    {
        // Both conditions must hold: TryCreate parses an absolute URI AND the parsed scheme
        // appeared verbatim in the input. The latter rejects Unix-path / Windows-drive auto-
        // conversions to file:// — the parsed Scheme would be "file" but the input wouldn't
        // start with "file:".
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? baseUri)
            || !baseUrl.StartsWith(baseUri.Scheme + ":", StringComparison.OrdinalIgnoreCase))
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

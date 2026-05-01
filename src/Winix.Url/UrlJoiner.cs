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
    /// and protocol-relative URLs per RFC 3986 §5. <paramref name="baseUrl"/> must be absolute
    /// AND must carry an explicit scheme prefix (RFC 3986 §3.1) — the explicit-scheme check is
    /// load-bearing for cross-platform consistency. <see cref="Uri.TryCreate(string, UriKind, out Uri)"/>
    /// with <see cref="UriKind.Absolute"/> silently treats Unix-style absolute paths like
    /// <c>/relative/base</c> as <c>file://</c> URIs on Linux/macOS hosts but rejects the same
    /// input on Windows. Without the scheme-prefix guard, "base URL must be absolute" would be
    /// platform-dependent.
    /// </remarks>
    public static Result Join(string baseUrl, string relative)
    {
        if (!HasExplicitScheme(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? baseUri))
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

    /// <summary>
    /// True if <paramref name="s"/> begins with a syntactically-valid URI scheme followed by ':'.
    /// RFC 3986 §3.1: <c>scheme = ALPHA *( ALPHA / DIGIT / "+" / "-" / "." )</c>.
    /// AOT/trim-friendly character scan; no regex.
    /// </summary>
    private static bool HasExplicitScheme(string s)
    {
        if (string.IsNullOrEmpty(s)) { return false; }
        int colonIndex = s.IndexOf(':');
        if (colonIndex < 1) { return false; } // must have at least one scheme char before the colon
        if (!char.IsAsciiLetter(s[0])) { return false; }
        for (int i = 1; i < colonIndex; i++)
        {
            char c = s[i];
            if (!char.IsAsciiLetterOrDigit(c) && c != '+' && c != '-' && c != '.')
            {
                return false;
            }
        }
        return true;
    }
}

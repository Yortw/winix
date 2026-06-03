#nullable enable

using Winix.Codec;

namespace Winix.MkAuth;

/// <summary>
/// base64url (RFC 4648 §5) without <c>=</c> padding — the JOSE encoding for JWT parts.
/// Delegates alphabet substitution to <see cref="Winix.Codec.Base64"/> (URL-safe mode)
/// then strips the trailing <c>=</c> pad chars.
/// </summary>
public static class Base64Url
{
    /// <summary>
    /// Encodes <paramref name="bytes"/> as base64url with no <c>=</c> padding.
    /// Uses <c>-</c> and <c>_</c> in place of <c>+</c> and <c>/</c> (RFC 4648 §5).
    /// </summary>
    public static string EncodeNoPad(byte[] bytes)
        => Base64.Encode(bytes, urlSafe: true).TrimEnd('=');
}

#nullable enable
namespace Winix.Digest;

/// <summary>Output encoding for hash bytes.</summary>
public enum OutputFormat
{
    /// <summary>Hex encoding. Lowercase by default; <see cref="DigestOptions.Uppercase"/> produces uppercase.</summary>
    Hex,

    /// <summary>Base64 with standard alphabet (RFC 4648 §4).</summary>
    Base64,

    /// <summary>Base64 URL-safe variant (RFC 4648 §5).</summary>
    Base64Url,

    /// <summary>Crockford base32 (uppercase, no padding).</summary>
    Base32,
}

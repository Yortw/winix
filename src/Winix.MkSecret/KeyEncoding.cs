namespace Winix.MkSecret;

/// <summary>Output encoding for <see cref="SecretMode.Key"/>.</summary>
public enum KeyEncoding
{
    /// <summary>Lowercase hex.</summary>
    Hex,
    /// <summary>Standard base64 (RFC 4648 §4), padded.</summary>
    Base64,
    /// <summary>URL-safe base64 (RFC 4648 §5), padding stripped.</summary>
    Base64Url,
    /// <summary>Crockford base32 (uppercase, unpadded, ambiguity-free).</summary>
    Base32,
}

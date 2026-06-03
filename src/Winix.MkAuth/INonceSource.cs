namespace Winix.MkAuth;

/// <summary>Nonce source seam (OAuth 1.0a <c>oauth_nonce</c>) so nonce values are pinnable under test.</summary>
public interface INonceSource
{
    /// <summary>Returns a fresh nonce string. Must be unique across requests within a short time window.</summary>
    string NextNonce();
}

/// <summary>
/// Production nonce — 16 random bytes from the OS CSPRNG, hex-encoded (32 lowercase chars).
/// URL-safe and opaque to the server.
/// </summary>
public sealed class RandomNonceSource : INonceSource
{
    /// <inheritdoc/>
    public string NextNonce()
    {
        byte[] buf = new byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(buf);
        return Convert.ToHexString(buf).ToLowerInvariant();
    }
}

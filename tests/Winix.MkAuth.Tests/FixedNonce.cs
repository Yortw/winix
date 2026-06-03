using Winix.MkAuth;

/// <summary>
/// <see cref="INonceSource"/> test double that always returns the same pinned nonce string.
/// Use to make OAuth 1.0a signature tests deterministic.
/// </summary>
public sealed class FixedNonce(string nonce) : INonceSource
{
    /// <inheritdoc/>
    public string NextNonce() => nonce;
}

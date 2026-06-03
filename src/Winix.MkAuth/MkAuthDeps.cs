using Winix.SecretStore;

namespace Winix.MkAuth;

/// <summary>
/// Injectable dependencies for <see cref="Cli"/>. Defaults are the production seams.
/// Tests substitute <c>FixedClock</c>/<c>FixedNonce</c>/<c>InMemorySecretStore</c> for determinism.
/// </summary>
public sealed class MkAuthDeps
{
    /// <summary>Time source for OAuth 1.0a timestamps and JWT <c>iat</c>/<c>exp</c> claims. Defaults to <see cref="SystemClock"/>.</summary>
    public IClock Clock { get; init; } = new SystemClock();

    /// <summary>Nonce source for OAuth 1.0a <c>oauth_nonce</c>. Defaults to <see cref="RandomNonceSource"/>.</summary>
    public INonceSource Nonce { get; init; } = new RandomNonceSource();

    /// <summary>
    /// OS-native secret store for <c>vault:</c> secret references. <c>null</c> is acceptable when
    /// no <c>vault:</c> references are used; the resolver will throw if a <c>vault:</c> ref is
    /// encountered with no store wired.
    /// </summary>
    public ISecretStore? SecretStore { get; init; }
}

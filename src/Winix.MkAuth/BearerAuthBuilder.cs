namespace Winix.MkAuth;

/// <summary>RFC 6750 Bearer token header builder. Pure passthrough of the resolved token.</summary>
public static class BearerAuthBuilder
{
    /// <summary>
    /// Builds a Bearer <c>Authorization</c> header value from the supplied token.
    /// The token is passed through verbatim; callers are responsible for resolving it
    /// from the appropriate <see cref="SecretRef"/> before calling this method.
    /// </summary>
    /// <param name="token">The bearer token string (e.g. a JWT compact serialization).</param>
    /// <returns>A <see cref="HeaderResult"/> with <c>HeaderName="Authorization"</c>.</returns>
    public static HeaderResult Build(string token) => new("Authorization", $"Bearer {token}");
}

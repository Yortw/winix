namespace Winix.MkAuth;

/// <summary>
/// The computed HTTP Authorization header. <paramref name="BaseString"/> is populated only when
/// a signing scheme (OAuth 1.0a, JWT) ran with base-string / debug output enabled; it is
/// <c>null</c> for schemes that don't produce a signable base string (Basic, Bearer).
/// </summary>
public readonly record struct HeaderResult(string HeaderName, string HeaderValue, string? BaseString = null);

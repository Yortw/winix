#nullable enable

namespace Winix.MkAuth;

/// <summary>The auth schemes mkauth can compute, selected by positional[0].</summary>
public enum AuthScheme
{
    /// <summary>HTTP Basic (RFC 7617).</summary>
    Basic,

    /// <summary>HTTP Bearer (RFC 6750).</summary>
    Bearer,

    /// <summary>OAuth 1.0a request signing (RFC 5849).</summary>
    OAuth1,

    /// <summary>JSON Web Token (RFC 7519/7515).</summary>
    Jwt,

    /// <summary>Azure Storage SharedKey (Blob/Queue/File).</summary>
    AzureStorage,
}

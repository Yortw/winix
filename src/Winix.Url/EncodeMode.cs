#nullable enable
namespace Winix.Url;

/// <summary>Percent-encoding variant. Default <see cref="Component"/> matches JavaScript <c>encodeURIComponent</c>.</summary>
public enum EncodeMode
{
    /// <summary>RFC 3986 unreserved set only (A-Za-z0-9-._~); space → %20. Safe for any URL component.</summary>
    Component,
    /// <summary>Same alphabet as <see cref="Component"/> but <c>/</c> preserved between segments. For constructing paths.</summary>
    Path,
    /// <summary>Same alphabet as <see cref="Component"/>; differs from <see cref="Form"/> only on space handling.</summary>
    Query,
    /// <summary>application/x-www-form-urlencoded: component encoding, then space → <c>+</c>.</summary>
    Form,
}

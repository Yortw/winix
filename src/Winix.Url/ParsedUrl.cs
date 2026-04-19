#nullable enable
using System.Collections.Generic;

namespace Winix.Url;

/// <summary>Structured view of a URL; output of <see cref="UrlParser"/>, intermediate shape for building/editing.</summary>
/// <param name="Scheme">The scheme (e.g. "https"). Empty string for relative URLs.</param>
/// <param name="UserInfo">The userinfo (e.g. "user:pw"). Null if absent.</param>
/// <param name="Host">The host. Empty string for relative URLs.</param>
/// <param name="Port">The port number. Null if absent OR default for the scheme.</param>
/// <param name="Path">The path, including leading slash. Never null.</param>
/// <param name="QueryPairs">Ordered list of (key, value) tuples — preserves order AND duplicate keys.</param>
/// <param name="Fragment">The fragment (without leading '#'). Null if absent.</param>
public sealed record ParsedUrl(
    string Scheme,
    string? UserInfo,
    string Host,
    int? Port,
    string Path,
    IReadOnlyList<(string Key, string Value)> QueryPairs,
    string? Fragment);

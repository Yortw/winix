#nullable enable

namespace Winix.Url;

/// <summary>
/// Maps <see cref="System.UriFormatException"/> messages to project-controlled English.
/// </summary>
/// <remarks>
/// <para>
/// Necessary because under <c>InvariantGlobalization=true</c> (the default for AOT csprojs in
/// this suite), <c>ex.Message</c> on framework <c>UriFormatException</c> returns the raw
/// <c>SR</c> resource key (e.g. <c>net_uri_BadHostName</c>, <c>net_uri_EmptyUri</c>) instead
/// of localised English. Piping that token directly into user output leaks framework internals
/// — same defect class fixed in <c>qr</c> tier-2 round-2 (helper-payload validation +
/// I/O catches). Tier-2 re-verification on 2026-05-06 found this class still open in url.
/// </para>
/// <para>
/// Unknown messages are passed through with an "unmapped" prefix so that future .NET runtime
/// changes (new SR keys, renamed keys) surface visibly in user output rather than silently
/// regressing to the leak.
/// </para>
/// </remarks>
internal static class UriErrorMessage
{
    /// <summary>
    /// Returns a controlled English description for the given <see cref="System.UriFormatException"/> message.
    /// </summary>
    /// <param name="srMessageOrEnglish">
    /// The raw <c>ex.Message</c> from a <see cref="System.UriFormatException"/>. Under
    /// <c>InvariantGlobalization=true</c> this is an <c>SR</c> resource key like <c>net_uri_BadHostName</c>;
    /// on a localised framework it would be the localised English text.
    /// </param>
    /// <returns>A short, project-controlled English fragment describing the failure.</returns>
    public static string ToEnglish(string? srMessageOrEnglish)
    {
        string key = srMessageOrEnglish ?? "";
        return key switch
        {
            "net_uri_EmptyUri" => "URL is empty",
            "net_uri_BadFormat" => "URL has no scheme or is malformed",
            "net_uri_BadHostName" => "hostname is malformed or empty",
            "net_uri_BadPort" => "port number is out of range (0–65535)",
            "net_uri_PortOutOfRange" => "port number is out of range (0–65535)",
            "net_uri_BadScheme" => "scheme contains invalid characters",
            "net_uri_SchemeLimit" => "scheme is too long",
            "net_uri_BadAuthority" => "authority component is malformed",
            "net_uri_BadAuthorityTerminator" => "authority component is not terminated correctly",
            "net_uri_BadUserPassword" => "userinfo component is malformed",
            "net_uri_BadString" => "URL contains invalid characters",
            "net_uri_MustRootedPath" => "path must start with '/'",
            "net_uri_NotAbsolute" => "URL is not absolute",
            "net_uri_SizeLimit" => "URL exceeds the maximum size",
            _ => $"unrecognised URL format error ({key})",
        };
    }
}

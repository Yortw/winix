#nullable enable

using System.Text;

namespace Winix.MkAuth;

/// <summary>
/// RFC 3986 percent-encoding (the form OAuth 1.0a §3.6 requires). Only the unreserved set
/// <c>A-Z a-z 0-9 - . _ ~</c> is left literal; every other byte (of the UTF-8 encoding) becomes
/// <c>%XX</c> in upper-case hex. This is deliberately stricter than URL form-encoding (space is
/// <c>%20</c>, never <c>+</c>).
/// </summary>
public static class PercentEncoder
{
    private const string Unreserved =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";

    /// <summary>
    /// Percent-encodes <paramref name="value"/> per RFC 3986. Every byte of the UTF-8 encoding
    /// that is not in the unreserved set (<c>A-Z a-z 0-9 - . _ ~</c>) is escaped as <c>%XX</c>
    /// (upper-case hex). This is the encoding required by OAuth 1.0a §3.6.
    /// </summary>
    public static string Encode(string value)
    {
        // Iterate UTF-8 bytes (not chars) so multi-byte sequences are percent-encoded correctly.
        var sb = new StringBuilder(value.Length * 3);
        foreach (byte b in Encoding.UTF8.GetBytes(value))
        {
            char c = (char)b;
            if (Unreserved.IndexOf(c) >= 0)
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('%').Append(b.ToString("X2"));
            }
        }
        return sb.ToString();
    }
}

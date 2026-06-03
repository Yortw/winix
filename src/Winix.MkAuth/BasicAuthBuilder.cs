using System.Text;

namespace Winix.MkAuth;

/// <summary>RFC 7617 Basic authentication header builder.</summary>
public static class BasicAuthBuilder
{
    /// <summary>
    /// Builds a Basic <c>Authorization</c> header value from the supplied credentials.
    /// The user and password are encoded as UTF-8, joined with <c>:</c>, and base64-encoded per RFC 7617.
    /// </summary>
    /// <param name="user">The username. Must not contain <c>:</c>.</param>
    /// <param name="password">The password (may be empty, but not null).</param>
    /// <returns>A <see cref="HeaderResult"/> with <c>HeaderName="Authorization"</c>.</returns>
    /// <exception cref="ArgumentException">The username contains a colon (the user:password delimiter).</exception>
    public static HeaderResult Build(string user, string password)
    {
        if (user.Contains(':'))
        {
            throw new ArgumentException("A Basic-auth username cannot contain ':'.", nameof(user));
        }

        string token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));
        return new HeaderResult("Authorization", $"Basic {token}");
    }
}

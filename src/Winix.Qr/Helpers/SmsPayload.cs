#nullable enable
using System;

namespace Winix.Qr.Helpers;

/// <summary>Builds an <c>sms:</c> URI per RFC 5724.</summary>
public static class SmsPayload
{
    /// <summary>Build the SMS URI.</summary>
    /// <param name="number">Phone number (E.164 recommended). Required, non-empty.</param>
    /// <param name="message">Optional pre-composed message body.</param>
    /// <exception cref="ArgumentException">Number empty or contains characters disallowed by RFC 3966.</exception>
    /// <remarks>
    /// Round-1 review SFH-I3: number is sanitised via <see cref="TelPayload.SanitisePhoneNumber"/> —
    /// whitespace stripped, then validated against the RFC 3966 visible-element grammar.
    /// Garbage like <c>"+1 555 abc"</c> is rejected at parse time, so the user sees the failure
    /// before generating an unscannable QR.
    /// </remarks>
    public static string Build(string number, string? message)
    {
        if (string.IsNullOrEmpty(number))
        {
            throw new ArgumentException("Number must be non-empty.");
        }

        string sanitised = TelPayload.SanitisePhoneNumber(number);
        string uri = $"sms:{sanitised}";
        if (!string.IsNullOrEmpty(message))
        {
            uri += $"?body={Uri.EscapeDataString(message)}";
        }
        return uri;
    }
}
